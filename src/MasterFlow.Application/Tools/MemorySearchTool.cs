using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MasterFlow.Application.Documents;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Institutional memory search (premium, D-290). Ask a question in plain words ("how did we handle a sag inlet on a 6%
/// street?") and it searches the firm's past MasterFlow projects (every saved tool input, network and note), reports,
/// calculation packages and review letters (PDF, Word, text) in the folders you name. Ranking is BM25 with civil
/// engineering synonyms, a bonus for the numbers in the question (6 % matches 6.0 % and 0.06), and a bonus when the
/// words sit close together. Runs entirely on the computer; extracted text is cached by file date and size.
/// </summary>
public sealed partial class MemorySearchTool : IDesignTool
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public string Id => "memory-search";
    public string Name => "Institutional memory search (past projects, calcs and review letters)";
    public string Description => "Searches the firm's past work for how a problem was handled before: MasterFlow projects (every tool input, storm network and note), drainage reports, calculation packages and City review letters in the folders you name. Ask in plain words; results are ranked by relevance with civil engineering synonyms (sag = sump, inlet = catch basin, pond = detention …) and the numbers in the question, and show the project, file, page and the passage.";
    public bool UsesNetwork => false;

    /// <summary>Where extracted text is cached; null turns the cache off (tests).</summary>
    public static string? CacheFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MasterFlow", "memory-index");

    public string SampleInput(DesignToolContext context) => """
        # Institutional memory search. Folders are searched with their subfolders for MasterFlow projects (.mfproj),
        # PDF and Word documents, and text files. Several folders: separate with ;.
        folders = P:\Projects; P:\Review letters
        question = how did we handle a sag inlet on a 6% street last time?
        top = 15                  # results shown
        kinds = all               # all, or any of: project, report, review, calc, other
        since = 2015              # only files modified in or after this year (optional)
        """;

    public sealed record Chunk(string File, string Project, string Kind, string Where, string Text, DateTime Modified);

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Institutional memory search";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>(p.Errors);
        string question = p.Text("question") ?? "";
        if (question.Length == 0) return ToolResult.Failed(title, CalcMessage.Error("E-MS-001", "Ask something: question = how did we handle …"));
        var folders = (p.Text("folders") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (folders.Count == 0) return ToolResult.Failed(title, CalcMessage.Error("E-MS-002", "Name the folders to search: folders = P:\\Projects; P:\\Review letters."));
        int top = (int)Math.Clamp(p.Number("top", 15), 1, 200);
        var kinds = (p.Text("kinds") ?? "all").ToLowerInvariant().Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        int since = (int)p.Number("since", 0);

        // ---- corpus
        var chunks = new List<Chunk>();
        int files = 0, unreadable = 0;
        foreach (var f in folders)
        {
            if (!Directory.Exists(f)) { msgs.Add(CalcMessage.Warning("W-MS-001", $"Folder not found: {f}")); continue; }
            IEnumerable<string> list;
            try { list = Directory.EnumerateFiles(f, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { msgs.Add(CalcMessage.Warning("W-MS-001", $"{f}: {ex.Message}")); continue; }
            foreach (var path in list)
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext is not (".mfproj" or ".pdf" or ".docx" or ".txt" or ".md")) continue;
                FileInfo fi;
                try { fi = new FileInfo(path); } catch (IOException) { continue; }
                if (fi.Length > 80_000_000 || fi.LastWriteTime.Year < since) continue;
                if (++files > 5000) { msgs.Add(CalcMessage.Warning("W-MS-002", "More than 5,000 files; the rest are skipped. Name narrower folders.")); goto done; }
                try { chunks.AddRange(Chunks(path, fi)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or FormatException) { unreadable++; }
            }
        }
        done:
        if (unreadable > 0) msgs.Add(CalcMessage.Info("I-MS-001", $"{unreadable} file(s) could not be read (open in another program, damaged or protected)."));
        if (!kinds.Contains("all")) chunks = chunks.Where(c => kinds.Contains(c.Kind)).ToList();
        if (chunks.Count == 0)
        {
            msgs.Add(CalcMessage.Warning("W-MS-003", $"Nothing to search: no .mfproj, .pdf, .docx, .txt or .md files{(since > 0 ? $" modified since {since}" : "")} in {string.Join("; ", folders)}."));
            return new ToolResult(title, ["Nothing to search in the folders given."], [new ToolTable("Results", ["Rank", "Score", "Project", "Kind", "File", "Where", "Modified", "Passage", "Path"], [])], msgs);
        }

        // ---- rank
        var hits = Rank(question, chunks).Take(top).ToList();
        var rows = hits.Select((h, i) => (IReadOnlyList<object?>)[i + 1, Math.Round(h.Score, 2), h.Chunk.Project, h.Chunk.Kind, Path.GetFileName(h.Chunk.File), h.Chunk.Where, h.Chunk.Modified.ToString("yyyy-MM-dd", Inv), h.Snippet, h.Chunk.File]).ToList();
        var byFile = hits.GroupBy(h => h.Chunk.File).Select(g => (IReadOnlyList<object?>)[g.First().Chunk.Project, Path.GetFileName(g.Key), g.First().Chunk.Kind, g.Count(), Math.Round(g.Max(x => x.Score), 2), g.Key]).ToList();
        var terms = Expand(Tokens(question)).Select(t => t.Term).Distinct().ToList();
        var summary = new List<string>
        {
            $"{chunks.Count:N0} passages from {files:N0} files searched for: {question}",
            hits.Count == 0 ? "Nothing matched; try other words or a wider folder." : $"Best match: {hits[0].Chunk.Project}, {Path.GetFileName(hits[0].Chunk.File)} ({hits[0].Chunk.Where}).",
            "Search words (with synonyms): " + string.Join(", ", terms.Take(20)) + ".",
        };
        var tables = new List<ToolTable>
        {
            new("Results", ["Rank", "Score", "Project", "Kind", "File", "Where", "Modified", "Passage", "Path"], rows,
                ["Ranked by BM25 with synonyms; the numbers in the question and words close together score higher. Words matched are in [brackets]."]),
            new("By file", ["Project", "File", "Kind", "Passages", "Best score", "Path"], byFile),
        };
        return new ToolResult(title, summary, tables, msgs);
    }

    // ---------------------------------------------------------------- corpus

    /// <summary>The searchable passages of one file (from the cache when the file has not changed).</summary>
    public static List<Chunk> Chunks(string path, FileInfo fi)
    {
        string? cachePath = null;
        if (CacheFolder is { } cf)
        {
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{path}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}|v1")))[..32];
            cachePath = Path.Combine(cf, key + ".json");
            if (File.Exists(cachePath))
                try { return JsonSerializer.Deserialize<List<Chunk>>(File.ReadAllText(cachePath)) ?? []; } catch (JsonException) { }
        }
        var list = Path.GetExtension(path).Equals(".mfproj", StringComparison.OrdinalIgnoreCase) ? ProjectChunks(path, File.ReadAllText(path), fi.LastWriteTime) : DocumentChunks(path, fi.LastWriteTime);
        if (cachePath is not null)
            try { Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!); File.WriteAllText(cachePath, JsonSerializer.Serialize(list)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return list;
    }

    /// <summary>A MasterFlow project: project info, each design tool's saved input, each storm network, each channel calculation.</summary>
    public static List<Chunk> ProjectChunks(string path, string json, DateTime modified)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.TryGetProperty("project", out var pr) ? pr : doc.RootElement;
        string S(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        var info = root.TryGetProperty("info", out var inf) ? inf : default;
        string project = S(info, "name") is { Length: > 0 } nm ? nm : Path.GetFileNameWithoutExtension(path);
        var list = new List<Chunk>();
        if (info.ValueKind == JsonValueKind.Object)
            list.Add(new(path, project, "project", "project info", string.Join(". ", info.EnumerateObject().Where(x => x.Value.ValueKind == JsonValueKind.String).Select(x => $"{x.Name}: {x.Value.GetString()}")), modified));
        if (root.TryGetProperty("toolInputs", out var ti) && ti.ValueKind == JsonValueKind.Object)
            foreach (var t in ti.EnumerateObject())
            {
                string name = DesignTools.Find(t.Name)?.Name ?? t.Name;
                list.Add(new(path, project, "project", $"tool: {name}", name + "\n" + (t.Value.GetString() ?? ""), modified));
            }
        if (root.TryGetProperty("networks", out var nets) && nets.ValueKind == JsonValueKind.Array)
            foreach (var n in nets.EnumerateArray())
                list.Add(new(path, project, "project", $"storm network: {S(n, "name")}", Flatten(n), modified));
        if (root.TryGetProperty("calculations", out var calcs) && calcs.ValueKind == JsonValueKind.Array)
            foreach (var c in calcs.EnumerateArray())
                list.Add(new(path, project, "project", $"calculation: {S(c, "title")}", Flatten(c), modified));
        return list;
    }

    private static string Flatten(JsonElement e)
    {
        var sb = new StringBuilder();
        void Walk(JsonElement x, string key)
        {
            switch (x.ValueKind)
            {
                case JsonValueKind.Object: foreach (var p in x.EnumerateObject()) Walk(p.Value, p.Name); break;
                case JsonValueKind.Array: foreach (var y in x.EnumerateArray()) Walk(y, key); sb.Append(". "); break;
                case JsonValueKind.String: sb.Append(key).Append(' ').Append(x.GetString()).Append(", "); break;
                case JsonValueKind.Number: sb.Append(key).Append(' ').Append(x.GetRawText()).Append(", "); break;
            }
        }
        Walk(e, "");
        return sb.ToString();
    }

    /// <summary>A document: pages split into passages of about 120 words at paragraph breaks.</summary>
    public static List<Chunk> DocumentChunks(string path, DateTime modified)
    {
        var doc = Path.GetExtension(path).ToLowerInvariant() is ".txt" or ".md" ? new DocumentPages(path, [File.ReadAllText(path)]) : DocumentText.Read(path);
        string name = Path.GetFileNameWithoutExtension(path);
        string kind = Kind(name, doc.Pages.FirstOrDefault() ?? "");
        string project = ProjectOf(path);
        var list = new List<Chunk>();
        for (int pg = 0; pg < doc.Pages.Count; pg++)
        {
            var paras = RegexPara().Split(doc.Pages[pg]).Select(x => RegexSpace().Replace(x, " ").Trim()).Where(x => x.Length > 0).ToList();
            var buf = new StringBuilder();
            void Flush() { if (buf.Length > 0) { list.Add(new(path, project, kind, doc.Pages.Count > 1 ? $"page {pg + 1}" : "text", buf.ToString().Trim(), modified)); buf.Clear(); } }
            foreach (var para in paras)
            {
                if (buf.Length > 0 && WordCount(buf.ToString()) + WordCount(para) > 160) Flush();
                buf.Append(para).Append(' ');
                if (WordCount(buf.ToString()) > 200) Flush();
            }
            Flush();
        }
        return list;
    }

    private static int WordCount(string s) => s.Count(c => c == ' ') + 1;

    private static string Kind(string name, string first)
    {
        // The file name decides first ("Drainage report.pdf" is a report even if it quotes review comments), then the first page.
        static string? Of(string s) =>
            Regex.IsMatch(s, @"review|comment|redline|response to|resubmittal") ? "review"
            : Regex.IsMatch(s, @"\bcalc|calculation|hgl|computation") ? "calc"
            : Regex.IsMatch(s, @"report|drainage study|storm ?water|swmp|swppp|manual") ? "report" : null;
        return Of(name.ToLowerInvariant()) ?? Of((first.Length > 600 ? first[..600] : first).ToLowerInvariant()) ?? "other";
    }

    /// <summary>The project a file belongs to: the folder under the searched root that looks like a project (number or name).</summary>
    private static string ProjectOf(string path)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(path)!);
        for (var d = dir; d is not null; d = d.Parent)
            if (RegexJob().IsMatch(d.Name)) return d.Name;
        return dir.Name;
    }

    // ---------------------------------------------------------------- ranking

    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "and", "or", "of", "on", "in", "at", "to", "for", "by", "with", "from", "is", "are", "was", "were", "be", "been", "it", "its",
        "how", "what", "when", "where", "which", "who", "why", "did", "do", "does", "we", "our", "us", "you", "they", "last", "time", "ever", "before",
        "handle", "handled", "deal", "dealt", "use", "used", "this", "that", "these", "those", "any", "there", "have", "has", "had", "about", "find", "show", "me",
    };

    private static readonly string[][] Synonyms =
    [
        ["sag", "sump", "low point", "lowpoint"], ["inlet", "catch basin", "cb", "grate", "curb opening"], ["street", "roadway", "road", "gutter"],
        ["pond", "detention", "basin", "retention"], ["pipe", "storm sewer", "rcp", "culvert"], ["riprap", "rip rap", "rock", "apron"],
        ["hgl", "hydraulic grade"], ["egl", "energy grade"], ["manhole", "mh", "junction"], ["swale", "ditch", "channel"],
        ["spread", "ponding", "flooding"], ["clog", "clogging", "debris"], ["slope", "grade"], ["variance", "exception", "deviation"],
        ["outfall", "outlet", "discharge"], ["wqcv", "water quality"], ["comment", "review", "redline"], ["tc", "time of concentration"],
    ];

    /// <summary>Lower-case words with simple stemming; numbers keep their decimal point.</summary>
    public static List<string> Tokens(string text)
    {
        var list = new List<string>();
        foreach (Match m in RegexWord().Matches(text.ToLowerInvariant()))
        {
            string w = m.Value.TrimEnd('.');
            if (w.Length == 0 || Stop.Contains(w)) continue;
            list.Add(Stem(w));
        }
        return list;
    }

    private static string Stem(string w)
    {
        if (char.IsDigit(w[0])) return w;
        if (w.Length > 4 && w.EndsWith("ies", StringComparison.Ordinal)) return w[..^3] + "y";
        if (w.Length > 4 && w.EndsWith("ing", StringComparison.Ordinal)) return w[..^3];
        if (w.Length > 3 && w.EndsWith("es", StringComparison.Ordinal) && !w.EndsWith("ses", StringComparison.Ordinal)) return w[..^2];
        if (w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal)) return w[..^1];
        if (w.Length > 4 && w.EndsWith("ed", StringComparison.Ordinal)) return w[..^2];
        return w;
    }

    /// <summary>Question terms with their synonyms (weight 1 for the word asked, 0.6 for a synonym); a number also matches its percent/decimal forms.</summary>
    public static List<(string Term, double Weight, int Group)> Expand(List<string> q)
    {
        var list = new List<(string, double, int)>();
        for (int i = 0; i < q.Count; i++)
        {
            list.Add((q[i], 1.0, i));
            foreach (var set in Synonyms)
                if (set.Select(s => string.Join(' ', Tokens(s))).Contains(q[i]))
                    foreach (var s in set) foreach (var t in Tokens(s)) if (t != q[i]) list.Add((t, 0.6, i));
            if (double.TryParse(q[i], NumberStyles.Float, Inv, out var v))
            {
                foreach (var alt in new[] { v.ToString("0.0", Inv), v.ToString("0.00", Inv), (v / 100).ToString("0.0##", Inv) }) if (alt != q[i]) list.Add((alt, 0.8, i));
            }
        }
        return list.DistinctBy(x => x.Item1).ToList();
    }

    public sealed record Hit(Chunk Chunk, double Score, string Snippet);

    /// <summary>BM25 (k1 1.2, b 0.75) over the passages, with synonym weights and a proximity bonus.</summary>
    public static IEnumerable<Hit> Rank(string question, List<Chunk> chunks)
    {
        var q = Expand(Tokens(question));
        if (q.Count == 0) return [];
        var docs = chunks.Select(c => Tokens(c.Text + " " + c.Where)).ToList();
        double avg = docs.Average(d => d.Count);
        var df = q.ToDictionary(t => t.Term, t => docs.Count(d => d.Contains(t.Term)));
        int N = docs.Count;
        var hits = new List<Hit>();
        for (int i = 0; i < N; i++)
        {
            var d = docs[i];
            if (d.Count == 0) continue;
            var tf = d.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            // Per idea of the question: its best-matching word counts fully, other synonyms a quarter, so a passage
            // full of synonyms of one word does not outrank one that answers the whole question.
            var perGroup = new Dictionary<int, List<double>>();
            foreach (var (term, w, g) in q)
            {
                if (!tf.TryGetValue(term, out var f)) continue;
                double idf = Math.Log(1 + (N - df[term] + 0.5) / (df[term] + 0.5));
                if (!perGroup.TryGetValue(g, out var l)) perGroup[g] = l = [];
                l.Add(w * idf * f * 2.2 / (f + 1.2 * (0.25 + 0.75 * d.Count / avg)));
            }
            double score = perGroup.Values.Sum(l => l.Max() + 0.25 * (l.Sum() - l.Max()));
            var groups = perGroup.Keys.ToHashSet();
            if (score <= 0) continue;
            // Coverage: passages that answer more of the question's ideas rank higher.
            int ideas = q.Select(x => x.Group).Distinct().Count();
            score *= 0.5 + 0.5 * groups.Count / ideas;
            // Proximity: smallest window holding two different matched ideas.
            var pos = d.Select((t, k) => (t, k)).Where(x => q.Any(y => y.Term == x.t)).Select(x => (x.k, q.First(y => y.Term == x.t).Group)).ToList();
            int best = int.MaxValue;
            for (int a = 0; a < pos.Count; a++) for (int b = a + 1; b < pos.Count && pos[b].k - pos[a].k < best; b++) if (pos[b].Group != pos[a].Group) best = pos[b].k - pos[a].k;
            if (best < 12) score *= 1 + 0.3 * (12 - best) / 12.0;
            hits.Add(new(chunks[i], score, Snippet(chunks[i].Text, q.Select(x => x.Term).ToHashSet())));
        }
        return hits.OrderByDescending(h => h.Score);
    }

    private static string Snippet(string text, HashSet<string> terms)
    {
        var words = RegexSpace().Split(text.Trim());
        int bestAt = 0, bestN = -1;
        for (int i = 0; i < words.Length; i++)
        {
            int n = 0;
            for (int j = i; j < Math.Min(words.Length, i + 40); j++) if (Match(words[j], terms)) n++;
            if (n > bestN) { bestN = n; bestAt = i; }
        }
        int from = Math.Max(0, bestAt - 5), to = Math.Min(words.Length, from + 45);
        var sb = new StringBuilder(from > 0 ? "… " : "");
        for (int i = from; i < to; i++) sb.Append(Match(words[i], terms) ? $"[{words[i]}]" : words[i]).Append(' ');
        if (to < words.Length) sb.Append('…');
        return sb.ToString().Trim();
    }

    private static bool Match(string word, HashSet<string> terms) => Tokens(word).Any(terms.Contains);

    [GeneratedRegex(@"\d+(?:\.\d+)?|[a-z][a-z0-9\-']*")]
    private static partial Regex RegexWord();
    [GeneratedRegex(@"\n\s*\n|\r?\n(?=[A-Z0-9•\-])")]
    private static partial Regex RegexPara();
    [GeneratedRegex(@"\s+")]
    private static partial Regex RegexSpace();
    [GeneratedRegex(@"^\d{2}-\d{2,4}|^\d{4,6}\b|project", RegexOptions.IgnoreCase)]
    private static partial Regex RegexJob();
}
