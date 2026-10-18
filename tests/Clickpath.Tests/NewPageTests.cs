using Clickpath.Browser;
using Clickpath.Engine;
using Clickpath.Models;

namespace Clickpath.Tests;

public class NewPageTests
{
    [Theory]
    [InlineData("https://a.example/list", "https://a.example/item", true)]
    [InlineData("https://a.example/list", "https://b.example/list", true)]
    [InlineData("https://a.example/list?page=1", "https://a.example/list?page=2", true)]
    [InlineData("https://a.example/list", "https://a.example/list#section", false)]
    [InlineData("https://a.example/list#a", "https://a.example/list#b", false)]
    [InlineData("about:blank", "https://a.example/", true)]
    [InlineData("", "https://a.example/", true)]
    public void A_navigation_is_a_new_page_unless_only_the_fragment_changes(string from, string to, bool expected)
    {
        Assert.Equal(expected, BrowserTab.IsNewPage(from, to));
    }

    [Theory]
    [InlineData(StepType.MoveMouse)]
    [InlineData(StepType.Delay)]
    public void Waiting_steps_offer_to_end_on_a_new_page(StepType type)
    {
        Assert.True(new Step { Type = type }.UsesEndOnNewPage);
    }

    [Theory]
    [InlineData(StepType.Click)]
    [InlineData(StepType.Navigate)]
    [InlineData(StepType.WaitFor)]
    public void Other_steps_do_not(StepType type)
    {
        Assert.False(new Step { Type = type }.UsesEndOnNewPage);
    }

    [Fact]
    public void The_option_is_off_by_default_and_survives_saving_and_loading()
    {
        Assert.False(new Step { Type = StepType.Delay }.EndOnNewPage);

        var loaded = PlanFile.FromJson(PlanFile.ToJson([new Step { Type = StepType.MoveMouse, EndOnNewPage = true }, new Step { Type = StepType.Delay }]));

        Assert.True(loaded[0].EndOnNewPage);
        Assert.False(loaded[1].EndOnNewPage);
    }

    [Fact]
    public void The_subtitle_says_when_the_step_ends_early()
    {
        Assert.Contains("→", new Step { Type = StepType.Delay, EndOnNewPage = true }.Subtitle);
        Assert.DoesNotContain("→", new Step { Type = StepType.Delay }.Subtitle);
    }
}
