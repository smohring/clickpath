using Clickpath.Browser;

namespace Clickpath.Tests;

public class MousePathTests
{
    static readonly MousePath.Point Start = new(100, 100);
    static double Dist(MousePath.Point a, MousePath.Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    [Fact]
    public void A_curve_ends_exactly_on_the_target()
    {
        var target = new MousePath.Point(700, 420);

        Assert.Equal(target, MousePath.Curve(Start, target, new Random(1))[^1]);
    }

    [Fact]
    public void A_zero_length_move_is_just_the_target()
    {
        Assert.Equal([Start], MousePath.Curve(Start, Start, new Random(1)));
    }

    [Fact]
    public void The_pointer_never_jumps_far_between_two_samples()
    {
        var target = new MousePath.Point(800, 500);
        var d = Dist(Start, target);

        for (var seed = 0; seed < 50; seed++)
        {
            var path = MousePath.Curve(Start, target, new Random(seed));
            var previous = Start;
            foreach (var p in path)
            {
                Assert.True(Dist(previous, p) <= 2.5 * d / path.Count + 3, $"seed {seed}: jump of {Dist(previous, p):F1}px");
                previous = p;
            }
        }
    }

    [Fact]
    public void The_pointer_starts_slowly_and_is_fastest_in_the_middle()
    {
        var path = MousePath.Curve(Start, new MousePath.Point(900, 100), new Random(3));

        var first = Dist(Start, path[0]);
        var middle = Dist(path[path.Count / 2 - 1], path[path.Count / 2]);
        var last = Dist(path[^2], path[^1]);

        Assert.True(first < middle / 2, $"first {first:F1} vs middle {middle:F1}");
        Assert.True(last < middle / 2, $"last {last:F1} vs middle {middle:F1}");
    }

    [Fact]
    public void Long_moves_are_curved_not_straight_and_stay_near_the_line()
    {
        var target = new MousePath.Point(900, 100);
        var curved = 0;

        for (var seed = 0; seed < 50; seed++)
        {
            var deviation = MousePath.Curve(Start, target, new Random(seed)).Max(p => Math.Abs(p.Y - 100));
            if (deviation > 5) curved++;
            Assert.True(deviation < 0.25 * 800, $"seed {seed}: deviation {deviation:F0}px");
        }

        Assert.True(curved >= 35, $"only {curved} of 50 paths were curved");
    }

    [Fact]
    public void Two_moves_to_the_same_target_take_different_paths()
    {
        var target = new MousePath.Point(700, 420);

        Assert.NotEqual(MousePath.Curve(Start, target, new Random(1)), MousePath.Curve(Start, target, new Random(2)));
    }

    [Fact]
    public void A_journey_ends_on_the_target_and_sometimes_overshoots_it_first()
    {
        var target = new MousePath.Point(900, 100);
        var overshoots = 0;

        for (var seed = 0; seed < 100; seed++)
        {
            var path = MousePath.Journey(Start, target, 1600, 900, new Random(seed));
            Assert.Equal(target, path[^1]);
            if (path.Max(p => p.X) > target.X + 3) overshoots++;
        }

        Assert.InRange(overshoots, 10, 60);
    }

    [Fact]
    public void Short_journeys_never_overshoot()
    {
        var target = new MousePath.Point(160, 120);

        for (var seed = 0; seed < 100; seed++)
            Assert.True(MousePath.Journey(Start, target, 1600, 900, new Random(seed)).Max(p => p.X) <= target.X + 3);
    }

    [Fact]
    public void A_click_point_lies_in_the_inner_part_of_the_element_and_clusters_around_its_centre()
    {
        var rng = new Random(7);
        var points = Enumerable.Range(0, 500).Select(_ => MousePath.ClickPoint(new MousePath.Point(300, 200), 200, 40, rng)).ToList();

        Assert.All(points, p =>
        {
            Assert.InRange(p.X, 300 - 60.001, 300 + 60.001);
            Assert.InRange(p.Y, 200 - 12.001, 200 + 12.001);
        });
        Assert.InRange(points.Average(p => p.X), 295, 305);
        Assert.InRange(points.Average(p => p.Y), 198, 202);
        Assert.True(points.Select(p => p.X).Distinct().Count() > 100, "the click point should vary");
    }

    [Fact]
    public void The_gap_between_random_clicks_varies_around_the_average()
    {
        var rng = new Random(11);
        var gaps = Enumerable.Range(0, 1000).Select(_ => MousePath.ClickGap(TimeSpan.FromSeconds(4), rng).TotalSeconds).ToList();

        Assert.All(gaps, g => Assert.InRange(g, 2.0 - 0.001, 6.0 + 0.001));
        Assert.InRange(gaps.Average(), 3.8, 4.2);
        Assert.True(gaps.Distinct().Count() > 500, "the gaps should vary");
    }

    [Fact]
    public void A_click_point_in_an_element_without_size_is_its_centre()
    {
        Assert.Equal(new MousePath.Point(50, 60), MousePath.ClickPoint(new MousePath.Point(50, 60), 0, 0, new Random(1)));
    }

    [Fact]
    public void Every_point_of_a_journey_stays_inside_the_viewport()
    {
        for (var seed = 0; seed < 100; seed++)
            foreach (var p in MousePath.Journey(new MousePath.Point(1500, 850), new MousePath.Point(1590, 895), 1600, 900, new Random(seed)))
            {
                Assert.InRange(p.X, 0, 1600);
                Assert.InRange(p.Y, 0, 900);
            }
    }
}
