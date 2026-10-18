using Clickpath.Engine;
using Clickpath.Models;

namespace Clickpath.Tests;

public class ForEachElementTests
{
    static Step Loop() => Step.Create(StepType.ForEachElement);

    [Fact]
    public void The_element_loop_is_a_group_that_searches_the_page_clicks_and_can_continue_on_error()
    {
        var loop = Loop();

        Assert.True(loop.CanHaveChildren);
        Assert.True(loop.UsesMatch);
        Assert.True(loop.UsesNewWindow);
        Assert.True(loop.UsesHumanMouse);
        Assert.True(loop.UsesContinueOnError);
        Assert.True(loop.UsesClickEachMatch);
    }

    [Fact]
    public void It_looks_for_things_to_click_and_clicks_each_match_by_default()
    {
        var loop = Loop();

        Assert.Equal(StepTarget.Clickable, loop.Target);
        Assert.True(loop.ClickEachMatch);
    }

    [Fact]
    public void Only_the_element_loop_offers_the_click_each_match_option()
    {
        Assert.False(new Step { Type = StepType.ForEachLink }.UsesClickEachMatch);
        Assert.False(new Step { Type = StepType.Click }.UsesClickEachMatch);
    }

    [Fact]
    public void The_stop_step_holds_nothing_and_has_no_fields()
    {
        var stop = Step.Create(StepType.StopRepeating);

        Assert.False(stop.CanHaveChildren);
        Assert.False(stop.UsesMatch);
        Assert.False(stop.UsesNewWindow);
        Assert.Equal("", stop.Subtitle);
    }

    [Fact]
    public void The_new_steps_can_be_swapped_with_the_other_groups_without_losing_steps()
    {
        var loop = Loop();
        loop.Children.Add(new Step { Type = StepType.Click, Text = "x" });

        Assert.True(loop.CanBecome(StepType.ForEachLink));
        Assert.True(loop.CanBecome(StepType.IfFound));
        Assert.False(loop.CanBecome(StepType.StopRepeating));
        Assert.Contains(StepType.ForEachElement, Options.GroupTypes.Select(o => o.Value));
        Assert.DoesNotContain(StepType.StopRepeating, Options.GroupTypes.Select(o => o.Value));
    }

    [Fact]
    public void The_options_survive_saving_and_loading()
    {
        var saved = PlanFile.ToJson([new Step { Type = StepType.ForEachElement, Text = "Download Mirror", ClickEachMatch = false, NearText = "x" }]);
        var loaded = PlanFile.FromJson(saved)[0];

        Assert.Equal(StepType.ForEachElement, loaded.Type);
        Assert.False(loaded.ClickEachMatch);
        Assert.Equal("Download Mirror", loaded.Text);
        Assert.Equal("x", loaded.NearText);
        Assert.DoesNotContain("MatchIndex", saved);   // internal, used for the click of one iteration only
    }

    [Fact]
    public void A_query_can_ask_for_the_nth_match_and_defaults_to_the_first()
    {
        var step = new Step { Type = StepType.Click, Text = "Download Mirror" };

        Assert.Equal(0, step.ToQuery().Index);
        step.MatchIndex = 2;
        Assert.Equal(2, step.ToQuery().Index);
    }

    [Fact]
    public void The_subtitle_describes_what_is_clicked_like_the_click_step()
    {
        var loop = Loop();
        loop.Text = "Download Mirror";

        Assert.Contains("Download Mirror", loop.Subtitle);
    }

    [Fact]
    public void The_stop_condition_is_off_by_default_and_waits_two_seconds_after_each_round()
    {
        var loop = Loop();

        Assert.Equal("", loop.UntilText);
        Assert.Equal(TextMatch.Contains, loop.UntilMode);
        Assert.Equal(2000, loop.UntilWaitMs);
        Assert.Null(loop.ToUntilQuery());
        Assert.Null(new Step { Type = StepType.ForEachElement, UntilText = "   " }.ToUntilQuery());
    }

    [Fact]
    public void The_stop_condition_looks_for_any_visible_text_with_the_chosen_match_mode()
    {
        var loop = Loop();
        loop.UntilText = "Download von AlphaHost";
        loop.UntilMode = TextMatch.StartsWith;

        var query = loop.ToUntilQuery()!;

        Assert.Equal("^Download von AlphaHost", query.Pattern);
        Assert.True(query.Deepest);          // only the innermost element that holds the text
        Assert.Contains("body", query.Selector);
        Assert.Equal("", query.Near);
    }

    [Fact]
    public void The_stop_condition_fills_in_variables()
    {
        var loop = Loop();
        loop.UntilText = "Mirror {index}";

        Assert.Equal("Mirror 2", loop.ToUntilQuery(s => s.Replace("{index}", "2"))!.Pattern);
    }

    [Fact]
    public void The_wait_cannot_be_negative_and_zero_means_check_once()
    {
        Assert.Equal(0, new Step { UntilWaitMs = -5 }.UntilWaitMs);
        Assert.Equal(0, new Step { UntilWaitMs = 0 }.UntilWaitMs);
    }

    [Fact]
    public void The_stop_condition_survives_saving_and_loading_and_shows_only_for_the_element_loop()
    {
        var saved = PlanFile.ToJson([new Step { Type = StepType.ForEachElement, UntilText = "x", UntilMode = TextMatch.Exactly, UntilWaitMs = 900 }]);
        var loaded = PlanFile.FromJson(saved)[0];

        Assert.Equal(("x", TextMatch.Exactly, 900), (loaded.UntilText, loaded.UntilMode, loaded.UntilWaitMs));
        Assert.True(loaded.UsesUntil);
        Assert.False(new Step { Type = StepType.ForEachLink }.UsesUntil);
        Assert.Equal("", PlanFile.FromJson("""[ { "Type": "ForEachElement" } ]""")[0].UntilText);
    }

    [Fact]
    public void The_subtitle_names_the_stop_condition_when_there_is_one()
    {
        var loop = Loop();
        loop.Text = "Download Mirror";
        var without = loop.Subtitle;
        loop.UntilText = "AlphaHost";

        Assert.Contains("AlphaHost", loop.Subtitle);
        Assert.DoesNotContain("AlphaHost", without);
    }

    [Fact]
    public void A_step_that_leaves_the_loop_is_offered_in_the_menu_after_the_other_steps()
    {
        var types = Enum.GetValues<StepType>().ToList();

        Assert.Equal(types.IndexOf(StepType.ForEachListedLink) + 1, types.IndexOf(StepType.ForEachElement));
        Assert.Equal(types.Count - 1, types.IndexOf(StepType.StopRepeating));
    }
}
