using Clickpath.Engine;
using Clickpath.Models;

namespace Clickpath.Tests;

public class RandomClickStepTests
{
    [Fact]
    public void Random_clicks_are_off_by_default_every_four_seconds()
    {
        var step = new Step { Type = StepType.MoveMouse };

        Assert.False(step.RandomClicks);
        Assert.Equal(4000, step.ClickEveryMs);
    }

    [Theory]
    [InlineData(StepType.MoveMouse, true)]
    [InlineData(StepType.Delay, false)]
    [InlineData(StepType.Click, false)]
    public void Only_the_mouse_move_offers_random_clicks(StepType type, bool expected)
    {
        Assert.Equal(expected, new Step { Type = type }.UsesRandomClicks);
    }

    [Fact]
    public void The_rate_field_shows_only_while_random_clicks_are_on_and_tells_so()
    {
        var step = new Step { Type = StepType.MoveMouse };
        var changed = new List<string?>();
        step.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.False(step.UsesClickRate);
        step.RandomClicks = true;

        Assert.True(step.UsesClickRate);
        Assert.Contains(nameof(Step.UsesClickRate), changed);
    }

    [Fact]
    public void The_click_interval_has_a_lower_bound()
    {
        var step = new Step { Type = StepType.MoveMouse, ClickEveryMs = -5 };

        Assert.Equal(100, step.ClickEveryMs);
    }

    [Fact]
    public void The_options_survive_saving_and_loading_and_old_plans_load_with_them_off()
    {
        var loaded = PlanFile.FromJson(PlanFile.ToJson([new Step { Type = StepType.MoveMouse, RandomClicks = true, ClickEveryMs = 7000 }]));
        var old = PlanFile.FromJson("""[ { "Type": "MoveMouse", "MoveSeconds": 3 } ]""");

        Assert.True(loaded[0].RandomClicks);
        Assert.Equal(7000, loaded[0].ClickEveryMs);
        Assert.False(old[0].RandomClicks);
    }

    [Fact]
    public void The_subtitle_mentions_random_clicks()
    {
        Assert.Contains("·", new Step { Type = StepType.MoveMouse, RandomClicks = true }.Subtitle);
        Assert.DoesNotContain("·", new Step { Type = StepType.MoveMouse }.Subtitle);
    }
}
