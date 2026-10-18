using Clickpath.Models;

namespace Clickpath.Tests;

public class StepProgressTests
{
    [Fact]
    public void A_pause_and_a_mouse_move_count_down_their_planned_length()
    {
        Assert.Equal(7000, new Step { Type = StepType.Delay, DelayMs = 7000 }.WaitLimitMs);
        Assert.Equal(120000, new Step { Type = StepType.MoveMouse, MoveMs = 120000 }.WaitLimitMs);
        Assert.False(new Step { Type = StepType.Delay }.WaitLimitIsTimeout);
    }

    [Fact]
    public void A_step_that_looks_for_something_counts_down_its_timeout_and_repeat_steps_have_no_limit()
    {
        var wait = new Step { Type = StepType.WaitFor, TimeoutMs = 8000 };

        Assert.Equal(8000, wait.WaitLimitMs);
        Assert.True(wait.WaitLimitIsTimeout);
        Assert.Null(new Step { Type = StepType.ForEachLink }.WaitLimitMs);
        Assert.Null(new Step { Type = StepType.ForEachListedLink }.WaitLimitMs);
        Assert.Null(new Step { Type = StepType.StopRepeating }.WaitLimitMs);
    }

    [Fact]
    public void The_countdown_shows_minutes_above_a_minute_and_rounds_seconds_up()
    {
        var step = new Step { Type = StepType.MoveMouse };

        step.ShowCountdown(120000, 33000, isTimeout: false);
        Assert.Contains("1:27", step.RemainingText);
        Assert.Equal(0.275, step.Progress, 3);

        step.ShowCountdown(120000, 119200, isTimeout: false);
        Assert.Contains("1 s", step.RemainingText);

        step.ClearCountdown();
        Assert.False(step.HasCountdown);
        Assert.Equal(0, step.Progress);
    }

    [Fact]
    public void The_running_state_is_not_saved_with_the_plan()
    {
        var step = new Step { Type = StepType.Delay, DelayMs = 500 };
        step.IsRunning = true;
        step.ShowCountdown(500, 100, isTimeout: false);

        var json = System.Text.Json.JsonSerializer.Serialize(step);

        Assert.DoesNotContain("IsRunning", json);
        Assert.DoesNotContain("Progress", json);
        Assert.DoesNotContain("RemainingText", json);
    }
}
