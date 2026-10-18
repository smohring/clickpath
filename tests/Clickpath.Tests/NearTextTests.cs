using Clickpath.Engine;
using Clickpath.Models;

namespace Clickpath.Tests;

public class NearTextTests
{
    static Step Click(string near = "") => new() { Type = StepType.Click, Text = "Download Mirror", NearText = near };

    [Fact]
    public void There_is_no_restriction_by_default()
    {
        var step = new Step { Type = StepType.Click };

        Assert.Equal("", step.NearText);
        Assert.Equal("", step.ToQuery().Near);
    }

    [Fact]
    public void The_text_becomes_an_escaped_contains_pattern_in_the_query()
    {
        Assert.Equal("alphahost", Click("alphahost").ToQuery().Near);
        Assert.Equal(@"dd\.own\+load", Click("dd.own+load").ToQuery().Near);
        Assert.Equal("rapid gator", Click("  rapid   gator  ").ToQuery().Near);
    }

    [Fact]
    public void Variables_are_filled_in_like_in_the_main_text()
    {
        Assert.Equal("example", Click("{host}").ToQuery(s => s.Replace("{host}", "example")).Near);
    }

    [Fact]
    public void The_restriction_is_part_of_the_query_so_every_search_step_uses_it()
    {
        var plain = Click().ToQuery();
        var restricted = Click("alphahost").ToQuery();

        Assert.NotEqual(plain, restricted);
        Assert.Equal(plain with { Near = "alphahost" }, restricted);
    }

    [Fact]
    public void It_survives_saving_and_loading_and_old_plans_load_without_it()
    {
        var loaded = PlanFile.FromJson(PlanFile.ToJson([Click("alphahost")]));
        var old = PlanFile.FromJson("""[ { "Type": "Click", "Text": "x" } ]""");

        Assert.Equal("alphahost", loaded[0].NearText);
        Assert.Equal("", old[0].NearText);
    }

    [Fact]
    public void The_subtitle_names_the_restriction_only_when_there_is_one()
    {
        Assert.Contains("alphahost", Click("alphahost").Subtitle);
        Assert.DoesNotContain("alphahost", Click().Subtitle);
    }

    [Theory]
    [InlineData(StepType.Click, true)]
    [InlineData(StepType.IfFound, true)]
    [InlineData(StepType.ForEachLink, true)]
    [InlineData(StepType.Delay, false)]
    [InlineData(StepType.Navigate, false)]
    public void The_field_shows_for_the_steps_that_search_the_page(StepType type, bool expected)
    {
        Assert.Equal(expected, new Step { Type = type }.UsesMatch);
    }
}
