using System.Collections.ObjectModel;
using Clickpath.Models;

namespace Clickpath.Tests;

public class PlanEditorTests
{
    static Step Group() => Step.Create(StepType.ForEachLink);
    static Step Leaf(string text) => new() { Type = StepType.Click, Text = text };

    static (PlanEditor Editor, ObservableCollection<Step> Root) Plan(params Step[] steps)
    {
        var root = new ObservableCollection<Step>(steps);
        return (new PlanEditor(root), root);
    }

    [Fact]
    public void Add_inside_a_group_puts_the_step_into_it()
    {
        var group = Group(); var click = Leaf("a");
        var (editor, root) = Plan(group);

        editor.Add(click, group, Place.Inside);

        Assert.Equal([group], root);
        Assert.Equal([click], group.Children);
    }

    [Fact]
    public void Add_after_a_group_puts_the_step_next_to_it()
    {
        var group = Group(); var open = Leaf("open");
        var (editor, root) = Plan(group);

        editor.Add(open, group, Place.After);

        Assert.Equal([group, open], root);
        Assert.Empty(group.Children);
    }

    [Fact]
    public void Add_before_a_group_puts_the_step_in_front_of_it()
    {
        var group = Group(); var open = Leaf("open");
        var (editor, root) = Plan(group);

        editor.Add(open, group, Place.Before);

        Assert.Equal([open, group], root);
    }

    [Fact]
    public void Add_after_a_child_stays_in_the_group()
    {
        var group = Group(); var first = Leaf("a"); var second = Leaf("b");
        var (editor, root) = Plan(group);
        editor.Add(first, group, Place.Inside);

        editor.Add(second, first, Place.After);

        Assert.Equal([group], root);
        Assert.Equal([first, second], group.Children);
    }

    [Fact]
    public void Add_without_anchor_appends_to_the_end()
    {
        var group = Group(); var last = Leaf("z");
        var (editor, root) = Plan(group);

        editor.Add(last, null, Place.After);

        Assert.Equal([group, last], root);
    }

    [Fact]
    public void Move_swaps_neighbours_within_a_level()
    {
        var a = Leaf("a"); var b = Leaf("b");
        var (editor, root) = Plan(a, b);

        Assert.True(editor.Move(a, 1));
        Assert.Equal([b, a], root);
    }

    [Fact]
    public void Move_up_from_the_start_of_a_group_leaves_it_in_front_of_the_group()
    {
        var group = Group(); var child = Leaf("a");
        var (editor, root) = Plan(group);
        group.Children.Add(child);

        Assert.True(editor.Move(child, -1));
        Assert.Equal([child, group], root);
        Assert.Empty(group.Children);
    }

    [Fact]
    public void Move_down_from_the_end_of_a_group_leaves_it_behind_the_group()
    {
        var group = Group(); var child = Leaf("a");
        var (editor, root) = Plan(group);
        group.Children.Add(child);

        Assert.True(editor.Move(child, 1));
        Assert.Equal([group, child], root);
        Assert.Empty(group.Children);
    }

    [Fact]
    public void Move_at_the_edge_of_the_root_does_nothing()
    {
        var a = Leaf("a"); var b = Leaf("b");
        var (editor, root) = Plan(a, b);

        Assert.False(editor.Move(a, -1));
        Assert.False(editor.Move(b, 1));
        Assert.Equal([a, b], root);
    }

    [Fact]
    public void Outdent_moves_a_child_behind_its_group()
    {
        var group = Group(); var child = Leaf("a"); var tail = Leaf("t");
        var (editor, root) = Plan(group, tail);
        group.Children.Add(child);

        Assert.True(editor.Outdent(child));
        Assert.Equal([group, child, tail], root);
    }

    [Fact]
    public void Outdent_of_a_top_level_step_does_nothing()
    {
        var a = Leaf("a");
        var (editor, root) = Plan(a);

        Assert.False(editor.Outdent(a));
        Assert.Equal([a], root);
    }

    [Fact]
    public void Indent_moves_a_step_into_the_group_above_it()
    {
        var group = Group(); var existing = Leaf("a"); var next = Leaf("b");
        var (editor, root) = Plan(group, next);
        group.Children.Add(existing);

        Assert.True(editor.Indent(next));
        Assert.Equal([group], root);
        Assert.Equal([existing, next], group.Children);
    }

    [Fact]
    public void Indent_without_a_group_above_does_nothing()
    {
        var a = Leaf("a"); var b = Leaf("b");
        var (editor, root) = Plan(a, b);

        Assert.False(editor.Indent(b));
        Assert.Equal([a, b], root);
    }

    [Fact]
    public void Default_place_is_inside_an_empty_group_and_after_everything_else()
    {
        var empty = Group();
        var filled = Group(); filled.Children.Add(Leaf("a"));

        Assert.Equal(Place.Inside, PlanEditor.DefaultPlace(empty));
        Assert.Equal(Place.After, PlanEditor.DefaultPlace(filled));
        Assert.Equal(Place.After, PlanEditor.DefaultPlace(Leaf("a")));
        Assert.Equal(Place.After, PlanEditor.DefaultPlace(null));
    }

    static Step LinkListStep() => Step.Create(StepType.ForEachListedLink);

    [Fact]
    public void A_link_goes_into_the_selected_link_list()
    {
        var first = LinkListStep(); var second = LinkListStep();
        var (editor, _) = Plan(first, second);

        Assert.Same(second, editor.FindLinkList(second, first));
    }

    [Fact]
    public void Without_a_selected_list_the_last_used_one_is_taken_while_it_is_in_the_plan()
    {
        var first = LinkListStep(); var second = LinkListStep();
        var (editor, root) = Plan(first, Leaf("a"), second);

        Assert.Same(second, editor.FindLinkList(root[1], second));
        root.Remove(second);
        Assert.Same(first, editor.FindLinkList(root[1], second));
    }

    [Fact]
    public void A_link_list_inside_a_group_is_found_and_a_plan_without_one_gives_null()
    {
        var group = Group(); var inner = LinkListStep();
        group.Children.Add(inner);
        var (editor, _) = Plan(Leaf("a"), group);
        var (empty, _) = Plan(Leaf("a"), Group());

        Assert.Same(inner, editor.FindLinkList(null, null));
        Assert.Null(empty.FindLinkList(null, null));
    }
}
