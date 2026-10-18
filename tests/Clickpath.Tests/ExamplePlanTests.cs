using System.IO;
using Clickpath.Engine;
using Clickpath.Models;

namespace Clickpath.Tests;

public class ExamplePlanTests
{
    static string ExamplesFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Clickpath.slnx"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("repository root not found"), "examples");
    }

    public static IEnumerable<object[]> Plans() =>
        Directory.Exists(ExamplesFolder()) ? Directory.GetFiles(ExamplesFolder(), "*.json").Select(f => new object[] { Path.GetFileName(f) }) : [];

    [Fact]
    public void At_least_two_example_plans_ship_with_the_project()
    {
        Assert.True(Directory.Exists(ExamplesFolder()), "examples folder is missing");
        Assert.True(Directory.GetFiles(ExamplesFolder(), "*.json").Length >= 2);
    }

    [Theory]
    [MemberData(nameof(Plans))]
    public void Example_plans_load_and_start_by_opening_a_page(string file)
    {
        var steps = PlanFile.Load(Path.Combine(ExamplesFolder(), file));
        Assert.NotEmpty(steps);
        Assert.Equal(StepType.Navigate, steps[0].Type);
        Assert.StartsWith("https://", steps[0].Url);
    }
}
