using System.Collections.ObjectModel;

namespace Clickpath.Models;

/// <summary>Where a new step goes relative to the anchor step.</summary>
public enum Place { Before, Inside, After }

/// <summary>Structural edits of the step tree: the root list, with groups ("repeat", "if") holding children.</summary>
public sealed class PlanEditor(IList<Step> root)
{
    public IList<Step>? ListOf(Step step) => FindList(step, root);

    /// <summary>The group that holds <paramref name="step"/>, or null for a top-level step.</summary>
    public Step? GroupOf(Step step) => FindGroup(step, root);

    /// <summary>"Inside" only applies to groups; for any other anchor it means "after".</summary>
    public void Add(Step step, Step? anchor, Place place)
    {
        if (anchor is null) { root.Add(step); return; }
        if (place == Place.Inside && anchor.CanHaveChildren) { anchor.Children.Add(step); return; }
        if (ListOf(anchor) is not { } list) { root.Add(step); return; }
        list.Insert(list.IndexOf(anchor) + (place == Place.Before ? 0 : 1), step);
    }

    /// <summary>Moves one position; at the edge of a group the step leaves it (in front of / behind the group).</summary>
    public bool Move(Step step, int delta)
    {
        if (ListOf(step) is not { } list) return false;
        var from = list.IndexOf(step);
        var to = from + delta;
        if (to >= 0 && to < list.Count) { Reorder(list, from, to); return true; }

        if (GroupOf(step) is not { } group || ListOf(group) is not { } outer) return false;
        list.RemoveAt(from);
        outer.Insert(outer.IndexOf(group) + (delta < 0 ? 0 : 1), step);
        return true;
    }

    /// <summary>One level up: behind the group the step is in.</summary>
    public bool Outdent(Step step)
    {
        if (GroupOf(step) is not { } group || ListOf(group) is not { } outer) return false;
        group.Children.Remove(step);
        outer.Insert(outer.IndexOf(group) + 1, step);
        return true;
    }

    /// <summary>One level down: at the end of the group directly above the step.</summary>
    public bool Indent(Step step)
    {
        if (ListOf(step) is not { } list) return false;
        var index = list.IndexOf(step);
        if (index == 0 || list[index - 1] is not { CanHaveChildren: true } above) return false;
        list.RemoveAt(index);
        above.Children.Add(step);
        return true;
    }

    /// <summary>Whether <paramref name="step"/> (with everything inside it) could be dropped before/inside/after <paramref name="target"/> and change something.</summary>
    public bool CanMoveTo(Step step, Step target, Place place)
    {
        if (step == target || IsWithin(target, step)) return false;
        if (place == Place.Inside && !target.CanHaveChildren) return false;
        if (ListOf(step) is not { } from || ListOf(target) is not { } targetList) return false;

        if (place == Place.Inside) return target.Children.LastOrDefault() != step;
        if (from != targetList) return true;
        var distance = targetList.IndexOf(target) - targetList.IndexOf(step);
        return place == Place.Before ? distance != 1 : distance != -1;   // already exactly there
    }

    /// <summary>Drag and drop: moves the step with all its children next to / into the target. False (and no change) if that is not possible.</summary>
    public bool MoveTo(Step step, Step target, Place place)
    {
        if (!CanMoveTo(step, target, place)) return false;
        ListOf(step)!.Remove(step);
        if (place == Place.Inside) target.Children.Add(step);
        else
        {
            var list = ListOf(target)!;
            list.Insert(list.IndexOf(target) + (place == Place.Before ? 0 : 1), step);
        }
        return true;
    }

    // ---- Several selected steps ----

    /// <summary>Every step of the plan, depth first, each group before the steps inside it (the order the tree shows).</summary>
    public IEnumerable<Step> InOrder() => Flatten(root);

    /// <summary>The link list a link from the browser goes into: the selected one, else the one used last (if still in the plan), else the first.</summary>
    public Step? FindLinkList(Step? selected, Step? last)
    {
        if (selected is { Type: StepType.ForEachListedLink }) return selected;
        var lists = InOrder().Where(s => s.Type == StepType.ForEachListedLink).ToList();
        return last is not null && lists.Contains(last) ? last : lists.FirstOrDefault();
    }

    /// <summary>The steps from <paramref name="a"/> to <paramref name="b"/> in tree order, both included, whichever comes first.</summary>
    public IReadOnlyList<Step> Range(Step a, Step b)
    {
        var all = InOrder().ToList();
        int i = all.IndexOf(a), j = all.IndexOf(b);
        if (i < 0 || j < 0) return [];
        return all.GetRange(Math.Min(i, j), Math.Abs(i - j) + 1);
    }

    /// <summary>The selected steps in tree order without those inside another selected step (that step takes them along).</summary>
    public IReadOnlyList<Step> TopLevelOf(IEnumerable<Step> steps)
    {
        var selected = steps.ToHashSet();
        return InOrder().Where(s => selected.Contains(s) && !HasSelectedGroup(s, selected)).ToList();
    }

    public bool CanMoveManyTo(IEnumerable<Step> steps, Step target, Place place)
    {
        var roots = TopLevelOf(steps);
        if (roots.Count == 0) return false;
        if (roots.Count == 1) return CanMoveTo(roots[0], target, place);
        if (ListOf(target) is null || (place == Place.Inside && !target.CanHaveChildren)) return false;
        return roots.All(r => r != target && !IsWithin(target, r));
    }

    /// <summary>Drag and drop of a selection: all of them (with their steps) next to / into the target, keeping their order.</summary>
    public bool MoveManyTo(IEnumerable<Step> steps, Step target, Place place)
    {
        if (!CanMoveManyTo(steps, target, place)) return false;
        var roots = TopLevelOf(steps);
        foreach (var step in roots) ListOf(step)!.Remove(step);
        if (place == Place.Inside)
        {
            foreach (var step in roots) target.Children.Add(step);
            return true;
        }
        var list = ListOf(target)!;
        var at = list.IndexOf(target) + (place == Place.Before ? 0 : 1);
        foreach (var step in roots) list.Insert(at++, step);
        return true;
    }

    /// <summary>One position up/down for the whole selection; if any of them is at the edge of its list nothing moves.</summary>
    public bool MoveMany(IEnumerable<Step> steps, int delta)
    {
        var roots = TopLevelOf(steps);
        if (roots.Count == 0) return false;
        if (roots.Count == 1) return Move(roots[0], delta);

        var placed = roots.Select(r => (Step: r, List: ListOf(r)!)).ToList();
        foreach (var group in placed.GroupBy(p => p.List))
        {
            var indexes = group.Select(p => p.List.IndexOf(p.Step)).ToList();
            if (indexes.Min() + delta < 0 || indexes.Max() + delta >= group.Key.Count) return false;
        }
        var order = delta < 0 ? placed.OrderBy(p => p.List.IndexOf(p.Step)) : placed.OrderByDescending(p => p.List.IndexOf(p.Step));
        foreach (var (step, list) in order.ToList())
        {
            var from = list.IndexOf(step);
            Reorder(list, from, from + delta);
        }
        return true;
    }

    public bool IndentMany(IEnumerable<Step> steps)
    {
        var changed = false;
        foreach (var step in TopLevelOf(steps)) changed |= Indent(step);
        return changed;
    }

    /// <summary>Last step first, so each one lands directly behind the group and the order is kept.</summary>
    public bool OutdentMany(IEnumerable<Step> steps)
    {
        var changed = false;
        foreach (var step in TopLevelOf(steps).Reverse()) changed |= Outdent(step);
        return changed;
    }

    public bool RemoveMany(IEnumerable<Step> steps)
    {
        var roots = TopLevelOf(steps);
        foreach (var step in roots) ListOf(step)?.Remove(step);
        return roots.Count > 0;
    }

    static IEnumerable<Step> Flatten(IEnumerable<Step> list)
    {
        foreach (var step in list)
        {
            yield return step;
            foreach (var child in Flatten(step.Children)) yield return child;
        }
    }

    bool HasSelectedGroup(Step step, HashSet<Step> selected)
    {
        for (var group = GroupOf(step); group is not null; group = GroupOf(group))
            if (selected.Contains(group)) return true;
        return false;
    }

    /// <summary>Where a drop lands for a pointer <paramref name="y"/> pixels down a row of <paramref name="height"/>: groups also take drops in their middle half.</summary>
    public static Place PlaceAt(double y, double height, bool targetIsGroup)
    {
        var fraction = height <= 0 ? 0.5 : y / height;
        if (!targetIsGroup) return fraction < 0.5 ? Place.Before : Place.After;
        return fraction < 0.25 ? Place.Before : fraction > 0.75 ? Place.After : Place.Inside;
    }

    static bool IsWithin(Step candidate, Step container) =>
        container.Children.Any(c => c == candidate || IsWithin(candidate, c));

    /// <summary>An empty group is almost always meant to be filled; everything else gets the new step behind it.</summary>
    public static Place DefaultPlace(Step? selected) =>
        selected is { CanHaveChildren: true, Children.Count: 0 } ? Place.Inside : Place.After;

    static IList<Step>? FindList(Step step, IList<Step> list)
    {
        if (list.Contains(step)) return list;
        return list.Select(s => FindList(step, s.Children)).FirstOrDefault(l => l is not null);
    }

    static Step? FindGroup(Step step, IEnumerable<Step> list)
    {
        foreach (var candidate in list)
        {
            if (candidate.Children.Contains(step)) return candidate;
            if (FindGroup(step, candidate.Children) is { } nested) return nested;
        }
        return null;
    }

    static void Reorder(IList<Step> list, int from, int to)
    {
        if (list is ObservableCollection<Step> observable) { observable.Move(from, to); return; }
        var step = list[from];
        list.RemoveAt(from);
        list.Insert(to, step);
    }
}
