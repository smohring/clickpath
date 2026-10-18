using Clickpath.Engine;
using Clickpath.Models;

namespace Clickpath.Tests;

public class LinkListStepTests
{
    static Step List(string links = "") => new() { Type = StepType.ForEachListedLink, Links = links };

    [Fact]
    public void The_list_step_is_a_loop_that_can_hold_steps_and_continues_on_error()
    {
        var step = List();

        Assert.True(step.CanHaveChildren);
        Assert.True(step.UsesLinkList);
        Assert.True(step.UsesContinueOnError);
    }

    [Theory]
    [InlineData(StepType.ForEachLink)]
    [InlineData(StepType.Click)]
    public void Other_steps_do_not_show_the_list_editor(StepType type)
    {
        Assert.False(new Step { Type = type }.UsesLinkList);
    }

    [Fact]
    public void The_list_survives_saving_and_loading_with_its_line_breaks()
    {
        var saved = PlanFile.ToJson([List("https://a.example/1\r\nhttps://a.example/2")]);

        Assert.Equal("https://a.example/1\r\nhttps://a.example/2", PlanFile.FromJson(saved)[0].Links);
    }

    [Fact]
    public void Old_plans_load_with_an_empty_list()
    {
        Assert.Equal("", PlanFile.FromJson("""[ { "Type": "ForEachLink", "Text": "x" } ]""")[0].Links);
    }

    [Fact]
    public void The_summary_follows_the_text_and_the_subtitle_shows_the_count()
    {
        var step = List();
        var changed = new List<string?>();
        step.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        step.Links = "https://a.example/1\nnope\nhttps://a.example/2";

        Assert.Contains(nameof(Step.LinkListSummary), changed);
        Assert.Contains("2", step.Subtitle);
        Assert.Contains("2", step.LinkListSummary);
        Assert.Contains("1", step.LinkListSummary);
    }

    [Fact]
    public void Without_ignored_lines_the_summary_does_not_mention_them()
    {
        var summary = List("https://a.example/1").LinkListSummary;
        var withIgnored = List("https://a.example/1\nnope").LinkListSummary;

        Assert.True(withIgnored.Length > summary.Length);
    }

    [Fact]
    public void It_is_offered_in_the_add_step_menu_right_after_the_normal_loop()
    {
        var types = Enum.GetValues<StepType>().ToList();

        Assert.Equal(types.IndexOf(StepType.ForEachLink) + 1, types.IndexOf(StepType.ForEachListedLink));
    }
}
