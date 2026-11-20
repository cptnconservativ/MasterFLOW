using MasterFlow.Desktop.ViewModels;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Desktop.Tests;

public class ResultWarningsTests
{
    [Fact]
    public void Overtopping_IsShownWithTheResults_NotOnlyInTheMessagesCard()
    {
        var vm = MainViewModel.CreateWithSample();
        var creek = vm.Calculations.Single(c => c.Title == "Creek with overbanks");
        creek.RoughnessMethod = CompositeRoughnessMethod.EqualVelocity;
        Assert.True(creek.HasResultWarnings);
        Assert.Contains(creek.ResultWarnings, m => m.Code == MessageCodes.WallExtended);
        Assert.DoesNotContain(creek.ResultWarnings, m => m.Severity == nameof(MessageSeverity.Info));

        var trapezoid = vm.Calculations[0];
        Assert.False(trapezoid.HasResultWarnings);
    }
}
