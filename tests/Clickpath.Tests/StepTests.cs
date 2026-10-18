using Clickpath.Engine;
using Clickpath.Localization;
using Clickpath.Models;

namespace Clickpath.Tests;

public class StepTests
{
    static Step Click(bool newTab = false, bool close = false) =>
        new() { Type = StepType.Click, NewTab = newTab, CloseNewWindow = close };

    [Fact]
    public void Every_step_type_has_a_translated_title_and_description()
    {
        foreach (var type in Enum.GetValues<StepType>())
        {
            Assert.NotEqual($"Step.{type}", Loc.T($"Step.{type}"));
            Assert.NotEqual($"Desc.{type}", Loc.T($"Desc.{type}"));
        }
    }

    [Fact]
    public void A_click_stays_on_its_tab_by_default()
    {
        Assert.Equal(NewWindowMode.Stay, Click().NewWindowMode);
    }

    [Fact]
    public void The_new_tab_flag_means_follow_the_new_window()
    {
        Assert.Equal(NewWindowMode.Follow, Click(newTab: true).NewWindowMode);
    }

    [Fact]
    public void The_close_flag_means_stay_and_close_the_new_window()
    {
        Assert.Equal(NewWindowMode.StayAndClose, Click(close: true).NewWindowMode);
    }

    [Theory]
    [InlineData(NewWindowMode.Follow, true, false)]
    [InlineData(NewWindowMode.Stay, false, false)]
    [InlineData(NewWindowMode.StayAndClose, false, true)]
    public void Setting_the_mode_sets_both_flags(NewWindowMode mode, bool newTab, bool close)
    {
        var step = Click(newTab: !newTab, close: !close);

        step.NewWindowMode = mode;

        Assert.Equal(newTab, step.NewTab);
        Assert.Equal(close, step.CloseNewWindow);
        Assert.Equal(mode, step.NewWindowMode);
    }

    [Fact]
    public void The_repeat_flag_means_close_and_click_again()
    {
        Assert.Equal(NewWindowMode.CloseAndRepeat, new Step { Type = StepType.Click, RepeatUntilNoWindow = true }.NewWindowMode);
    }

    [Fact]
    public void Choosing_the_repeat_mode_sets_only_the_repeat_flag()
    {
        var step = Click(newTab: true, close: true);

        step.NewWindowMode = NewWindowMode.CloseAndRepeat;

        Assert.True(step.RepeatUntilNoWindow);
        Assert.False(step.NewTab);
        Assert.False(step.CloseNewWindow);
        step.NewWindowMode = NewWindowMode.Stay;
        Assert.False(step.RepeatUntilNoWindow);
    }

    [Fact]
    public void The_repeat_flag_survives_saving_and_loading()
    {
        var saved = PlanFile.ToJson([new Step { Type = StepType.ClickPage, RepeatUntilNoWindow = true }]);

        Assert.Equal(NewWindowMode.CloseAndRepeat, PlanFile.FromJson(saved)[0].NewWindowMode);
    }

    [Fact]
    public void The_human_mouse_option_is_off_by_default_and_survives_saving_and_loading()
    {
        Assert.False(Click().HumanMouse);

        var saved = PlanFile.ToJson([new Step { Type = StepType.Click, HumanMouse = true }, Click()]);
        var loaded = PlanFile.FromJson(saved);

        Assert.True(loaded[0].HumanMouse);
        Assert.False(loaded[1].HumanMouse);
    }

    [Fact]
    public void Plan_files_without_the_human_mouse_option_load_with_it_off()
    {
        Assert.False(PlanFile.FromJson("""[ { "Type": "Click", "Text": "x" } ]""")[0].HumanMouse);
    }

    [Fact]
    public void Changing_the_flags_notifies_the_mode()
    {
        var step = Click();
        var changed = new List<string?>();
        step.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        step.CloseNewWindow = true;

        Assert.Contains(nameof(Step.NewWindowMode), changed);
    }

    [Fact]
    public void Plan_files_written_before_the_option_existed_keep_their_meaning()
    {
        var steps = PlanFile.FromJson("""[ { "Type": "Click", "Text": "x", "NewTab": true }, { "Type": "Click", "Text": "y" } ]""");

        Assert.Equal(NewWindowMode.Follow, steps[0].NewWindowMode);
        Assert.Equal(NewWindowMode.Stay, steps[1].NewWindowMode);
    }

    [Fact]
    public void The_close_flag_survives_saving_and_loading()
    {
        var saved = PlanFile.ToJson([Click(close: true)]);

        Assert.Equal(NewWindowMode.StayAndClose, PlanFile.FromJson(saved)[0].NewWindowMode);
    }
}
