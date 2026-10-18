using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Clickpath.Localization;
using Clickpath.Models;

namespace Clickpath;

/// <summary>Selecting several steps in the tree with Ctrl+click, Shift+click and Ctrl+A (a WPF TreeView only selects one).</summary>
public partial class MainWindow
{
    readonly List<Step> _selection = [];   // every selected step, the TreeView's own selection included; empty = nothing selected
    Step? _anchor;                         // where a Shift+click range starts
    Step? _collapseOnRelease;              // plain click on a member of the selection: reduce to it on mouse up, unless a drag started
    bool _syncingSelection;                // true while the code moves the TreeView's own selection, so it does not reset ours

    /// <summary>The selected steps in tree order, without steps inside another selected step (that one takes them along).</summary>
    IReadOnlyList<Step> SelectedRoots =>
        _editor.TopLevelOf(_selection.Count > 0 ? _selection : SelectedStep is { } one ? [one] : []);

    void SetSelection(IEnumerable<Step> steps)
    {
        foreach (var step in _selection) step.IsMultiSelected = false;
        _selection.Clear();
        _selection.AddRange(steps);
        foreach (var step in _selection) step.IsMultiSelected = _selection.Count > 1;   // a single step shows the normal selection
        RefreshDetail();
    }

    /// <summary>Selects the steps; the TreeView's own (single) selection is kept on one of them.</summary>
    void ApplySelection(IReadOnlyList<Step> steps)
    {
        SetSelection(steps);
        if (steps.Count > 0 && (SelectedStep is null || !steps.Contains(SelectedStep))) MakePrimary(steps[^1]);
    }

    void MakePrimary(Step step)
    {
        _syncingSelection = true;
        try { SelectContainer(StepTree, step); }
        finally { _syncingSelection = false; }
    }

    /// <summary>The editor shows one step; with several selected it shows how many instead.</summary>
    GridLength _detailHeight = new(1, GridUnitType.Star);

    /// <summary>The settings card only takes room while steps are selected; its height is remembered for the next time.</summary>
    void ShowDetailCard(bool show)
    {
        if (show == (DetailCard.Visibility == Visibility.Visible)) return;
        if (!show) _detailHeight = DetailRow.Height;
        DetailRow.Height = show ? _detailHeight : new GridLength(0);
        DetailRow.MinHeight = show ? 150 : 0;
        DetailCard.Visibility = DetailSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    void RefreshDetail()
    {
        var many = _selection.Count > 1;
        var one = !many && StepTree.SelectedItem is Step;
        ShowDetailCard((one || many) && !_running);   // during a run the settings are out of the way, the list gets the room
        DetailHeader.Visibility = DetailScroll.Visibility = one ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Visibility = many ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Text = many ? string.Format(Loc.T("Detail.Multi"), _selection.Count) : "";
        Properties.IsEnabled = DetailHeader.IsEnabled = !_running;
    }

    /// <summary>Ctrl and Shift clicks change the selection here; a plain click on a selected step is kept for a possible drag. True = handled.</summary>
    bool HandleSelectionClick(MouseButtonEventArgs e)
    {
        if (_running || ItemAt(e.OriginalSource as DependencyObject)?.DataContext is not Step step) return false;
        var modifiers = Keyboard.Modifiers;
        var ctrl = modifiers.HasFlag(ModifierKeys.Control);
        var shift = modifiers.HasFlag(ModifierKeys.Shift);

        if (ctrl || shift)
        {
            var current = _selection.Count > 0 ? _selection.ToList() : SelectedStep is { } primary ? [primary] : [];
            if (shift)
            {
                var range = _editor.Range(_anchor ?? SelectedStep ?? step, step);
                current = ctrl ? current.Union(range).ToList() : range.ToList();
            }
            else
            {
                if (!current.Remove(step)) current.Add(step);
                if (current.Count == 0) current.Add(step);
                _anchor = step;
            }
            ApplySelection(current);
            StepTree.Focus();
            e.Handled = true;
            return true;
        }

        if (_selection.Count > 1 && _selection.Contains(step))
        {
            _collapseOnRelease = step;
            StepTree.Focus();
            e.Handled = true;
            return true;
        }
        return false;
    }

    void OnStepMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_collapseOnRelease is not { } step) return;
        _collapseOnRelease = null;
        ApplySelection([step]);
        _anchor = step;
    }

    void OnStepKeyDown(object sender, KeyEventArgs e)
    {
        if (_running) return;
        if (e.Key == Key.Delete)
        {
            OnDeleteStep(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control && _steps.Count > 0)
        {
            ApplySelection(_editor.InOrder().ToList());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Alt)
        {
            // Alt+arrows move the selection like a drag would: up/down one position, left/right out of / into a group
            switch (e.Key == Key.System ? e.SystemKey : e.Key)
            {
                case Key.Up: OnMoveUp(sender, e); break;
                case Key.Down: OnMoveDown(sender, e); break;
                case Key.Left: OnOutdent(sender, e); break;
                case Key.Right: OnIndent(sender, e); break;
                default: return;
            }
            e.Handled = true;
        }
    }

    /// <summary>After a move: selects the same steps again in the rebuilt tree.</summary>
    void ReselectMany(IReadOnlyList<Step> steps) =>
        Dispatcher.BeginInvoke(() =>
        {
            MakePrimary(steps[0]);
            ApplySelection(steps);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
}
