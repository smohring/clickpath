using System.Collections.ObjectModel;
using Clickpath.Models;

namespace Clickpath.Tests;

public class StepMoveTests
{
    static Step Group(string text = "") => new() { Type = StepType.ForEachLink, Text = text };
    static Step Leaf(string text) => new() { Type = StepType.Click, Text = text };

    static (PlanEditor Editor, ObservableCollection<Step> Root) Plan(params Step[] steps)
    {
        var root = new ObservableCollection<Step>(steps);
        return (new PlanEditor(root), root);
    }

    static string Texts(IEnumerable<Step> steps) => string.Join(",", steps.Select(s => s.Text));

    [Fact]
    public void A_step_can_be_dropped_before_or_after_another_one_in_the_same_list()
    {
        var (a, b, c, d) = (Leaf("a"), Leaf("b"), Leaf("c"), Leaf("d"));
        var (editor, root) = Plan(a, b, c, d);

        Assert.True(editor.MoveTo(d, b, Place.Before));
        Assert.Equal("a,d,b,c", Texts(root));
        Assert.True(editor.MoveTo(a, c, Place.After));
        Assert.Equal("d,b,c,a", Texts(root));
    }

    [Fact]
    public void Dropping_a_step_in_front_of_the_next_one_or_behind_the_previous_one_changes_nothing()
    {
        var (a, b, c) = (Leaf("a"), Leaf("b"), Leaf("c"));
        var (editor, root) = Plan(a, b, c);

        Assert.False(editor.MoveTo(b, c, Place.Before));
        Assert.False(editor.MoveTo(b, a, Place.After));
        Assert.Equal("a,b,c", Texts(root));
    }

    [Fact]
    public void A_group_moves_with_all_its_steps()
    {
        var (a, b) = (Leaf("a"), Leaf("b"));
        var loop = Group("loop");
        loop.Children.Add(Leaf("x"));
        loop.Children.Add(Leaf("y"));
        var (editor, root) = Plan(a, loop, b);

        Assert.True(editor.MoveTo(loop, a, Place.Before));

        Assert.Equal("loop,a,b", Texts(root));
        Assert.Equal("x,y", Texts(loop.Children));
    }

    [Fact]
    public void Dropping_onto_a_group_puts_the_step_at_the_end_of_it()
    {
        var a = Leaf("a");
        var loop = Group("loop");
        loop.Children.Add(Leaf("x"));
        var (editor, root) = Plan(a, loop);

        Assert.True(editor.MoveTo(a, loop, Place.Inside));

        Assert.Equal("loop", Texts(root));
        Assert.Equal("x,a", Texts(loop.Children));
    }

    [Fact]
    public void A_step_can_leave_its_group_by_being_dropped_next_to_something_outside()
    {
        var after = Leaf("after");
        var loop = Group("loop");
        var x = Leaf("x");
        loop.Children.Add(x);
        loop.Children.Add(Leaf("y"));
        var (editor, root) = Plan(loop, after);

        Assert.True(editor.MoveTo(x, after, Place.After));

        Assert.Equal("loop,after,x", Texts(root));
        Assert.Equal("y", Texts(loop.Children));
    }

    [Fact]
    public void A_step_can_move_between_groups()
    {
        var (one, two) = (Group("one"), Group("two"));
        var x = Leaf("x");
        one.Children.Add(x);
        two.Children.Add(Leaf("y"));
        var (editor, _) = Plan(one, two);

        Assert.True(editor.MoveTo(x, two.Children[0], Place.Before));

        Assert.Empty(one.Children);
        Assert.Equal("x,y", Texts(two.Children));
    }

    [Fact]
    public void A_step_cannot_be_dropped_onto_itself_or_into_its_own_content()
    {
        var inner = Group("inner");
        var leaf = Leaf("leaf");
        inner.Children.Add(leaf);
        var outer = Group("outer");
        outer.Children.Add(inner);
        var (editor, root) = Plan(outer);

        Assert.False(editor.MoveTo(outer, outer, Place.Inside));
        Assert.False(editor.MoveTo(outer, inner, Place.Inside));
        Assert.False(editor.MoveTo(outer, leaf, Place.After));
        Assert.False(editor.CanMoveTo(outer, leaf, Place.Before));
        Assert.Equal("outer", Texts(root));
        Assert.Equal("inner", Texts(outer.Children));
    }

    [Fact]
    public void Only_groups_accept_a_drop_inside_them()
    {
        var (a, b) = (Leaf("a"), Leaf("b"));
        var (editor, root) = Plan(a, b);

        Assert.False(editor.CanMoveTo(a, b, Place.Inside));
        Assert.False(editor.MoveTo(a, b, Place.Inside));
        Assert.Equal("a,b", Texts(root));
    }

    [Fact]
    public void A_target_that_is_not_in_the_plan_is_refused()
    {
        var a = Leaf("a");
        var (editor, root) = Plan(a);

        Assert.False(editor.MoveTo(a, Leaf("stranger"), Place.After));
        Assert.Equal("a", Texts(root));
    }

    [Fact]
    public void Checking_does_not_change_anything()
    {
        var (a, b) = (Leaf("a"), Leaf("b"));
        var (editor, root) = Plan(a, b);

        Assert.True(editor.CanMoveTo(b, a, Place.Before));

        Assert.Equal("a,b", Texts(root));
    }

    [Theory]
    [InlineData(0.00, false, Place.Before)]
    [InlineData(0.49, false, Place.Before)]
    [InlineData(0.51, false, Place.After)]
    [InlineData(1.00, false, Place.After)]
    [InlineData(0.10, true, Place.Before)]
    [InlineData(0.30, true, Place.Inside)]
    [InlineData(0.70, true, Place.Inside)]
    [InlineData(0.90, true, Place.After)]
    public void The_drop_position_comes_from_where_in_the_row_the_pointer_is(double fraction, bool targetIsGroup, Place expected)
    {
        Assert.Equal(expected, PlanEditor.PlaceAt(fraction * 40, 40, targetIsGroup));
    }

    [Fact]
    public void Changing_the_kind_of_a_group_keeps_its_steps()
    {
        var loop = Step.Create(StepType.ForEachLink);
        loop.Children.Add(Leaf("x"));
        loop.Children.Add(Leaf("y"));

        loop.ConvertTo(StepType.ForEachListedLink);

        Assert.Equal(StepType.ForEachListedLink, loop.Type);
        Assert.Equal("x,y", Texts(loop.Children));
    }

    [Fact]
    public void Changing_between_a_loop_and_a_condition_adjusts_what_it_looks_for_and_the_default_wait()
    {
        var loop = Step.Create(StepType.ForEachLink);
        Assert.Equal(StepTarget.Link, loop.Target);
        Assert.Equal(10000, loop.TimeoutMs);

        loop.ConvertTo(StepType.IfFound);

        Assert.Equal(2000, loop.TimeoutMs);
        Assert.Equal(StepTarget.Clickable, loop.Target);

        loop.ConvertTo(StepType.ForEachLink);
        Assert.Equal(10000, loop.TimeoutMs);
        Assert.Equal(StepTarget.Link, loop.Target);
    }

    [Fact]
    public void A_wait_the_user_chose_is_kept_when_the_kind_changes()
    {
        var loop = Step.Create(StepType.ForEachLink);
        loop.TimeoutMs = 7000;
        loop.Text = "Item ";

        loop.ConvertTo(StepType.IfFound);

        Assert.Equal(7000, loop.TimeoutMs);
        Assert.Equal("Item ", loop.Text);
    }

    [Fact]
    public void A_step_with_steps_inside_cannot_become_a_step_that_holds_none()
    {
        var loop = Step.Create(StepType.ForEachLink);
        loop.Children.Add(Leaf("x"));

        Assert.False(loop.CanBecome(StepType.Click));
        Assert.True(loop.CanBecome(StepType.IfFound));
        loop.ConvertTo(StepType.Click);

        Assert.Equal(StepType.ForEachLink, loop.Type);
    }

    [Fact]
    public void The_group_kinds_offered_in_the_editor_are_exactly_the_steps_that_can_hold_steps()
    {
        var expected = Enum.GetValues<StepType>().Where(t => Step.Create(t).CanHaveChildren).ToList();

        Assert.Equal(expected, Options.GroupTypes.Select(o => o.Value).ToList());
        Assert.Contains(StepType.ForEachListedLink, expected);
        Assert.All(Options.GroupTypes, o => Assert.NotEqual($"Step.{o.Value}", o.Label));
    }
}
