using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Clickpath.Models;

namespace Clickpath;

/// <summary>Drag and drop of steps in the step tree, and changing the kind of a group step.</summary>
public partial class MainWindow
{
    Point _dragOrigin;
    Step? _dragCandidate;
    DropAdorner? _dropAdorner;

    /// <summary>Remembers the step the left button went down on; a drag starts once the pointer has moved far enough.</summary>
    void RememberDragStart(MouseButtonEventArgs e)
    {
        _dragOrigin = e.GetPosition(null);
        _dragCandidate = ItemAt(e.OriginalSource as DependencyObject)?.DataContext as Step;
    }

    void OnStepDragMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is not { } step || _running) return;
        var moved = e.GetPosition(null) - _dragOrigin;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _dragCandidate = null;
        _collapseOnRelease = null;
        // dragging a step of the selection takes the whole selection along
        Step[] dragged = _selection.Count > 1 && _selection.Contains(step) ? [.. SelectedRoots] : [step];
        DragDrop.DoDragDrop(StepTree, new DataObject(typeof(Step[]), dragged), DragDropEffects.Move);
        HideDropMarker();
    }

    void OnStepDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (e.Data.GetData(typeof(Step[])) is not Step[] steps) { HideDropMarker(); return; }

        AutoScroll(e.GetPosition(StepTree));
        if (!TryGetDrop(e, out var target, out var place, out var bounds) || !_editor.CanMoveManyTo(steps, target, place))
        {
            HideDropMarker();
            return;
        }
        e.Effects = DragDropEffects.Move;
        ShowDropMarker(bounds, place);
    }

    void OnStepDragLeave(object sender, DragEventArgs e) => HideDropMarker();

    void OnStepDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        HideDropMarker();
        if (e.Data.GetData(typeof(Step[])) is not Step[] steps || !TryGetDrop(e, out var target, out var place, out _)) return;
        if (!_editor.MoveManyTo(steps, target, place)) return;
        if (steps.Length == 1) SelectInTree(steps[0]);
        else ReselectMany(steps);
    }

    /// <summary>The row under the pointer and where in it a drop would land; below the last row it means "at the end".</summary>
    bool TryGetDrop(DragEventArgs e, out Step target, out Place place, out Rect bounds)
    {
        target = null!;
        place = Place.After;
        bounds = Rect.Empty;
        var point = e.GetPosition(StepTree);

        if (ItemAt(StepTree.InputHitTest(point) as DependencyObject) is not { DataContext: Step step } item)
        {
            if (_steps.Count == 0 || StepTree.ItemContainerGenerator.ContainerFromIndex(_steps.Count - 1) is not TreeViewItem last) return false;
            var lastBox = BoundsIn(last);
            if (point.Y <= lastBox.Bottom) return false;
            (target, place, bounds) = (_steps[^1], Place.After, FullWidth(lastBox));
            return true;
        }

        var header = HeaderOf(item);
        if (header is null) return false;
        var inRow = e.GetPosition(header);
        target = step;
        place = PlanEditor.PlaceAt(inRow.Y, header.ActualHeight, step.CanHaveChildren);
        // after a group with visible steps the drop lands behind all of them, so the line goes under the whole group
        var row = place == Place.After && step.Children.Count > 0 ? BoundsIn(item) : BoundsIn(header);
        bounds = FullWidth(row);
        return true;
    }

    /// <summary>The marker spans the whole list, not just the text of the row (the row template is only as wide as its content).</summary>
    Rect FullWidth(Rect row) => new(4, row.Y, Math.Max(0, StepTree.ActualWidth - 12), row.Height);

    Rect BoundsIn(FrameworkElement element) => new(element.TranslatePoint(new Point(0, 0), StepTree), new Size(element.ActualWidth, element.ActualHeight));

    static TreeViewItem? ItemAt(DependencyObject? source)
    {
        for (var node = source; node is not null; node = ParentOf(node))
        {
            if (node is ToggleButton) return null;
            if (node is TreeViewItem item) return item;
        }
        return null;
    }

    /// <summary>The row of a step (the template's grid), not including the steps inside it.</summary>
    static FrameworkElement? HeaderOf(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Grid { Tag: "StepRow" } row) return row;
            if (HeaderOf(child) is { } found) return found;
        }
        return null;
    }

    void ShowDropMarker(Rect bounds, Place place)
    {
        if (_dropAdorner is null)
        {
            _dropAdorner = new DropAdorner(StepTree);
            AdornerLayer.GetAdornerLayer(StepTree)?.Add(_dropAdorner);
        }
        _dropAdorner.Bounds = bounds;
        _dropAdorner.Place = place;
        _dropAdorner.InvalidateVisual();
    }

    void HideDropMarker()
    {
        if (_dropAdorner is null) return;
        AdornerLayer.GetAdornerLayer(StepTree)?.Remove(_dropAdorner);
        _dropAdorner = null;
    }

    /// <summary>Scrolls the list while a step is dragged close to its top or bottom edge.</summary>
    void AutoScroll(Point point)
    {
        if (FindScrollViewer(StepTree) is not { } scroll) return;
        if (point.Y < 24) scroll.ScrollToVerticalOffset(scroll.VerticalOffset - 16);
        else if (point.Y > StepTree.ActualHeight - 24) scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 16);
    }

    static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer scroll) return scroll;
            if (FindScrollViewer(child) is { } found) return found;
        }
        return null;
    }

    /// <summary>The "kind" box in the editor header: swaps a group step for another kind and keeps the steps inside it.</summary>
    void OnGroupTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { DataContext: Step step, SelectedValue: StepType type } || type == step.Type) return;
        step.ConvertTo(type);
    }
}
