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
        c.SlopePercent = "0";
        Assert.False(c.HasResult);
        Assert.Contains(c.Messages, m => m.Code == MessageCodes.SlopeNotPositive);
    }

    [Fact]
    public void SlopeField_IsPercent()
    {
        var c = MainViewModel.CreateWithSample().Selected!;
        c.SlopePercent = "0.16";
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
        c.ManningN = "0.013"; c.SlopePercent = "1"; c.Depth = "1.5";
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
