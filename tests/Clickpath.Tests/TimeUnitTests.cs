using Clickpath.Engine;
using Clickpath.Models;

namespace Clickpath.Tests;

public class TimeUnitTests
{
    [Fact]
    public void All_durations_are_milliseconds_with_their_defaults()
    {
        var step = new Step();

        Assert.Equal(10000, step.TimeoutMs);
        Assert.Equal(1000, step.DelayMs);
        Assert.Equal(3000, step.MoveMs);
        Assert.Equal(4000, step.ClickEveryMs);
    }

    [Fact]
    public void Durations_have_a_lower_bound()
    {
        var step = new Step { TimeoutMs = -1, MoveMs = 0, ClickEveryMs = 5, DelayMs = -3 };

        Assert.Equal(100, step.TimeoutMs);
        Assert.Equal(100, step.MoveMs);
        Assert.Equal(100, step.ClickEveryMs);
        Assert.Equal(0, step.DelayMs);
    }

    [Fact]
    public void Plans_saved_in_seconds_are_converted_when_loaded()
    {
        var steps = PlanFile.FromJson("""
            [ { "Type": "Click", "Text": "x", "TimeoutSeconds": 7 },
              { "Type": "MoveMouse", "MoveSeconds": 3, "RandomClicks": true, "ClickEverySeconds": 2 },
              { "Type": "Delay", "DelayMs": 500 } ]
            """);

        Assert.Equal(7000, steps[0].TimeoutMs);
        Assert.Equal(3000, steps[1].MoveMs);
        Assert.Equal(2000, steps[1].ClickEveryMs);
        Assert.Equal(500, steps[2].DelayMs);
    }

    [Fact]
    public void Saving_writes_milliseconds_and_no_leftover_second_fields()
    {
        var json = PlanFile.ToJson([new Step { Type = StepType.MoveMouse, MoveMs = 2500, TimeoutMs = 8000, ClickEveryMs = 1500 }]);

        Assert.Contains("\"MoveMs\": 2500", json);
        Assert.Contains("\"TimeoutMs\": 8000", json);
        Assert.Contains("\"ClickEveryMs\": 1500", json);
        Assert.DoesNotContain("Seconds", json);
        Assert.Equal(2500, PlanFile.FromJson(json)[0].MoveMs);
    }

    [Fact]
    public void A_plan_in_the_new_format_is_not_multiplied_again()
    {
        var steps = PlanFile.FromJson("""[ { "Type": "Click", "Text": "x", "TimeoutMs": 7000 } ]""");

        Assert.Equal(7000, steps[0].TimeoutMs);
    }

    [Fact]
    public void The_subtitles_show_milliseconds()
    {
        Assert.StartsWith("2500 ms", new Step { Type = StepType.MoveMouse, MoveMs = 2500 }.Subtitle);
        Assert.StartsWith("800 ms", new Step { Type = StepType.Delay, DelayMs = 800 }.Subtitle);
    }
}
