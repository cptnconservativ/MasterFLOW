using MasterFlow.Desktop.ViewModels;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

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
            Assert.Equal(3, vm2.Calculations.Count);
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
        Assert.Equal(4, vm.Calculations.Count);
        Assert.EndsWith("(copy)", vm.Selected!.Title);
        Assert.NotEqual(vm.Calculations[0].Id, vm.Selected.Id);
        vm.DeleteCommand.Execute(null); // no dialog service attached → deletes without prompt
        Assert.Equal(3, vm.Calculations.Count);
    }
}
