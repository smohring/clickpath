using Clickpath.Engine;
using Clickpath.Models;

namespace Clickpath.Tests;

public class CloseAllStepTests
{
    [Fact]
    public void Pages_the_run_still_needs_are_kept_by_default()
    {
        Assert.True(new Step { Type = StepType.CloseAllTabs }.KeepOpenPages);
    }

    [Theory]
    [InlineData(StepType.CloseAllTabs, true)]
    [InlineData(StepType.CloseTab, false)]
    [InlineData(StepType.ForEachLink, false)]
    public void Only_close_all_windows_offers_the_option(StepType type, bool expected)
    {
        Assert.Equal(expected, new Step { Type = type }.UsesKeepOpenPages);
    }

    [Fact]
    public void Plans_without_the_option_keep_the_pages_and_a_saved_off_stays_off()
    {
        var old = PlanFile.FromJson("""[ { "Type": "CloseAllTabs" } ]""")[0];
        var off = PlanFile.FromJson(PlanFile.ToJson([new Step { Type = StepType.CloseAllTabs, KeepOpenPages = false }]))[0];

        Assert.True(old.KeepOpenPages);
        Assert.False(off.KeepOpenPages);
    }

    [Fact]
    public void The_subtitle_says_what_is_kept()
    {
        Assert.NotEqual("", new Step { Type = StepType.CloseAllTabs }.Subtitle);
        Assert.Equal("", new Step { Type = StepType.CloseAllTabs, KeepOpenPages = false }.Subtitle);
    }
}
