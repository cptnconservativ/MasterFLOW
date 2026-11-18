using System.Globalization;
using System.Text;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Persistence;

/// <summary>
/// Puts solved HGL/EGL results back on an existing Civil 3D pipe network, matched by part name.
/// <para>
/// <see cref="AutoLisp"/> writes an AutoLISP file defining the command MFHGL. Loaded into the drawing that holds
/// the network (APPLOAD), it sets each matching pipe's HGL up/down, EGL up/down and flow rate through the Civil 3D
/// COM object, so the drawing's own pipe labels, profile-view HGL lines and tables show MasterFlow's values. It
/// looks for the network by name and, when none matches, asks before matching pipe names across all networks.
/// Several property names are tried for each value and the command reports which ones it could set, because the
/// COM names differ from the .NET API names and have not yet been confirmed on a real Civil 3D release.
/// The whole run is one undo step. Civil 3D structures have no HGL property; their results go to the CSV and
/// to the LandXML file's StructFlow and Feature data.
/// </para>
/// <see cref="Csv"/> writes every pipe and structure result as one CSV table keyed by part name (for Excel, a
/// Civil 3D Data Link table, or checking the drawing by hand).
/// Values are in the network's units (ft or m; cfs or m³/s); nothing is converted.
/// </summary>
public static class Civil3DWriteBack
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Property names tried in order on each Civil 3D pipe (COM first, then the .NET-style names).</summary>
    public static readonly IReadOnlyList<(string Value, string[] Names)> PipeProperties =
    [
        ("HGL up", ["HGLUp", "HydraulicGradeLineUp"]),
        ("HGL down", ["HGLDown", "HydraulicGradeLineDown"]),
        ("EGL up", ["EGLUp", "EnergyGradeLineUp"]),
        ("EGL down", ["EGLDown", "EnergyGradeLineDown"]),
        ("Flow rate", ["FlowRate"]),
    ];

    public static (string Lisp, IReadOnlyList<string> Warnings) AutoLisp(StormNetwork net, StormNetworkResult result, string? appVersion = null, DateTimeOffset? when = null)
    {
        var warnings = new List<string>();
        if (!result.Succeeded) throw new InvalidOperationException("The network did not solve; there are no results to write to Civil 3D.");
        var pipes = NetworkPartResults.Pipes(result);
        var (len, _, _) = NetworkPartResults.UnitLabels(net.Units);
        string flow = net.Units == UnitSystem.SI ? "m3/s" : "cfs";
        var t = when ?? DateTimeOffset.Now;
        foreach (var dup in pipes.GroupBy(p => p.Name).Where(g => g.Count() > 1))
            warnings.Add($"Pipe name \"{dup.Key}\" is used {dup.Count()} times; Civil 3D gets the first one's values.");
        foreach (var p in pipes.Where(p => !double.IsFinite(p.HglUp) || !double.IsFinite(p.HglDown)))
            warnings.Add($"Pipe {p.Name} has no HGL; it is skipped.");

        var sb = new StringBuilder();
        void L(string s = "") => sb.Append(s).Append("\r\n");
        L(";;; MasterFlow hydraulic results for Civil 3D");
        L($";;; Network: {Comment(net.Name)}");
        L($";;; Units: elevations {len}, flow {flow} (the drawing must use the same units; nothing is converted)");
        L($";;; Criteria: {Comment(net.Criteria.Source.Length > 0 ? net.Criteria.Source : "none")}");
        L($";;; Written {t.ToString("yyyy-MM-dd HH:mm", Inv)} by MasterFlow {appVersion ?? AppInfo.Version}");
        L(";;;");
        L(";;; Use: open the drawing with the pipe network, APPLOAD this file, then type MFHGL.");
        L(";;; Each Civil 3D pipe whose name matches gets HGL up/down, EGL up/down and flow rate.");
        L(";;; The run is one undo step (U undoes it). Structures have no HGL property in Civil 3D;");
        L(";;; their results are in the CSV and LandXML files exported with this one.");
        L("(vl-load-com)");
        L($"(setq mf:network {Str(net.Name)})");
        L($"(setq mf:units {Str($"{len}, {flow}")})");
        L(";; (name hglUp hglDown eglUp eglDown flow)");
        L("(setq mf:pipes '(");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in pipes.Where(p => double.IsFinite(p.HglUp) && double.IsFinite(p.HglDown) && seen.Add(p.Name)))
            L($"  ({Str(p.Name)} {Real(p.HglUp)} {Real(p.HglDown)} {Real(p.EglUp)} {Real(p.EglDown)} {Real(p.Discharge)})");
        L("))");
        L("(setq mf:props '(");
        foreach (var (value, names) in PipeProperties) L($"  ({Str(value)} {string.Join(" ", names)})");
        L("))");
        L(Body);
        return (sb.ToString(), warnings);
    }

    /// <summary>The command. Kept free of network data so it can be read and checked on its own.</summary>
    private const string Body = """
        (defun mf:put (obj names val / done r)
          ;; First property in NAMES that the object has and accepts VAL; nil when none.
          (if val
            (foreach nm names
              (if (and (not done) (vlax-property-available-p obj nm T))
                (progn
                  (setq r (vl-catch-all-apply 'vlax-put-property (list obj nm val)))
                  (if (not (vl-catch-all-error-p r)) (setq done nm))))))
          done)
        (defun mf:netname (obj / net)
          (if (vlax-property-available-p obj 'Network)
            (progn
              (setq net (vl-catch-all-apply 'vlax-get-property (list obj 'Network)))
              (if (and (not (vl-catch-all-error-p net)) (= (type net) 'VLA-OBJECT) (vlax-property-available-p net 'Name))
                (vlax-get-property net 'Name)))))
        (defun mf:addname (lst x) (if (member x lst) lst (cons x lst)))
        (defun c:MFHGL (/ doc ss i obj nm rec onlynet ans go updated found missing usedprops failedvals k got)
          (setq doc (vla-get-ActiveDocument (vlax-get-acad-object)))
          (setq ss (ssget "_X" '((0 . "AECC_PIPE"))) go T)
          (cond
            ((null ss) (princ "\nMasterFlow: no Civil 3D pipes in this drawing; nothing changed.") (setq go nil))
            (T
              (setq i 0)
              (repeat (sslength ss)
                (if (= (mf:netname (vlax-ename->vla-object (ssname ss i))) mf:network) (setq onlynet T))
                (setq i (1+ i)))
              (if (not onlynet)
                (progn
                  (initget "Yes No")
                  (setq ans (getkword (strcat "\nNo pipe network named \"" mf:network "\". Match pipe names in every network? [Yes/No] <No>: ")))
                  (if (/= ans "Yes") (progn (princ "\nMasterFlow: nothing changed.") (setq go nil)))))))
          (if go
            (progn
              (vla-StartUndoMark doc)
              (setq i 0 updated 0 found '())
              (repeat (sslength ss)
                (setq obj (vlax-ename->vla-object (ssname ss i)))
                (setq nm (vl-catch-all-apply 'vlax-get-property (list obj 'Name)))
                (if (and (= (type nm) 'STR)
                         (or (not onlynet) (= (mf:netname obj) mf:network))
                         (setq rec (assoc nm mf:pipes)))
                  (progn
                    (setq found (mf:addname found nm) k 1 got nil)
                    (foreach pr mf:props
                      (if (setq got (mf:put obj (cdr pr) (nth k rec)))
                        (setq usedprops (mf:addname usedprops (vl-symbol-name got)))
                        (setq failedvals (mf:addname failedvals (car pr))))
                      (setq k (1+ k)))
                    (setq updated (1+ updated))))
                (setq i (1+ i)))
              (vla-EndUndoMark doc)
              (foreach rec mf:pipes (if (not (member (car rec) found)) (setq missing (cons (car rec) missing))))
              (princ (strcat "\nMasterFlow: " (itoa updated) " of " (itoa (length mf:pipes)) " pipes updated (" mf:units ")."))
              (if usedprops (princ (strcat "\n  Properties set: " (apply 'strcat (mapcar '(lambda (x) (strcat x " ")) (reverse usedprops))))))
              (if failedvals
                (progn
                  (princ (strcat "\n  Could not set: " (apply 'strcat (mapcar '(lambda (x) (strcat x "; ")) (reverse failedvals)))))
                  (princ "\n  To list a pipe's properties: (vlax-dump-object (vlax-ename->vla-object (car (entsel))))")))
              (if missing
                (princ (strcat "\n  Not found in the drawing: " (apply 'strcat (mapcar '(lambda (x) (strcat x " ")) (reverse missing))))))))
          (princ))
        (princ "\nMasterFlow results loaded. Type MFHGL to write them to the pipe network.")
        (princ)
        """;

    /// <summary>All pipe and structure results as one CSV table (UTF-8), keyed by part name.</summary>
    public static string Csv(StormNetwork net, StormNetworkResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException("The network did not solve; there are no results to export.");
        var (len, flow, vel) = NetworkPartResults.UnitLabels(net.Units);
        var sb = new StringBuilder();
        void Row(params string[] cells) => sb.Append(string.Join(",", cells.Select(Cell))).Append("\r\n");
        Row("Network", "Part", "Name", "From", "To", "Kind",
            $"Q ({flow})", $"Q full ({flow})", "Q/Q full", $"V up ({vel})", $"V down ({vel})", $"V full ({vel})",
            $"Depth up ({len})", $"Depth down ({len})", $"Normal depth ({len})", $"Critical depth ({len})",
            $"HGL up ({len})", $"HGL down ({len})", $"EGL up ({len})", $"EGL down ({len})", $"Friction loss ({len})", "Regime", "Flows full",
            $"Rim ({len})", $"HGL ({len})", $"EGL ({len})", $"HGL leaving ({len})", $"Structure loss ({len})", "Loss method",
            $"Rim - HGL ({len})", $"Rim - EGL ({len})", "Check");
        static string X(double v) => NetworkPartResults.Num(v);
        static string Xn(double? v) => v is { } d ? NetworkPartResults.Num(d) : "";
        foreach (var p in NetworkPartResults.Pipes(result))
            Row(net.Name, "Pipe", p.Name, p.From, p.To, "",
                X(p.Discharge), X(p.FullFlowCapacity), NetworkPartResults.Num(p.CapacityRatio, "0.###"), X(p.VelocityUp), X(p.VelocityDown), X(p.FullFlowVelocity),
                X(p.DepthUp), X(p.DepthDown), X(p.NormalDepth), X(p.CriticalDepth),
                X(p.HglUp), X(p.HglDown), X(p.EglUp), X(p.EglDown), X(p.FrictionLoss), p.Regime, p.FlowsFull ? "yes" : "no",
                "", "", "", "", "", "", "", "", NetworkPartResults.Status(p.Failures));
        foreach (var s in NetworkPartResults.Structures(result))
            Row(net.Name, "Structure", s.Name, "", "", s.Kind,
                "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "",
                Xn(s.Rim), X(s.Hgl), X(s.Egl), X(s.OutletHgl), X(s.Loss), s.LossMethod, Xn(s.HglClearance), Xn(s.EglClearance), NetworkPartResults.Status(s.Failures));
        return sb.ToString();
    }

    private static string Cell(string s) =>
        s.IndexOfAny([',', '"', '\r', '\n']) >= 0 || s.StartsWith(' ') || s.EndsWith(' ') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    /// <summary>An AutoLISP string literal.</summary>
    public static string Str(string s)
    {
        var b = new StringBuilder("\"");
        foreach (char c in s)
            b.Append(c switch { '\\' => "\\\\", '"' => "\\\"", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t", _ => c.ToString() });
        return b.Append('"').ToString();
    }

    /// <summary>An AutoLISP real (always with a decimal point, invariant culture); nil when not computed.</summary>
    public static string Real(double v)
    {
        if (!double.IsFinite(v)) return "nil";
        string s = v.ToString("0.0#####", Inv);
        return s;
    }

    /// <summary>Text safe on one ASCII comment line (older AutoCAD releases read .lsp files as ANSI).</summary>
    private static string Comment(string s) =>
        new(s.Replace("§", "Sec.").Select(c => c is '\r' or '\n' ? ' ' : c > 126 ? '?' : c).ToArray());
}
