namespace Clickpath.Browser;

/// <summary>Pointer paths that look hand-made: curved, slow at both ends, with fine tremor and an occasional overshoot.</summary>
public static class MousePath
{
    public readonly record struct Point(double X, double Y);

    /// <summary>A bent path from <paramref name="from"/> to <paramref name="to"/>; the last point is exactly the target.</summary>
    public static IReadOnlyList<Point> Curve(Point from, Point to, Random rng)
    {
        double dx = to.X - from.X, dy = to.Y - from.Y;
        var d = Math.Sqrt(dx * dx + dy * dy);
        if (d < 1) return [to];

        var n = Math.Clamp((int)(d / 7) + rng.Next(-3, 4), 8, 90);
        double nx = -dy / d, ny = dx / d;                           // unit normal: the path bends sideways along it
        double Bend() => d * (rng.NextDouble() * 0.3 - 0.15);
        double b1 = Bend(), b2 = Bend();
        double c1x = from.X + dx * 0.3 + nx * b1, c1y = from.Y + dy * 0.3 + ny * b1;
        double c2x = from.X + dx * 0.7 + nx * b2, c2y = from.Y + dy * 0.7 + ny * b2;

        var points = new List<Point>(n);
        for (var i = 1; i <= n; i++)
        {
            var u = i / (double)n;
            var s = u * u * u * (10 - 15 * u + 6 * u * u);          // minimum-jerk easing: slow, fast, slow
            double v = 1 - s, a = v * v * v, b = 3 * v * v * s, c = 3 * v * s * s, e = s * s * s;
            var tremor = Math.Sin(Math.PI * u) * 0.7;               // none at the ends, so the target is hit exactly
            points.Add(new Point(
                a * from.X + b * c1x + c * c2x + e * to.X + (rng.NextDouble() * 2 - 1) * tremor,
                a * from.Y + b * c1y + c * c2y + e * to.Y + (rng.NextDouble() * 2 - 1) * tremor));
        }
        points[^1] = to;
        return points;
    }

    /// <summary>
    /// A move that sometimes shoots 8-25 px past a far target and then corrects back to it, like a hand does.
    /// All points stay inside the viewport.
    /// </summary>
    public static IReadOnlyList<Point> Journey(Point from, Point to, double width, double height, Random rng)
    {
        double dx = to.X - from.X, dy = to.Y - from.Y;
        var d = Math.Sqrt(dx * dx + dy * dy);
        if (d <= 250 || rng.NextDouble() >= 0.35) return Clamp(Curve(from, to, rng), width, height);

        var over = 8 + rng.NextDouble() * 17;
        var aim = new Point(to.X + dx / d * over, to.Y + dy / d * over);
        aim = new Point(Math.Clamp(aim.X, 0, width), Math.Clamp(aim.Y, 0, height));
        return Clamp([.. Curve(from, aim, rng), .. Curve(aim, to, rng)], width, height);
    }

    /// <summary>A press point inside the middle 60 % of an element, clustered around its centre (a person rarely hits the exact middle).</summary>
    public static Point ClickPoint(Point centre, double width, double height, Random rng) => new(
        centre.X + (rng.NextDouble() + rng.NextDouble() - 1) * width * 0.3,
        centre.Y + (rng.NextDouble() + rng.NextDouble() - 1) * height * 0.3);

    /// <summary>The wait until the next random click: between half and one and a half times the average.</summary>
    public static TimeSpan ClickGap(TimeSpan average, Random rng) => average * (0.5 + rng.NextDouble());

    static IReadOnlyList<Point> Clamp(IEnumerable<Point> points, double width, double height) =>
        points.Select(p => new Point(Math.Clamp(p.X, 0, width), Math.Clamp(p.Y, 0, height))).ToList();
}
