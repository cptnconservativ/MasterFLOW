using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using MasterFlow.Desktop;
using MasterFlow.Desktop.ViewModels;
using MasterFlow.Desktop.Views;
using MasterFlow.Application.Licensing;
using MasterFlow.Application.Tools;

static class P
{
    static string Out;
    static void Pump(int n = 6) { for (int i = 0; i < n; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
    static void Mono(Window w) { foreach (var c in w.GetVisualDescendants()) { if (c is TemplatedControl tc && tc.FontFamily.ToString().Contains("Consolas")) tc.FontFamily = new Avalonia.Media.FontFamily("DejaVu Sans Mono"); if (c is TextBlock tb && tb.FontFamily.ToString().Contains("Consolas")) tb.FontFamily = new Avalonia.Media.FontFamily("DejaVu Sans Mono"); } }
    static void Shot(Window w, string name) { Pump(); Mono(w); Pump(); var f = w.CaptureRenderedFrame(); f?.Save(Path.Combine(Out, name + ".png")); Console.WriteLine("shot " + name); }
    static T Find<T>(Control root, Func<T, bool> pred = null) where T : class => root.GetLogicalDescendants().OfType<T>().FirstOrDefault(x => pred == null || pred(x));
    static void Tab(Window w, int tabControlIndex, string header)
    {
        var tcs = w.GetLogicalDescendants().OfType<TabControl>().ToList();
        var tc = tcs[tabControlIndex];
        foreach (var it in tc.Items.OfType<TabItem>()) if ((it.Header as string) == header) { tc.SelectedItem = it; break; }
        Pump();
    }

    [STAThread]
    static void Main(string[] args)
    {
        Out = args[0]; var demo = args[1]; string only = args.Length > 2 ? args[2] : null;
        Directory.CreateDirectory(Out);
        AppBuilder.Configure<App>().UseSkia().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        // Project: the built-in sample calculations plus the Main St storm network.
        var demoProj = MasterFlow.Persistence.ProjectFile.Load(demo);
        var proj = MainViewModel.SampleProject() with { Networks = demoProj.Networks, Info = MainViewModel.SampleProject().Info with { Name = "Main Street improvements", ProjectNumber = "26-001", PreparedBy = "J. Engineer, P.E.", Client = "City of Rapid City" } };
        var path = Path.Combine(Path.GetTempPath(), "manual-sample.mfproj");
        MasterFlow.Persistence.ProjectFile.Save(proj, path);
        var vm = new MainViewModel();
        vm.LoadFrom(path);
        vm.StatusMessage = "Ready.";

        var main = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
        main.Show(); Pump();

        if (only == null || only == "main")
        {
            vm.Selected = vm.Calculations[0]; Shot(main, "main-window");
            vm.Selected = vm.Calculations.First(c => c.Title.Contains("Creek")); Pump(); Shot(main, "main-irregular");
            vm.Selected = vm.Calculations.First(c => c.Title.StartsWith("36-in")); Pump(); Shot(main, "main-pipe");
            vm.Selected = vm.Calculations.First(c => c.Title.StartsWith("HE")); Pump(); Shot(main, "main-ellipse");
            vm.Selected = vm.Calculations[0];
            Tab(main, 0, "Compare"); Shot(main, "main-compare");
            Tab(main, 0, "Sensitivity"); Shot(main, "main-sensitivity");
            Tab(main, 0, "Rating table");
            try { vm.GenerateRatingTableCommand.Execute(null); } catch (Exception e) { Console.WriteLine(e.Message); }
            Shot(main, "main-rating");
            Tab(main, 0, "Results");
        }

        if (only == null || only == "net")
        {
            var nw = new NetworkWindow { DataContext = vm, Width = 1500, Height = 950 };
            nw.Show(); Pump();
            vm.SelectedNetwork?.Solve(); Pump();
            Shot(nw, "network-profile");
            foreach (var h in new[] { "Pipes", "Structures", "Messages" }) { Tab(nw, 0, h); Shot(nw, "network-" + h.ToLower()); }
            nw.Close();
        }

        // Design tools window. Premium tools are shown with an unsigned, in-memory Premium state so every tool can be
        // screenshotted; this program is a documentation aid, is not shipped and never saves the state.
        vm.License = new LicenseState(LicenseStatus.Valid, new LicensePayload { Licensee = "Example Engineering, LLC", Features = ["*"] }, "");
        vm.SelectedNetwork?.Solve();
        var tw = new DesignToolsWindow { DataContext = vm, Width = 1500, Height = 950 };
        tw.Show(); Pump();
        var reports = Path.Combine(Out, "reports"); Directory.CreateDirectory(reports);
        var log = new List<string>();
        foreach (var t in vm.Tools.Tools)
        {
            if (only != null && only != "tools" && only != t.Id) continue;
            try
            {
                vm.Tools.SelectedTool = t; Pump();
                vm.Tools.LoadSampleCommand.Execute(null); Pump();
                vm.Tools.Run(); Pump();
                Shot(tw, "tool-" + t.Id);
                var html = vm.Tools.BuildHtml();
                if (html != null) File.WriteAllText(Path.Combine(reports, t.Id + ".html"), html);
                var r = vm.Tools.Result;
                log.Add($"{t.Id}\t{t.Name}\t{(PremiumFeatures.IsPremium(t) ? "premium" : "free")}\tok={r?.Succeeded}\tcharts={r?.Charts?.Count}\terrors={string.Join(" | ", r?.Messages.Where(m => m.Severity == MasterFlow.Hydraulics.MessageSeverity.Error).Select(m => m.Code + " " + m.Text) ?? [])}");
            }
            catch (Exception e) { log.Add($"{t.Id}\tEXC {e.Message}"); }
        }
        File.WriteAllLines(Path.Combine(Out, "tools.tsv"), log);
        // Free-license view of the window, to show the license box and a locked tool
        if (only == null || only == "tools")
        {
            vm.License = LicenseState.Free;
            vm.Tools.SelectedTool = vm.Tools.Tools.First(x => x.Id == "network-optimizer"); vm.Tools.LoadSampleCommand.Execute(null); vm.Tools.Run();
            Shot(tw, "tools-locked");
        }
    }
}
