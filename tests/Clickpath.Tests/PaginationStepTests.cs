using Clickpath.Engine;
using Clickpath.Models;

namespace Clickpath.Tests;

public class PaginationStepTests
{
    static Step Loop() => new() { Type = StepType.ForEachLink };

    [Fact]
    public void Pagination_and_the_range_are_off_by_default()
    {
        var step = Loop();

        Assert.False(step.Paginate);
        Assert.Equal("", step.NextPageText);
        Assert.Equal(20, step.MaxPages);
        Assert.Equal("", step.StartText);
        Assert.Equal("", step.EndText);
        Assert.Equal(TextMatch.Contains, step.StartMode);
        Assert.Equal(TextMatch.Contains, step.EndMode);
    }

    [Theory]
    [InlineData(StepType.ForEachLink, true)]
    [InlineData(StepType.Click, false)]
    [InlineData(StepType.Extract, false)]
    public void Only_the_loop_offers_range_and_pagination(StepType type, bool expected)
    {
        Assert.Equal(expected, new Step { Type = type }.UsesForEachOptions);
    }

    [Fact]
    public void The_pagination_fields_show_only_while_pagination_is_on_and_tell_so()
    {
        var step = Loop();
        var changed = new List<string?>();
        step.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.False(step.UsesPaginationOptions);
        step.Paginate = true;

        Assert.True(step.UsesPaginationOptions);
        Assert.Contains(nameof(Step.UsesPaginationOptions), changed);
    }

    [Fact]
    public void At_least_one_page_is_allowed()
    {
        Assert.Equal(1, new Step { MaxPages = 0 }.MaxPages);
    }

    [Fact]
    public void The_options_survive_saving_and_loading_and_old_plans_load_with_them_off()
    {
        var saved = PlanFile.ToJson([new Step
        {
            Type = StepType.ForEachLink, Paginate = true, NextPageText = "Weiter", MaxPages = 5,
            StartText = "Item 2", StartMode = TextMatch.StartsWith, EndText = "Item 9", EndMode = TextMatch.Exactly,
        }]);
        var loaded = PlanFile.FromJson(saved)[0];
        var old = PlanFile.FromJson("""[ { "Type": "ForEachLink", "Text": "Item " } ]""")[0];

        Assert.True(loaded.Paginate);
        Assert.Equal("Weiter", loaded.NextPageText);
        Assert.Equal(5, loaded.MaxPages);
        Assert.Equal(("Item 2", TextMatch.StartsWith), (loaded.StartText, loaded.StartMode));
        Assert.Equal(("Item 9", TextMatch.Exactly), (loaded.EndText, loaded.EndMode));
        Assert.False(old.Paginate);
        Assert.Equal("", old.StartText);
    }

    [Fact]
    public void The_subtitle_mentions_pagination_and_a_limited_range()
    {
        var plain = new Step { Type = StepType.ForEachLink, Text = "x" };

        Assert.True(new Step { Type = StepType.ForEachLink, Text = "x", Paginate = true }.Subtitle.Length > plain.Subtitle.Length);
        Assert.True(new Step { Type = StepType.ForEachLink, Text = "x", StartText = "a" }.Subtitle.Length > plain.Subtitle.Length);
        Assert.True(new Step { Type = StepType.ForEachLink, Text = "x", EndText = "z" }.Subtitle.Length > plain.Subtitle.Length);
    }

    [Theory]
    [InlineData("Next", TextMatch.Contains, "Next")]
    [InlineData("Next", TextMatch.StartsWith, "^Next")]
    [InlineData("Next", TextMatch.EndsWith, "Next$")]
    [InlineData("Next", TextMatch.Exactly, "^Next$")]
    [InlineData("a.b", TextMatch.Contains, @"a\.b")]
    [InlineData("a*b", TextMatch.Wildcard, "^a.*b$")]
    [InlineData("^x[0-9]$", TextMatch.Regex, "^x[0-9]$")]
    public void Delimiter_patterns_are_built_like_the_step_text_pattern(string text, TextMatch mode, string expected)
    {
        Assert.Equal(expected, Step.PatternFor(text, mode));
    }
}
