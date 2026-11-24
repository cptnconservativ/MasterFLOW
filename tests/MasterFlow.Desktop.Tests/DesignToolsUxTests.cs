using MasterFlow.Application.Licensing;
using MasterFlow.Application.Tools;
using MasterFlow.Desktop.ViewModels;

namespace MasterFlow.Desktop.Tests;

/// <summary>Design tools window usability: grouped, searchable list; locked marks; stale-result flag.</summary>
public class DesignToolsUxTests
{
    [Fact]
    public void EveryToolHasACategory_AndTheListIsGroupedUnderHeadings()
    {
        Assert.All(DesignTools.All, t => Assert.NotEqual("Other", DesignToolCatalog.Category(t)));
        var vm = new MainViewModel().Tools;
        Assert.True(vm.ToolList[0].IsHeader);
        Assert.Equal(DesignTools.All.Count, vm.ToolList.Count(i => i.IsTool));
        Assert.Equal("HYDROLOGY", vm.ToolList[0].Text);
    }

    [Fact]
    public void Search_FiltersByNameDescriptionOrCategory_AndEnterPicksFirstMatch()
    {
        var vm = new MainViewModel().Tools;
        vm.SearchText = "culvert";
        Assert.Contains(vm.ToolList, i => i.Tool?.Id == "hds5-culvert");
        Assert.DoesNotContain(vm.ToolList, i => i.Tool?.Id == "rational-rapid-city");
        vm.SelectFirstMatch();
        Assert.Equal("hds5-culvert", vm.SelectedTool?.Id);
        vm.SearchText = "zzzz no such tool";
        Assert.True(vm.NoToolsMatch);
    }

    [Fact]
    public void PremiumToolsAreMarkedLockedUnderFree_AndHeadingsCannotBeSelected()
    {
        var main = new MainViewModel();
        var vm = main.Tools;
        Assert.Equal("★ locked", vm.ToolList.First(i => i.Tool?.Id == "network-optimizer").Badge);
        Assert.Equal("", vm.ToolList.First(i => i.Tool?.Id == "rational-rapid-city").Badge);
        vm.SelectedTool = DesignTools.Find("network-optimizer");
        Assert.True(vm.IsLocked);
        Assert.Contains("Premium", vm.LockedText);
        vm.SelectedListItem = vm.ToolList[0];   // a heading
        Assert.Equal("network-optimizer", vm.SelectedTool?.Id);
        main.License = new LicenseState(LicenseStatus.Valid, new LicensePayload { Licensee = "Test", Features = ["*"] }, "");
        Assert.False(vm.IsLocked);
        Assert.Equal("★", vm.ToolList.First(i => i.Tool?.Id == "network-optimizer").Badge);
    }

    [Fact]
    public void EditingAfterRun_FlagsTheResultAsStale_UntilTheNextRun()
    {
        var vm = new MainViewModel().Tools;
        vm.SelectedTool = DesignTools.Find("rational-rapid-city");
        vm.Run();
        Assert.False(vm.IsStale);
        vm.InputText += "\n# edited";
        Assert.True(vm.IsStale);
        vm.Run();
        Assert.False(vm.IsStale);
    }
}
