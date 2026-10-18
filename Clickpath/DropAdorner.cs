using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Clickpath.Models;

namespace Clickpath;

/// <summary>Marks where a dragged step will land: a line before/after a row, or a frame around a group.</summary>
public sealed class DropAdorner : Adorner
{
    public DropAdorner(UIElement target) : base(target) => IsHitTestVisible = false;

    public Rect Bounds { get; set; }
    public Place Place { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        var brush = Application.Current.TryFindResource("AccentTextFillColorPrimaryBrush") as Brush ?? Brushes.DodgerBlue;
        var pen = new Pen(brush, 2);
        switch (Place)
        {
            case Place.Before:
                dc.DrawLine(pen, Bounds.TopLeft, Bounds.TopRight);
                break;
            case Place.After:
                dc.DrawLine(pen, Bounds.BottomLeft, Bounds.BottomRight);
                break;
            default:
                var fill = brush.Clone();
                fill.Opacity = 0.15;
                dc.DrawRoundedRectangle(fill, pen, Bounds, 4, 4);
                break;
        }
    }
}
