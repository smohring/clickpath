using System.Collections.ObjectModel;
using Clickpath.Engine;
using Clickpath.Models;

namespace Clickpath.Tests;

public class StepSelectionTests
{
    static Step Group(string text) => new() { Type = StepType.ForEachLink, Text = text };
    static Step Leaf(string text) => new() { Type = StepType.Click, Text = text };

    static (PlanEditor Editor, ObservableCollection<Step> Root) Plan(params Step[] steps)
    {
        var root = new ObservableCollection<Step>(steps);
        return (new PlanEditor(root), root);
    }

    static string Texts(IEnumerable<Step> steps) => string.Join(",", steps.Select(s => s.Text));

    // plan used by several tests:  a, G{x, y{y1, y2}, z}, b
    static (PlanEditor Editor, ObservableCollection<Step> Root, Dictionary<string, Step> S) Sample()
    {
        var s = new[] { "a", "x", "y1", "y2", "z", "b" }.ToDictionary(t => t, Leaf);
        s["G"] = Group("G");
        s["y"] = Group("y");
        s["y"].Children.Add(s["y1"]);
        s["y"].Children.Add(s["y2"]);
        s["G"].Children.Add(s["x"]);
        s["G"].Children.Add(s["y"]);
        s["G"].Children.Add(s["z"]);
        var (editor, root) = Plan(s["a"], s["G"], s["b"]);
        return (editor, root, s);
    }

    [Fact]
    public void The_tree_is_listed_parents_before_their_children()
    {
        var (editor, _, _) = Sample();

        Assert.Equal("a,G,x,y,y1,y2,z,b", Texts(editor.InOrder()));
    }

    [Fact]
    public void A_range_runs_from_one_step_to_the_other_in_tree_order_whichever_comes_first()
    {
        var (editor, _, s) = Sample();

        Assert.Equal("x,y,y1,y2,z", Texts(editor.Range(s["x"], s["z"])));
        Assert.Equal("x,y,y1,y2,z", Texts(editor.Range(s["z"], s["x"])));
        Assert.Equal("a", Texts(editor.Range(s["a"], s["a"])));
    }

    [Fact]
    public void A_range_with_a_step_that_is_not_in_the_plan_is_empty()
    {
        var (editor, _, s) = Sample();

        Assert.Empty(editor.Range(s["a"], Leaf("stranger")));
    }

    [Fact]
    public void Steps_inside_a_selected_group_are_left_out_because_the_group_takes_them_along()
    {
        var (editor, _, s) = Sample();

        var roots = editor.TopLevelOf([s["y2"], s["G"], s["y"], s["b"], s["z"]]);

        Assert.Equal("G,b", Texts(roots));
    }

    [Fact]
    public void The_selected_steps_are_returned_in_tree_order()
    {
        var (editor, _, s) = Sample();

        Assert.Equal("a,z,b", Texts(editor.TopLevelOf([s["b"], s["z"], s["a"]])));
    }

    [Fact]
    public void Several_steps_can_be_dropped_before_a_step_and_keep_their_order()
    {
        var (editor, root, s) = Sample();

        Assert.True(editor.MoveManyTo([s["b"], s["a"]], s["z"], Place.Before));

        Assert.Equal("G", Texts(root));
        Assert.Equal("x,y,a,b,z", Texts(s["G"].Children));
    }

    [Fact]
    public void Several_steps_can_be_dropped_behind_a_step()
    {
        var (editor, root, s) = Sample();

        Assert.True(editor.MoveManyTo([s["x"], s["z"]], s["b"], Place.After));

        Assert.Equal("a,G,b,x,z", Texts(root));
        Assert.Equal("y", Texts(s["G"].Children));
    }

    [Fact]
    public void Several_steps_dropped_onto_a_group_go_to_its_end_in_order()
    {
        var (editor, root, s) = Sample();

        Assert.True(editor.MoveManyTo([s["a"], s["b"]], s["y"], Place.Inside));

        Assert.Equal("G", Texts(root));
        Assert.Equal("y1,y2,a,b", Texts(s["y"].Children));
    }

    [Fact]
    public void A_selected_group_takes_its_steps_along_when_moved_with_others()
    {
        var (editor, root, s) = Sample();

        Assert.True(editor.MoveManyTo([s["G"], s["a"]], s["b"], Place.After));

        Assert.Equal("b,a,G", Texts(root));
        Assert.Equal("x,y,z", Texts(s["G"].Children));
    }

    [Fact]
    public void Several_steps_cannot_be_dropped_onto_one_of_themselves_or_into_their_own_content()
    {
        var (editor, root, s) = Sample();

        Assert.False(editor.MoveManyTo([s["a"], s["G"]], s["G"], Place.Inside));
        Assert.False(editor.MoveManyTo([s["a"], s["G"]], s["z"], Place.Before));
        Assert.False(editor.CanMoveManyTo([s["a"], s["b"]], s["a"], Place.After));
        Assert.Equal("a,G,b", Texts(root));
    }

    [Fact]
    public void Only_groups_take_several_steps_inside()
    {
        var (editor, _, s) = Sample();

        Assert.False(editor.CanMoveManyTo([s["a"], s["b"]], s["x"], Place.Inside));
    }

    [Fact]
    public void One_selected_step_behaves_like_the_single_step_move()
    {
        var (editor, _, s) = Sample();

        Assert.False(editor.CanMoveManyTo([s["a"]], s["G"], Place.Before));   // already right in front of it
        Assert.True(editor.CanMoveManyTo([s["a"]], s["b"], Place.Before));
    }

    [Fact]
    public void A_block_of_steps_moves_one_position_together()
    {
        var (editor, root, s) = Plan4();

        Assert.True(editor.MoveMany([s["c"], s["d"]], -1));
        Assert.Equal("a,c,d,b", Texts(root));
        Assert.True(editor.MoveMany([s["c"], s["d"]], 1));
        Assert.Equal("a,b,c,d", Texts(root));
    }

    [Fact]
    public void A_block_stops_at_the_edge_and_nothing_moves()
    {
        var (editor, root, s) = Plan4();

        Assert.False(editor.MoveMany([s["a"], s["c"]], -1));
        Assert.False(editor.MoveMany([s["b"], s["d"]], 1));
        Assert.Equal("a,b,c,d", Texts(root));
    }

    [Fact]
    public void Steps_that_are_apart_each_move_one_position()
    {
        var (editor, root, s) = Plan4();

        Assert.True(editor.MoveMany([s["b"], s["d"]], -1));

        Assert.Equal("b,a,d,c", Texts(root));
    }

    [Fact]
    public void Indenting_several_steps_puts_them_into_the_group_above_in_order()
    {
        var group = Group("G");
        var (a, b) = (Leaf("a"), Leaf("b"));
        var (editor, root) = Plan(group, a, b);

        Assert.True(editor.IndentMany([a, b]));

        Assert.Equal("G", Texts(root));
        Assert.Equal("a,b", Texts(group.Children));
    }

    [Fact]
    public void Outdenting_several_steps_puts_them_behind_the_group_in_order()
    {
        var group = Group("G");
        var (a, b, c) = (Leaf("a"), Leaf("b"), Leaf("c"));
        group.Children.Add(a);
        group.Children.Add(b);
        group.Children.Add(c);
        var (editor, root) = Plan(group);

        Assert.True(editor.OutdentMany([a, b]));

        Assert.Equal("G,a,b", Texts(root));
        Assert.Equal("c", Texts(group.Children));
    }

    [Fact]
    public void Removing_several_steps_also_removes_what_is_inside_them()
    {
        var (editor, root, s) = Sample();

        Assert.True(editor.RemoveMany([s["G"], s["x"], s["b"]]));

        Assert.Equal("a", Texts(root));
    }

    [Fact]
    public void The_multi_selection_flag_notifies_and_is_not_saved()
    {
        var step = Leaf("a");
        var changed = new List<string?>();
        step.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        step.IsMultiSelected = true;
        step.IsMultiSelected = true;

        Assert.Equal([nameof(Step.IsMultiSelected)], changed);
        Assert.DoesNotContain("IsMultiSelected", PlanFile.ToJson([step]));
    }

    static (PlanEditor Editor, ObservableCollection<Step> Root, Dictionary<string, Step> S) Plan4()
    {
        var s = new[] { "a", "b", "c", "d" }.ToDictionary(t => t, Leaf);
        var (editor, root) = Plan(s["a"], s["b"], s["c"], s["d"]);
        return (editor, root, s);
    }
}
