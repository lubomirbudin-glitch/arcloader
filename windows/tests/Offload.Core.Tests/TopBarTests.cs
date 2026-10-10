using Offload.Core.Desktop;
using Offload.Core.Hardware;

namespace Offload.Core.Tests;

/// The top bar (UI návrh): the Hardware dot and the project switcher's label.
public class TopBarTests
{
    static HwTestResult Result(params AdviceKind[] advice) =>
        new(DateTime.UtcNow, [], [], [], advice.Select(k => new Advice(k, "x")).ToList());

    [Fact]
    public void HardwareDot_NoneOkWarning()
    {
        Assert.Equal(HardwareDot.None, TopBar.Dot(null));
        Assert.Equal(HardwareDot.Ok, TopBar.Dot(Result()));
        Assert.Equal(HardwareDot.Ok, TopBar.Dot(Result(AdviceKind.Tip, AdviceKind.Info)));
        Assert.Equal(HardwareDot.Warning, TopBar.Dot(Result(AdviceKind.Info, AdviceKind.Warning)));
    }

    [Fact]
    public void ProjectLabel_NameOrNoProject()
    {
        Assert.Equal("Film X", TopBar.ProjectLabel("Film X"));
        Assert.Equal("No project", TopBar.ProjectLabel(null));
    }
}
