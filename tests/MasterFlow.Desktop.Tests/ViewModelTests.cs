using MasterFlow.Application;
using MasterFlow.Desktop.ViewModels;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Persistence;

namespace MasterFlow.Desktop.Tests;

public class ViewModelTests
{
    [Fact]
    public void Sample_Calculates_ChowExample()
    {
        var vm = MainViewModel.CreateWithSample();
        var c = vm.Selected!;
        Assert.True(c.HasResult);
        Assert.Equal(3.3609678403, c.Result!.Normal.Depth, 8);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void EditingAField_Recalculates_AndMarksDirty()
    {
        var vm = MainViewModel.CreateWithSample();
        var c = vm.Selected!;
        c.Discharge = "200";
        Assert.True(vm.IsDirty);
        Assert.Equal(200, c.Result!.Discharge, 10);
        Assert.True(c.Result.Normal.Depth < 3.36);
    }

    [Fact]
    public void InvalidText_ShowsError_AndNoNumbers()
    {
        var c = MainViewModel.CreateWithSample().Selected!;
        c.ManningN = "abc";
        Assert.False(c.HasResult);
        Assert.Empty(c.ResultLines);
        Assert.Contains(c.Messages, m => m.Severity == "Error" && m.Text.Contains("Manning"));
    }

    [Fact]
    public void ZeroSlope_ShowsEngineError()
    {
        var c = MainViewModel.CreateWithSample().Selected!;
        c.SlopeText = "0";
        Assert.False(c.HasResult);
        Assert.Contains(c.Messages, m => m.Code == MessageCodes.SlopeNotPositive);
    }

    [Fact]
    public void SlopeField_IsPercent()
    {
        var c = MainViewModel.CreateWithSample().Selected!;
        c.SlopeText = "0.16";
        Assert.Equal(0.0016, c.Result!.Slope, 12);
    }

    [Fact]
    public void Undo_Redo_RestoreInputsAndResults()
    {
        var vm = MainViewModel.CreateWithSample();
        var c = vm.Selected!;
        double before = c.Result!.Normal.Depth;
        c.BottomWidth = "30";
        double after = c.Result!.Normal.Depth;
        Assert.NotEqual(before, after);
        vm.UndoCommand.Execute(null);
        Assert.Equal("20", c.BottomWidth);
        Assert.Equal(before, c.Result!.Normal.Depth, 12);
        vm.RedoCommand.Execute(null);
        Assert.Equal("30", c.BottomWidth);
        Assert.Equal(after, c.Result!.Normal.Depth, 12);
    }

    [Fact]
    public void ShapeSwitch_HidesIrrelevantFields()
    {
        var c = MainViewModel.CreateWithSample().Selected!;
        c.Shape = SectionShape.Triangular;
        Assert.False(c.ShowBottomWidth);
        Assert.True(c.ShowSideSlopes);
        Assert.True(c.HasResult);
        c.Shape = SectionShape.Rectangular;
        Assert.True(c.ShowBottomWidth);
        Assert.False(c.ShowSideSlopes);
    }

    [Fact]
    public void SolveFor_DisablesSolvedField()
    {
        var c = MainViewModel.CreateWithSample().Selected!;
        c.SolveFor = SolveFor.Discharge;
        Assert.False(c.DischargeIsInput);
        Assert.True(c.DepthIsInput);
        Assert.InRange(c.Result!.Discharge, 300, 399.99); // y = 3 ft is below yn = 3.36 ft for 400 cfs, so Q < 400
        Assert.True(c.HasResult);
    }

    [Fact]
    public void SaveAndReload_ThroughViewModel_PreservesResults()
    {
        var vm = MainViewModel.CreateWithSample();
        vm.ProjectName = "VM test";
        vm.Selected!.Discharge = "321";
        string path = Path.Combine(Path.GetTempPath(), $"vm-{Guid.NewGuid():N}.mfproj");
        try
        {
            vm.SaveTo(path);
            Assert.False(vm.IsDirty);
            var vm2 = new MainViewModel();
            vm2.LoadFrom(path);
            Assert.Equal("VM test", vm2.ProjectName);
            Assert.Equal(6, vm2.Calculations.Count);
            Assert.Equal(vm.Calculations[0].Result!.Normal.Depth, vm2.Calculations[0].Result!.Normal.Depth, 12);
            Assert.Equal(vm.Calculations.Select(c => c.Id), vm2.Calculations.Select(c => c.Id));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RatingTable_GeneratesFromSelected()
    {
        var vm = MainViewModel.CreateWithSample();
        vm.RatingStart = "0.5"; vm.RatingEnd = "4"; vm.RatingIncrement = "0.5";
        vm.GenerateRatingTableCommand.Execute(null);
        Assert.Equal(8, vm.RatingTable!.Rows.Count);
        vm.RatingIncrement = "x";
        vm.GenerateRatingTableCommand.Execute(null);
        Assert.Null(vm.RatingTable);
    }

    [Fact]
    public void Report_FromViewModel_ContainsProjectFields()
    {
        var vm = MainViewModel.CreateWithSample();
        vm.PreparedBy = "K. Engineer";
        vm.Watermark = "DRAFT";
        var html = vm.BuildReportHtml(vm.Selected!);
        Assert.Contains("K. Engineer", html);
        Assert.Contains("DRAFT", html);
    }

    [Fact]
    public void Duplicate_And_Delete()
    {
        var vm = MainViewModel.CreateWithSample();
        vm.DuplicateCommand.Execute(null);
        Assert.Equal(7, vm.Calculations.Count);
        Assert.EndsWith("(copy)", vm.Selected!.Title);
        Assert.NotEqual(vm.Calculations[0].Id, vm.Selected.Id);
        vm.DeleteCommand.Execute(null); // no dialog service attached → deletes without prompt
        Assert.Equal(6, vm.Calculations.Count);
    }
}

public class PipeViewModelTests
{
    [Fact]
    public void Pipe_DiameterInInches_ConvertedOnce()
    {
        var vm = MainViewModel.CreateWithSample();
        var c = vm.Calculations.Single(x => x.Shape == SectionShape.Circular);
        Assert.True(c.IsPipe);
        Assert.Equal("36", c.Diameter);
        Assert.Equal(3.0, ((CircularSection)c.Result!.Input.Section).Diameter, 12);
        Assert.Contains(c.ResultLines, l => l.Symbol == "yn₂");
        c.Units = UnitSystem.SI;
        c.Diameter = "900";
        Assert.Equal(0.9, ((CircularSection)c.Result!.Input.Section).Diameter, 12);
        Assert.Equal("mm", c.DiameterUnit);
    }

    [Fact]
    public void Pipe_OverCapacity_ShowsError()
    {
        var c = MainViewModel.CreateWithSample().Calculations.Single(x => x.Shape == SectionShape.Circular);
        c.Discharge = "100";
        Assert.False(c.HasResult);
        Assert.Contains(c.Messages, m => m.Code == MessageCodes.ExceedsConduitCapacity);
    }

    [Fact]
    public void Pipe_SaveLoad_RoundTrip()
    {
        var vm = MainViewModel.CreateWithSample();
        string path = Path.Combine(Path.GetTempPath(), $"pipe-{Guid.NewGuid():N}.mfproj");
        try
        {
            vm.SaveTo(path);
            var vm2 = new MainViewModel();
            vm2.LoadFrom(path);
            var c = vm2.Calculations.Single(x => x.Shape == SectionShape.Circular);
            Assert.Equal("36", c.Diameter);
            Assert.True(c.HasResult);
        }
        finally { File.Delete(path); }
    }
}

public class EllipseViewModelTests
{
    private static CalculationViewModel Ellipse(MainViewModel vm) => vm.Calculations.Single(x => x.Shape == SectionShape.Elliptical);

    [Fact]
    public void Standard_HE36_Solves_AndMatchesIndependentCalc()
    {
        var c = Ellipse(MainViewModel.CreateWithSample());
        Assert.True(c.EllipseStandard);
        Assert.Equal(36, c.EllipseSize.EquivalentRound);
        Assert.Equal(1.07482756, c.Result!.Normal.Depth, 6);
    }

    [Fact]
    public void SwitchToVertical_And_Custom()
    {
        var c = Ellipse(MainViewModel.CreateWithSample());
        c.EllipseVertical = true;
        var ve = (EllipticalSection)c.Result!.Input.Section;
        Assert.False(ve.IsHorizontal);
        Assert.Equal(45.5 / 12, ve.Rise, 12);
        c.EllipseStandard = false;
        c.EllipseRise = "24"; c.EllipseSpan = "36";
        var custom = (EllipticalSection)c.Result!.Input.Section;
        Assert.Null(custom.EndRadius);
        Assert.Equal(2.0, custom.Rise, 12);
        Assert.True(c.HasResult);
    }

    [Fact]
    public void SaveLoad_RestoresStandardSizeAndOrientation()
    {
        var vm = MainViewModel.CreateWithSample();
        Ellipse(vm).EllipseVertical = true;
        Ellipse(vm).EllipseSize = EllipticalPipeCatalog.Sizes.Single(s => s.EquivalentRound == 60);
        string path = Path.Combine(Path.GetTempPath(), $"ell-{Guid.NewGuid():N}.mfproj");
        try
        {
            vm.SaveTo(path);
            var vm2 = new MainViewModel();
            vm2.LoadFrom(path);
            var c = Ellipse(vm2);
            Assert.True(c.EllipseStandard);
            Assert.True(c.EllipseVertical);
            Assert.Equal(60, c.EllipseSize.EquivalentRound);
            Assert.Equal(Ellipse(vm).Result!.Normal.Depth, c.Result!.Normal.Depth, 12);
        }
        finally { File.Delete(path); }
    }
}

public class IrregularViewModelTests
{
    private static CalculationViewModel Creek(MainViewModel vm) => vm.Calculations.Single(x => x.Shape == SectionShape.Irregular);

    [Fact]
    public void SampleCreek_Solves_WithSubareas()
    {
        var c = Creek(MainViewModel.CreateWithSample());
        Assert.True(c.HasResult, string.Join("; ", c.Messages.Select(m => m.Text)));
        Assert.Equal(3, c.Result!.Irregular!.Subareas.Count);
        Assert.Contains(c.ResultLines, l => l.Label == "Water-surface elev.");
        Assert.True(c.IsIrregular);
        Assert.False(c.ShowChannelDepth);
    }

    [Fact]
    public void PastedExcelText_IsParsed_AndBadLinesBlockTheResult()
    {
        var vm = MainViewModel.CreateWithSample();
        var c = vm.Selected!;
        c.Shape = SectionShape.Irregular;
        c.SolveFor = SolveFor.Discharge;
        c.ManningN = "0.013"; c.SlopeText = "1"; c.Depth = "1.5";
        c.PointsText = "Station\tElevation\n0+00\t5\n0+10\t0\n0+20\t0\n0+30\t5";
        Assert.True(c.HasResult);
        Assert.Equal(247.08514222, c.Result!.Discharge, 6);
        c.PointsText = "0\t5\n10\tabc\n20\t0\n30\t5";
        Assert.False(c.HasResult);
        Assert.Contains(c.Messages, m => m.Code == "E-UI-002" && m.Text.Contains("Line 2"));
        c.Undo();
        Assert.True(c.HasResult);
    }

    [Fact]
    public void SaveLoad_RestoresPointsAndMethod()
    {
        var vm = MainViewModel.CreateWithSample();
        var c = Creek(vm);
        c.RoughnessMethod = CompositeRoughnessMethod.EqualVelocity;
        string path = Path.Combine(Path.GetTempPath(), $"mf-{Guid.NewGuid():N}.mfproj");
        try
        {
            vm.SaveTo(path);
            var vm2 = new MainViewModel();
            vm2.LoadFrom(path);
            var c2 = Creek(vm2);
            Assert.Equal(CompositeRoughnessMethod.EqualVelocity, c2.RoughnessMethod);
            Assert.Equal(c.Result!.Normal.Depth, c2.Result!.Normal.Depth, 12);
            Assert.Equal(((IrregularSection)c.Result.Input.Section).Points, ((IrregularSection)c2.Result.Input.Section).Points);
        }
        finally { File.Delete(path); }
    }
}

public class FlowExtrasViewModelTests
{
    [Fact]
    public void PickingReferenceN_SetsN_AndIsUndoable()
    {
        var c = MainViewModel.CreateWithSample().Selected!;
        string before = c.ManningN;
        var entry = ManningRoughnessTable.Entries.First(x => x.Description.StartsWith("Corrugated metal storm"));
        c.RoughnessPick = entry;
        Assert.Equal(0.024, double.Parse(c.ManningN, System.Globalization.CultureInfo.CurrentCulture), 12);
        Assert.Null(c.RoughnessPick);
        c.Undo();
        Assert.Equal(before, c.ManningN);
    }

    [Fact]
    public void InvertElevation_RoundTrips_AndBadTextBlocksResult()
    {
        var vm = MainViewModel.CreateWithSample();
        var c = vm.Selected!;
        c.InvertElevation = "1000.25";
        Assert.Contains(c.ResultLines, l => l.Label == "Water surface (HGL)" && Math.Abs(l.Value - (1000.25 + c.Result!.Normal.Depth)) < 1e-9);
        string path = Path.Combine(Path.GetTempPath(), $"mf-{Guid.NewGuid():N}.mfproj");
        try
        {
            vm.SaveTo(path);
            var vm2 = new MainViewModel();
            vm2.LoadFrom(path);
            Assert.Equal(1000.25, double.Parse(vm2.Calculations[0].InvertElevation, System.Globalization.CultureInfo.CurrentCulture), 12);
        }
        finally { File.Delete(path); }
        c.InvertElevation = "abc";
        Assert.False(c.HasResult);
        c.InvertElevation = "";
        Assert.True(c.HasResult);
    }
}

public class ComparisonViewModelTests
{
    [Fact]
    public void Comparison_TracksProject_AndWorkbookBuilds()
    {
        var vm = MainViewModel.CreateWithSample();
        Assert.Equal(vm.Calculations.Count, vm.Comparison!.Titles.Count);
        vm.DuplicateCommand.Execute(null);
        Assert.Equal(vm.Calculations.Count, vm.Comparison!.Titles.Count);
        vm.Selected!.Title = "Renamed alternative";
        Assert.Contains("Renamed alternative", vm.Comparison!.Titles);
        var bytes = vm.BuildWorkbook();
        Assert.True(bytes.Length > 1000);
        Assert.Equal((byte)'P', bytes[0]); // zip signature "PK"
    }
}

public class ProjectFeatureTests
{
    private static string TempDir() { var d = Path.Combine(Path.GetTempPath(), "mf-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }

    [Fact]
    public void Delete_MovesToRecycleBin_RestoreAndSaveLoad()
    {
        var vm = MainViewModel.CreateWithSample();
        var title = vm.Selected!.Title;
        int n = vm.Calculations.Count;
        vm.DeleteCommand.Execute(null);
        Assert.Equal(n - 1, vm.Calculations.Count);
        Assert.Single(vm.RecycleBin);
        Assert.True(vm.HasRecycleBin);
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "p.mfproj");
            vm.SaveTo(path);
            var vm2 = new MainViewModel();
            vm2.LoadFrom(path);
            Assert.Equal(title, vm2.RecycleBin.Single().Title);
            vm2.RestoreCommand.Execute(null);
            Assert.Equal(n, vm2.Calculations.Count);
            Assert.Empty(vm2.RecycleBin);
            Assert.Equal(title, vm2.Selected!.Title);
            Assert.True(vm2.IsDirty);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Sort_ByFolderThenNaturalTitle()
    {
        var vm = MainViewModel.CreateWithSample();
        foreach (var c in vm.Calculations) c.Folder = "";
        vm.Calculations[0].Title = "Ditch 10"; vm.Calculations[1].Title = "Ditch 2"; vm.Calculations[2].Title = "a pipe";
        vm.Calculations[3].Folder = " Zeta / "; vm.Calculations[4].Folder = "Alpha";
        vm.SortCommand.Execute(null);
        var order = vm.Calculations.Select(c => (CalculationViewModel.NormaliseFolder(c.Folder), c.Title)).ToList();
        Assert.Equal("", order[0].Item1);
        Assert.Equal("a pipe", order[0].Title);
        Assert.True(order.FindIndex(o => o.Title == "Ditch 2") < order.FindIndex(o => o.Title == "Ditch 10"));
        Assert.Equal("Alpha", order[^2].Item1);
        Assert.Equal("Zeta", order[^1].Item1);
        Assert.Equal("Zeta", vm.Calculations[^1].ToModel().Folder);
    }

    [Fact]
    public async Task RecentFiles_And_Autosave_Recovery()
    {
        string dir = TempDir();
        try
        {
            var store = new UserDataStore(Path.Combine(dir, "user"));
            var vm = MainViewModel.CreateWithSample();
            vm.Store = store;
            string path = Path.Combine(dir, "a.mfproj");
            vm.SaveTo(path);
            Assert.Equal(Path.GetFullPath(path), vm.RecentFiles.Single());
            Assert.False(vm.Autosave()); // nothing unsaved

            vm.Selected!.Title = "Edited after save";
            Assert.True(vm.Autosave());
            Assert.NotNull(store.FindRecovery());

            // Simulated crash: a fresh session finds the recovery copy and restores it (no dialog service → restore).
            var vm2 = MainViewModel.CreateWithSample();
            vm2.Store = new UserDataStore(Path.Combine(dir, "user"));
            Assert.True(await vm2.CheckRecoveryAsync());
            Assert.Contains(vm2.Calculations, c => c.Title == "Edited after save");
            Assert.True(vm2.IsDirty);
            Assert.Equal(Path.GetFullPath(path), vm2.FilePath);
            Assert.Single(vm2.RecentFiles);

            vm2.SaveTo(path);
            Assert.Null(store.FindRecovery());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CorruptSettings_FallBackToDefaults()
    {
        string dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{ not json");
            Assert.Empty(new UserDataStore(dir).LoadSettings().RecentFiles);
            var s = new AppSettings();
            for (int i = 0; i < 15; i++) s = s.WithRecent(Path.Combine(dir, $"f{i}.mfproj"));
            Assert.Equal(AppSettings.MaxRecentFiles, s.RecentFiles.Count);
            Assert.EndsWith("f14.mfproj", s.RecentFiles[0]);
            s = s.WithRecent(Path.Combine(dir, "f10.mfproj"));
            Assert.EndsWith("f10.mfproj", s.RecentFiles[0]);
            Assert.Equal(s.RecentFiles.Count, s.RecentFiles.Distinct().Count());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ReportSettings_AndLogo_PersistWithProject()
    {
        string dir = TempDir();
        try
        {
            var png = Path.Combine(dir, "logo.png");
            File.WriteAllBytes(png, [0x89, 0x50, 0x4E, 0x47, 1, 2, 3]);
            var vm = MainViewModel.CreateWithSample();
            vm.Watermark = "PRELIMINARY"; vm.FooterText = "Firm"; vm.IncludeCalculationTrace = false;
            vm.LogoDataUri = MasterFlow.Application.LogoData.FromFile(png);
            Assert.True(vm.IsDirty);
            string path = Path.Combine(dir, "p.mfproj");
            vm.SaveTo(path);
            var vm2 = new MainViewModel();
            vm2.LoadFrom(path);
            Assert.Equal("PRELIMINARY", vm2.Watermark);
            Assert.False(vm2.IncludeCalculationTrace);
            Assert.StartsWith("data:image/png;base64,", vm2.LogoDataUri);
            var html = vm2.BuildReportHtml(vm2.Selected!);
            Assert.Contains("<img class=\"logo\"", html);
            Assert.DoesNotContain("Calculation trace</h2>", html);
            Assert.Throws<InvalidDataException>(() => MasterFlow.Application.LogoData.FromFile(Path.Combine(dir, "x.bmp")));
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class TitleExpressionViewModelTests
{
    [Fact]
    public void ListLabel_And_Report_UseEvaluatedTitle_ButFileKeepsTemplate()
    {
        var vm = MainViewModel.CreateWithSample();
        var c = vm.Selected!;
        c.Title = "Ditch Q={Q:F0}";
        Assert.True(c.TitleHasFields);
        Assert.Equal("Ditch Q=400", c.ListLabel);
        Assert.Equal("Ditch Q={Q:F0}", c.ToModel().Title);
        Assert.Contains("Ditch Q=400", vm.BuildReportHtml(c));
        c.Discharge = "250";
        Assert.Equal("Ditch Q=250", c.ListLabel);
    }
}

public class TemplateViewModelTests
{
    [Fact]
    public void NewFromTemplate_AddsAndSelectsCalculation()
    {
        var vm = MainViewModel.CreateWithSample();
        int before = vm.Calculations.Count;
        var t = vm.Templates.First(x => x.BuiltIn);
        vm.NewFromTemplateCommand.Execute(t);
        Assert.Equal(before + 1, vm.Calculations.Count);
        Assert.Equal(t.Name, vm.Selected!.Title);
        Assert.NotNull(vm.Selected.Result);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void SaveAsTemplate_PersistsPerUser_AndCanBeRemoved()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mf-tpl-" + Guid.NewGuid().ToString("N"));
        try
        {
            var vm = MainViewModel.CreateWithSample();
            vm.Store = new UserDataStore(dir);
            int builtIn = vm.Templates.Count;
            vm.Selected!.Title = "My ditch";
            vm.SaveAsTemplateCommand.Execute(null);
            vm.SaveAsTemplateCommand.Execute(null); // same title replaces, not duplicates
            Assert.Equal(builtIn + 1, vm.Templates.Count);

            var fresh = new MainViewModel { Store = new UserDataStore(dir) };
            var mine = fresh.Templates.Single(t => !t.BuiltIn);
            Assert.Equal("My ditch", mine.Name);
            Assert.Equal(vm.Selected.ToModel().Input, mine.Calculation.Input);

            fresh.DeleteUserTemplateCommand.Execute(mine);
            Assert.Equal(builtIn, new MainViewModel { Store = new UserDataStore(dir) }.Templates.Count);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}

public class PdfExportViewModelTests
{
    [Fact]
    public void BuildReportPdf_CoversEverySolvedCalculation()
    {
        var vm = MainViewModel.CreateWithSample();
        vm.Watermark = "DRAFT";
        var pdf = vm.BuildReportPdf(vm.Calculations);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 5));
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        int pages = 0;
        for (int i = text.IndexOf("/Type /Page", StringComparison.Ordinal); i >= 0; i = text.IndexOf("/Type /Page", i + 1, StringComparison.Ordinal))
            if (text[i + 11] != 's') pages++;
        Assert.True(pages >= vm.Calculations.Count(c => c.Result is not null));
    }
}

public class BatchSelectionTests
{
    [Fact]
    public void Unticked_IsLeftOutOfComparisonAndWorkbook_AndSaved()
    {
        var vm = MainViewModel.CreateWithSample();
        int all = vm.Comparison!.Titles.Count;
        var c = vm.Calculations[1];
        c.IncludeInBatch = false;
        Assert.True(vm.IsDirty);
        Assert.Equal(all - 1, vm.Comparison!.Titles.Count);
        Assert.DoesNotContain(c.ListLabel, vm.Comparison.Titles);
        Assert.DoesNotContain(c, vm.BatchCalculations);
        Assert.False(c.CanUndo); // a selection, not an input edit

        var json = ProjectFile.Serialize(vm.ToProject());
        var back = ProjectFile.Deserialize(json);
        Assert.False(back.Calculations[1].IncludeInBatch);
        Assert.True(back.Calculations[0].IncludeInBatch);
    }

    [Fact]
    public void OlderFiles_WithoutTheField_IncludeEverything()
    {
        var json = ProjectFile.Serialize(MainViewModel.SampleProject()).Replace("\"includeInBatch\": true,", "").Replace("\"IncludeInBatch\": true,", "");
        Assert.DoesNotContain("ncludeInBatch", json);
        Assert.All(ProjectFile.Deserialize(json).Calculations, c => Assert.True(c.IncludeInBatch));
    }
}

public class ErrorHandlingTests
{
    [Fact]
    public void UnexpectedError_IsLogged_AndUnsavedWorkIsCopiedForRecovery()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mf-err-" + Guid.NewGuid().ToString("N"));
        try
        {
            var vm = MainViewModel.CreateWithSample();
            vm.Store = new UserDataStore(dir);
            vm.Log = new LocalLog(vm.Store.LogDirectory);
            vm.Selected!.Title = "Edited before the crash";
            Assert.True(vm.IsDirty);

            vm.ReportUnexpectedError(new InvalidOperationException("simulated"), "test");

            Assert.Contains("simulated", vm.StatusMessage);
            Assert.Contains("recovery", vm.StatusMessage);
            Assert.NotNull(vm.Store.FindRecovery());
            Assert.Contains("Edited before the crash", vm.Store.LoadRecovery().Calculations.Select(c => c.Title));
            var log = File.ReadAllText(vm.Log.CurrentFile);
            Assert.Contains("ERROR Unexpected error (test).", log);
            Assert.Contains("simulated", log);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}

public class AccessibilityTests
{
    /// <summary>Every input in the main window has a screen-reader name (roadmap G5).</summary>
    [Fact]
    public void EveryInput_HasAnAutomationName()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "MasterFlow.slnx"))) dir = Path.GetDirectoryName(dir);
        if (dir is null) return; // not running from the repository
        var xaml = System.Xml.Linq.XDocument.Load(Path.Combine(dir, "src", "MasterFlow.Desktop", "Views", "MainWindow.axaml"));
        var unnamed = xaml.Descendants()
            .Where(e => e.Name.LocalName is "TextBox" or "ComboBox" or "ListBox")
            .Where(e => e.Attribute("AutomationProperties.Name") is null)
            .Select(e => e.ToString().Split('\n')[0])
            .ToList();
        Assert.Empty(unnamed);
    }
}

public class FlowCalcGapTests
{
    private static LandXmlReadResult TwoSections(UnitSystem? units) => new(
        [
            new LandXmlCrossSection("Ditch A", 100, "EG", [new(-10, 5), new(-2, 0), new(2, 0), new(10, 5)]),
            new LandXmlCrossSection("Ditch A", 200, "EG", [new(-8, 4), new(0, 0), new(8, 4)]),
        ], units, units is null ? null : "foot", units is null ? ["The file does not state its linear unit; choose the unit system yourself."] : []);

    [Fact]
    public void RatingRange_IsSaved_AndUsedForNonSelectedReports_AndWorkbook()
    {
        var vm = MainViewModel.CreateWithSample();
        var first = vm.Selected!;
        vm.RatingMode = RatingTableMode.DischargeFromDepth;
        (vm.RatingStart, vm.RatingEnd, vm.RatingIncrement) = ("1", "3", "0.5");
        vm.GenerateRatingTableCommand.Execute(null);
        Assert.Equal(new RatingRange(RatingDirection.DischargeFromDepth, 1, 3, 0.5), first.Rating);
        Assert.True(vm.IsDirty);

        vm.AddCalculationCommand.Execute(null);
        Assert.NotSame(first, vm.Selected);
        Assert.Contains("Rating table", vm.BuildReportHtml(first));
        Assert.Equal(5, first.BuildRatingTable()!.Rows.Count);

        // The range survives save and reload, and comes back in the rating panel on selection.
        var back = ProjectFile.Deserialize(ProjectFile.Serialize(vm.ToProject()));
        Assert.Contains(back.Calculations, c => c.Rating == new RatingRange(RatingDirection.DischargeFromDepth, 1, 3, 0.5));
        vm.RatingStart = "9";
        vm.Selected = first;
        Assert.Equal("1", vm.RatingStart);
        Assert.Equal(5, vm.RatingTable!.Rows.Count);

        vm.Selected = first;
        vm.RemoveRatingTableCommand.Execute(null);
        Assert.Null(first.Rating);
        Assert.DoesNotContain("Rating table", vm.BuildReportHtml(first));
    }

    [Fact]
    public void Workbook_HasARatingSheet_PerCalculationWithARange()
    {
        var vm = MainViewModel.CreateWithSample();
        (vm.RatingStart, vm.RatingEnd, vm.RatingIncrement) = ("1", "2", "0.5");
        vm.GenerateRatingTableCommand.Execute(null);
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(vm.BuildWorkbook()));
        var wb = new StreamReader(zip.GetEntry("xl/workbook.xml")!.Open()).ReadToEnd();
        int sheets = System.Text.RegularExpressions.Regex.Matches(wb, "<sheet ").Count;
        Assert.Equal(1 + vm.BatchCalculations.Count() + 1, sheets);
    }

    [Fact]
    public void ItemNumber_ShowsInTitleField_ListAndReport_AndPersists()
    {
        var vm = MainViewModel.CreateWithSample();
        var c = vm.Selected!;
        c.Title = "Culvert {item}";
        c.ItemNumber = " C-12 ";
        Assert.Equal("Culvert C-12", c.DisplayTitle);
        Assert.StartsWith("No. C-12 · ", c.ListDetail);
        Assert.True(vm.IsDirty);
        Assert.Contains("C-12", vm.BuildReportHtml(c));
        var back = ProjectFile.Deserialize(ProjectFile.Serialize(vm.ToProject()));
        Assert.Equal("C-12", back.Calculations.First(x => x.Title == "Culvert {item}").ItemNumber);
    }

    [Fact]
    public void ConvertToIrregular_KeepsNormalDepth()
    {
        var vm = MainViewModel.CreateWithSample();
        var c = vm.Selected!;
        c.Shape = SectionShape.Trapezoidal;
        (c.BottomWidth, c.LeftSlope, c.RightSlope, c.ChannelDepth) = ("20", "2", "2", "");
        double yn = c.Result!.Normal.Depth;
        vm.ConvertToIrregularCommand.Execute(null);
        Assert.Equal(SectionShape.Irregular, c.Shape);
        Assert.True(c.HasResult);
        Assert.Equal(yn, c.Result!.Normal.Depth, 6);
        c.Undo(); c.Undo();
        Assert.Equal(SectionShape.Trapezoidal, c.Shape);
    }

    [Fact]
    public void PointCommands_EditTheGrid_AndAreUndoable()
    {
        var vm = MainViewModel.CreateWithSample();
        var c = vm.Selected!;
        c.Shape = SectionShape.Irregular;
        c.PointsText = "10 5\n12 0\n16 0\n20 6";
        double yn = c.Result!.Normal.Depth;
        vm.ZeroStationsCommand.Execute(null);
        Assert.StartsWith("0", c.PointsText.TrimStart());
        vm.PointShift = "100";
        vm.ShiftElevationsCommand.Execute(null);
        Assert.Contains("105", c.PointsText);
        vm.ReversePointsCommand.Execute(null);
        Assert.Equal(yn, c.Result!.Normal.Depth, 9);
        c.Undo(); c.Undo(); c.Undo();
        Assert.Equal(10, StationPointText.Parse(c.PointsText).Points[0].Station);
        vm.PointShift = "abc";
        var before = c.PointsText;
        vm.ShiftStationsCommand.Execute(null);
        Assert.Equal(before, c.PointsText);
    }

    [Fact]
    public void ImportCrossSections_AddsOneSolvedIrregularCalculationEach_InAlignmentFolder()
    {
        var vm = MainViewModel.CreateWithSample();
        int n0 = vm.Calculations.Count;
        var status = vm.ImportCrossSections(TwoSections(UnitSystem.USCustomary), "ditch.xml");
        Assert.StartsWith("Imported 2 cross-section(s) from ditch.xml.", status);
        Assert.Equal(n0 + 2, vm.Calculations.Count);
        var added = vm.Calculations.Where(c => c.Folder == "Ditch A").ToList();
        Assert.Equal(2, added.Count);
        Assert.All(added, c => { Assert.Equal(SectionShape.Irregular, c.Shape); Assert.False(c.CanUndo); });
        Assert.Contains(added, c => c.Title == "Ditch A 1+00.00 (EG)");
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void ImportCrossSections_WithoutUnits_SaysSo_AndEmptyImportChangesNothing()
    {
        var vm = MainViewModel.CreateWithSample();
        Assert.Contains("not stated", vm.ImportCrossSections(TwoSections(null), "x.xml"));
        var vm2 = MainViewModel.CreateWithSample();
        int n = vm2.Calculations.Count;
        Assert.StartsWith("No cross-sections imported.", vm2.ImportCrossSections(new([], null, null, ["Not a LandXML file."]), "x.xml"));
        Assert.Equal(n, vm2.Calculations.Count);
        Assert.False(vm2.IsDirty);
    }
}

public class PipeNetworkImportTests
{
    private static byte[] StormBytes()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "MasterFlow.slnx"))) d = d.Parent;
        return File.ReadAllBytes(Path.Combine(d!.FullName, "docs", "samples", "storm-network.xml"));
    }

    [Fact]
    public void Import_CreatesSolvedPipeCalculations_AndStructureList()
    {
        var vm = MainViewModel.CreateWithSample();
        int n0 = vm.Calculations.Count;
        var status = vm.ImportLandXmlBytes(StormBytes(), "storm.xml");
        Assert.StartsWith("Imported 2 pipe(s) and 3 structure(s) (1 inlet(s)) from storm.xml.", status);
        Assert.Contains("RectPipe", status);
        Assert.Equal(n0 + 2, vm.Calculations.Count);
        Assert.True(vm.HasStructures);
        Assert.Equal(3, vm.Structures.Count);

        var p1 = vm.Calculations.Single(c => c.ItemNumber == "P-1");
        Assert.Equal("Storm A/Pipes", p1.Folder);
        Assert.Equal("P-1 (CB-1 → MH-1)", p1.Title);
        Assert.Equal(SectionShape.Circular, p1.Shape);
        Assert.Equal("18", p1.Diameter);           // 1.5 ft shown in inches in the field (the model keeps 1.5 ft)
        Assert.Equal("1", p1.SlopeText);
        Assert.Equal("100.5", p1.InvertElevation);
        Assert.True(p1.HasResult);
        // Flowing just full: Q = (1.486/0.013) A R^(2/3) S^(1/2), A = π·1.5²/4, R = 1.5/4 (independent of the engine)
        double qFull = 1.486 / 0.013 * (Math.PI * 1.5 * 1.5 / 4) * Math.Pow(1.5 / 4, 2.0 / 3.0) * Math.Sqrt(0.01);
        Assert.Equal(qFull, p1.Result!.Discharge, 6);
        Assert.False(p1.CanUndo);
        Assert.Equal(SectionShape.Elliptical, vm.Calculations.Single(c => c.ItemNumber == "P-2").Shape);

        // Saved with the project, in the workbook, and re-import replaces rather than duplicates.
        Assert.Equal(3, ProjectFile.Deserialize(ProjectFile.Serialize(vm.ToProject())).Structures.Count);
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(vm.BuildWorkbook()));
        Assert.Contains("Structures", new StreamReader(zip.GetEntry("xl/workbook.xml")!.Open()).ReadToEnd());
        vm.ImportLandXmlBytes(StormBytes(), "storm.xml");
        Assert.Equal(3, vm.Structures.Count);
    }

    [Fact]
    public void Import_SetsUpStormNetwork_ForHgl()
    {
        var vm = MainViewModel.CreateWithSample();
        var status = vm.ImportLandXmlBytes(StormBytes(), "storm.xml");
        Assert.Contains("1 storm network(s) set up for HGL/EGL", status);
        var n = Assert.Single(vm.Networks);
        Assert.Same(n, vm.SelectedNetwork);
        Assert.Equal("Storm A", n.Name);
        var model = n.ToModel();
        Assert.Equal(["P-1", "P-2"], model.Pipes.Select(p => p.Name));
        Assert.Equal(1.5, model.Pipes[0].Rise, 9);
        Assert.Equal(100.5, model.Pipes[0].UpstreamInvert, 9);
        Assert.True(double.IsNaN(model.Pipes[0].Discharge));      // design Q is entered by the user
        Assert.Equal(104.8, model.Structures.Single(s => s.Name == "MH-1").RimElevation);
        Assert.Equal(5100.0, model.Structures.Single(s => s.Name == "MH-1").Northing);

        // Without Q the network does not solve, and says why.
        n.Solve();
        Assert.False(n.HasResult);
        Assert.StartsWith("Not solved", n.Status);

        // Enter Q (last column), apply the Rapid City criteria and solve.
        var lines = n.PipesText.Split('\n');
        n.PipesText = string.Join('\n', lines.Select((l, i) => i == 0 ? l : l[..^1] + (i == 1 ? "6" : "10") + "\t"));   // Q is the second-last column
        n.ApplyRapidCityCommand.Execute(null);
        n.Tailwater = TailwaterRule.FreeOutfall;
        n.Solve();
        Assert.True(n.HasResult, n.Status);
        Assert.Equal(2, n.PipeRows.Count);
        Assert.Equal(3, n.StructureRows.Count);
        Assert.Equal("CB-1", n.SelectedPath);
        Assert.NotNull(n.Profile);
        Assert.Equal(225, n.Profile!.Length, 9);
        Assert.Equal(1.0, n.ToModel().Criteria.HglBelowRim);
        Assert.Contains("Rapid City", n.ToModel().Criteria.Source);
        Assert.Contains("\t", n.ResultsText());

        // Saved with the project and reloaded; edits mark the project changed; reports and workbook build.
        var back = ProjectFile.Deserialize(ProjectFile.Serialize(vm.ToProject()));
        var saved = Assert.Single(back.Networks);
        Assert.Equal(10, saved.Pipes[1].Discharge);
        Assert.Equal(TailwaterRule.FreeOutfall, saved.Tailwater);
        Assert.NotNull(vm.BuildNetworkPdf(n));
        Assert.Contains("<svg", vm.BuildNetworkHtml(n));
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(vm.BuildWorkbook()));
        Assert.Contains("Storm A profile", new StreamReader(zip.GetEntry("xl/workbook.xml")!.Open()).ReadToEnd());
    }

    [Fact]
    public void SizePipes_ThenExportLandXml_ForCivil3D()
    {
        var vm = MainViewModel.CreateWithSample();
        vm.ImportLandXmlBytes(StormBytes(), "storm.xml");
        var n = vm.SelectedNetwork!;
        // Inflows at the inlet (CB-1) and the manhole; Rapid City criteria; free outfall.
        n.StructuresText = n.StructuresText.Replace("CB-1\tInlet\t105.2\tAuto\t\t", "CB-1\tInlet\t105.2\tAuto\t\t12").Replace("MH-1\tManhole (circular)\t104.8\tAuto\t\t", "MH-1\tManhole (circular)\t104.8\tAuto\t\t4");
        n.ApplyRapidCityCommand.Execute(null);
        n.Tailwater = TailwaterRule.FreeOutfall;
        string before = n.PipesText;
        n.SizePipes();
        Assert.True(n.HasResult, n.Status);
        Assert.NotEmpty(n.SizingReport);
        Assert.Contains(n.SizingReport, x => x.StartsWith("P-1: 18 → 21 in"));   // Qfull(18 in., 1 %) = 10.5 cfs < 12
        Assert.True(n.CanUndoSizing);
        var (xml, warnings) = vm.BuildNetworkLandXml(n);
        Assert.Empty(warnings);                                   // imported structures carry coordinates
        var back = LandXmlPipeNetworks.Read(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml)));
        Assert.Equal(n.ToModel().Pipes.Single(p => p.Name == "P-1").Rise, back.Pipes.Single(p => p.Name == "P-1").Rise, 6);
        n.UndoSizing();
        Assert.Equal(before, n.PipesText);
        Assert.False(n.CanUndoSizing);
    }

    [Fact]
    public void Civil3DResults_FromImportedNetwork_MatchPipeNamesAndSolve()
    {
        var vm = MainViewModel.CreateWithSample();
        vm.ImportLandXmlBytes(StormBytes(), "storm.xml");
        var n = vm.SelectedNetwork!;
        n.StructuresText = n.StructuresText.Replace("CB-1\tInlet\t105.2\tAuto\t\t", "CB-1\tInlet\t105.2\tAuto\t\t6");
        n.Tailwater = TailwaterRule.FreeOutfall;
        var built = vm.BuildCivil3DResults(n);
        Assert.NotNull(built);
        Assert.True(n.HasResult, n.Status);
        foreach (var p in n.Result!.Pipes)
            Assert.Contains($"(\"{p.Pipe.Name}\" {Civil3DWriteBack.Real(p.HglUp)} {Civil3DWriteBack.Real(p.HglDown)} ", built.Value.Lisp);
        Assert.Contains($"(setq mf:network \"{n.Name}\")", built.Value.Lisp);   // the Civil 3D network name, for matching
        Assert.Equal(1 + n.Result.Pipes.Count + n.Result.Structures.Count, built.Value.Csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);

        // A network that cannot solve gives no files and says why.
        n.PipesText = "";
        Assert.Null(vm.BuildCivil3DResults(n));
        Assert.Contains("did not solve", n.Status);
    }

    [Fact]
    public void NewNetwork_EditMarksDirty_DeleteRemoves()
    {
        var vm = MainViewModel.CreateWithSample();
        Assert.False(vm.IsDirty);
        vm.NewNetworkCommand.Execute(null);
        var n = Assert.Single(vm.Networks);
        vm.IsDirty = false;
        n.HglBelowRim = "1";
        Assert.True(vm.IsDirty);
        vm.IsDirty = false;
        n.Solve();                       // solving is not an edit
        Assert.False(vm.IsDirty);
        vm.DeleteNetworkCommand.Execute(null);   // no dialog service in tests: deletes without asking
        Assert.Empty(vm.Networks);
        Assert.Null(vm.SelectedNetwork);
    }

    [Fact]
    public void Import_FileWithNothingUseful_ChangesNothing()
    {
        var vm = MainViewModel.CreateWithSample();
        int n = vm.Calculations.Count;
        Assert.StartsWith("Nothing imported", vm.ImportLandXmlBytes(System.Text.Encoding.UTF8.GetBytes("<LandXML/>"), "x.xml"));
        Assert.Equal(n, vm.Calculations.Count);
        Assert.False(vm.IsDirty);
    }
}

public class DesignCheckViewModelTests
{
    [Fact]
    public void Criteria_Check_Report_Persist_Undo()
    {
        var vm = MainViewModel.CreateWithSample();
        var c = vm.Selected!;
        c.Lining = "Class C riprap";
        c.PermissibleShear = "0.0001";
        Assert.True(vm.IsDirty);
        var k = Assert.Single(c.Checks);
        Assert.Equal(CheckStatus.Fail, k.Status);
        Assert.Contains(c.Messages, m => m.Code == "W-C-001");
        var html = vm.BuildReportHtml(c);
        Assert.Contains("Design checks", html);
        Assert.Contains("Class C riprap", html);
        Assert.Contains("FAILS", html);
        Assert.True(vm.BuildReportPdf([c]).Length > 1000);

        var back = ProjectFile.Deserialize(ProjectFile.Serialize(vm.ToProject()));
        var model = back.Calculations.First(x => x.Id == c.Id);
        Assert.Equal(0.0001, model.Criteria!.PermissibleShear);
        Assert.Equal("Class C riprap", CalculationViewModel.FromModel(model).Lining);

        c.Undo();
        Assert.Equal("", c.PermissibleShear);
        Assert.Empty(c.Checks);
    }

    [Fact]
    public void UnreadableLimit_IsReported_AndSkipped()
    {
        var c = MainViewModel.CreateWithSample().Selected!;
        c.MaxVelocity = "fast";
        Assert.Empty(c.Checks);
        Assert.True(c.HasResult);
        Assert.Contains(c.Messages, m => m.Code == "W-C-000");
    }

    [Fact]
    public void OlderFiles_HaveNoCriteria()
    {
        var json = ProjectFile.Serialize(MainViewModel.SampleProject());
        Assert.All(ProjectFile.Deserialize(json).Calculations, x => Assert.Null(x.Criteria));
    }
}

public class SensitivityViewModelTests
{
    [Fact]
    public void Sensitivity_ShownForSelection_IncludedInReportsAndWorkbook_WhenTicked_AndSaved()
    {
        var vm = MainViewModel.CreateWithSample();
        var calc = vm.Calculations.First(c => c.HasResult);
        vm.Selected = calc;
        Assert.NotNull(vm.SensitivityTable);
        Assert.True(vm.SensitivityTable!.Rows.Count >= 5);
        Assert.DoesNotContain("Sensitivity to n and slope", vm.BuildReportHtml(calc));

        calc.Sensitivity = true;
        Assert.Contains("Sensitivity to n and slope", vm.BuildReportHtml(calc));
        Assert.True(vm.BuildReportPdf([calc]).Length > 1000);
        using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(vm.BuildWorkbook())))
            Assert.Contains(zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/")),
                e => new StreamReader(e.Open()).ReadToEnd().Contains("Sensitivity to n and slope"));
        var back = ProjectFile.Deserialize(ProjectFile.Serialize(vm.ToProject()));
        Assert.True(back.Calculations.Single(c => c.Id == calc.Id).Sensitivity);
    }
}

public class AlignmentImportTests
{
    [Fact]
    public void ApplyAlignments_StationsListedAndNetworkStructures()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "MasterFlow.slnx"))) d = d.Parent;
        var vm = MainViewModel.CreateWithSample();
        vm.ImportLandXmlBytes(File.ReadAllBytes(Path.Combine(d!.FullName, "docs", "samples", "storm-network.xml")), "storm.xml");
        // Tangent heading north along E 2010: CB-1 (N 5000, E 2000) is 10 ft left, MH-1 (N 5100, E 2050) 40 ft right.
        const string xml = """
            <LandXML xmlns="http://www.landxml.org/schema/LandXML-1.2" version="1.2">
              <Units><Imperial linearUnit="USSurveyFoot"/></Units>
              <Alignments><Alignment name="CL" staStart="1000"><CoordGeom>
                <Line><Start>4900 2010</Start><End>5300 2010</End></Line>
              </CoordGeom></Alignment></Alignments>
            </LandXML>
            """;
        var status = vm.ImportLandXmlBytes(System.Text.Encoding.UTF8.GetBytes(xml), "cl.xml");
        Assert.Contains("1 alignment(s) from cl.xml (CL): stationed 3 listed structure(s) and 3 network structure(s).", status);
        var cb = vm.Structures.Single(s => s.Structure.Name == "CB-1").Structure;
        Assert.Equal("CL", cb.Alignment);
        Assert.Equal(1100, cb.Station!.Value, 6);
        Assert.Equal(-10, cb.Offset!.Value, 6);
        var mh = vm.SelectedNetwork!.ToModel().Structures.Single(s => s.Name == "MH-1");
        Assert.Equal(1200, mh.Station!.Value, 6);
        Assert.Equal(40, mh.Offset!.Value, 6);
        Assert.Equal("CL", ProjectFile.Deserialize(ProjectFile.Serialize(vm.ToProject())).Structures.Single(s => s.Name == "CB-1").Alignment);
    }
}

public class DesignToolsViewModelTests
{
    [Fact]
    public void RationalMethod_AppliesInflowsToNetwork_AndInputsAreSaved()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "MasterFlow.slnx"))) d = d.Parent;
        var vm = MainViewModel.CreateWithSample();
        vm.ImportLandXmlBytes(File.ReadAllBytes(Path.Combine(d!.FullName, "docs", "samples", "storm-network.xml")), "storm.xml");
        var tools = vm.Tools;
        tools.SelectedTool = MasterFlow.Application.Tools.DesignTools.Find("rational-rapid-city");
        Assert.Contains("CB-1", tools.InputText);             // sample uses the network's structure names
        tools.Run();
        Assert.True(tools.Result!.Succeeded, tools.ResultText);
        Assert.True(tools.CanApply);
        tools.ApplyToNetwork();
        var cb = vm.SelectedNetwork!.ToModel().Structures.Single(s => s.Name == "CB-1");
        Assert.True(cb.Inflow > 0 && cb.MinorInflow > 0 && cb.MinorInflow < cb.Inflow);
        Assert.True(vm.IsDirty);
        Assert.Contains("<table>", tools.BuildHtml());
        Assert.NotNull(tools.BuildWorkbook());

        tools.InputText += "\n# edited";
        var back = ProjectFile.Deserialize(ProjectFile.Serialize(vm.ToProject()));
        Assert.EndsWith("# edited", back.ToolInputs["rational-rapid-city"]);
        var vm2 = new MainViewModel();
        vm2.Tools.LoadInputs(back.ToolInputs);
        vm2.Tools.SelectedTool = MasterFlow.Application.Tools.DesignTools.Find("rational-rapid-city");
        Assert.EndsWith("# edited", vm2.Tools.InputText);
    }

    [Fact]
    public void License_BadKeyIsRejected_AndFreeStays()
    {
        var vm = new MainViewModel();
        Assert.False(vm.License.IsPremium);
        vm.Tools.LicenseKeyText = "MF1-not.a-key";
        vm.Tools.ActivateLicenseCommand.Execute(null);
        Assert.False(vm.License.IsPremium);
        Assert.Equal("MF1-not.a-key", vm.Tools.LicenseKeyText);   // kept so the user can correct it
        Assert.Contains("Premium tools stay locked", vm.StatusMessage);
    }

    [Fact]
    public void Context_MayRun_FollowsTheLicense()
    {
        var may = new MainViewModel().Tools.Context().MayRun;
        Assert.NotNull(may);
        Assert.True(may(MasterFlow.Application.Tools.DesignTools.Find("rational-rapid-city")!));    // free tool
        Assert.False(may(MasterFlow.Application.Tools.DesignTools.Find("drainage-report")!));             // premium, no key
    }
}

public class SlopeUnitTests
{
    [Fact]
    public void SwitchingUnit_ConvertsTheTypedValue_NotTheSlope()
    {
        var c = MainViewModel.CreateWithSample().Selected!;
        c.SlopeText = "0.16";
        c.SlopeUnit = SlopeUnit.RiseOverRun;
        Assert.Equal("0.0016", c.SlopeText);
        Assert.Equal("ft/ft", c.SlopeUnitLabel);
        Assert.Equal(0.0016, c.Result!.Slope, 12);
        c.SlopeText = "0.02";
        Assert.Equal(0.02, c.Result!.Slope, 12);
        c.SlopeUnit = SlopeUnit.Percent;
        Assert.Equal("2", c.SlopeText);
        Assert.Equal(0.02, c.Result!.Slope, 12);
    }

    [Fact]
    public void UnitChoice_IsSaved_AndUndoable()
    {
        var vm = MainViewModel.CreateWithSample();
        var c = vm.Selected!;
        c.SlopeUnit = SlopeUnit.RiseOverRun;
        var back = CalculationViewModel.FromModel(c.ToModel());
        Assert.Equal(SlopeUnit.RiseOverRun, back.SlopeUnit);
        Assert.Equal(c.Result!.Slope, back.Result!.Slope, 15);
        Assert.Equal("0.0016", back.SlopeText);
        c.Undo();
        Assert.Equal(SlopeUnit.Percent, c.SlopeUnit);
        Assert.Equal(0.0016, c.Result!.Slope, 12);
    }

    [Fact]
    public void SuspiciousSlope_ShowsTheUnitWarning()
    {
        var c = MainViewModel.CreateWithSample().Selected!;
        c.SlopeText = "0.02";
        Assert.Contains(c.Messages, m => m.Code == SlopeEntry.Code);
        c.SlopeText = "0.16";
        Assert.DoesNotContain(c.Messages, m => m.Code == SlopeEntry.Code);
    }
}
