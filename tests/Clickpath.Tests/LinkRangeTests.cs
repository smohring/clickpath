using Clickpath.Browser;
using Clickpath.Engine;
using Clickpath.Models;

namespace Clickpath.Tests;

public class LinkRangeTests
{
    static LinkMatch L(string text, string href = "") => new(text, href.Length > 0 ? href : $"https://a.example/{text.Replace(' ', '-')}");

    static readonly LinkMatch[] Links = new[] { "Item 1", "Item 2", "Item 3", "Item 4", "Item 5" }.Select(t => L(t)).ToArray();

    static Step ForEach(string start = "", TextMatch startMode = TextMatch.Contains, string end = "", TextMatch endMode = TextMatch.Contains, bool matchCase = false) =>
        new() { Type = StepType.ForEachLink, StartText = start, StartMode = startMode, EndText = end, EndMode = endMode, MatchCase = matchCase };

    static List<string> Run(Step step, IEnumerable<LinkMatch> links)
    {
        var range = LinkRange.From(step);
        return links.Where(range.Admit).Select(l => l.Text).ToList();
    }

    [Fact]
    public void Without_delimiters_every_link_is_taken_and_the_range_never_ends()
    {
        var range = LinkRange.From(ForEach());

        Assert.Equal(5, Links.Count(range.Admit));
        Assert.False(range.Finished);
    }

    [Fact]
    public void The_start_link_is_the_first_one_taken()
    {
        Assert.Equal(["Item 3", "Item 4", "Item 5"], Run(ForEach(start: "Item 3"), Links));
    }

    [Fact]
    public void The_end_link_is_the_last_one_taken_and_the_range_is_then_finished()
    {
        var step = ForEach(end: "Item 3");
        var range = LinkRange.From(step);

        Assert.Equal(["Item 1", "Item 2", "Item 3"], Links.Where(range.Admit).Select(l => l.Text).ToList());
        Assert.True(range.Finished);
    }

    [Fact]
    public void Start_and_end_together_take_the_links_in_between()
    {
        Assert.Equal(["Item 2", "Item 3", "Item 4"], Run(ForEach(start: "Item 2", end: "Item 4"), Links));
    }

    [Fact]
    public void One_link_can_be_start_and_end()
    {
        Assert.Equal(["Item 3"], Run(ForEach(start: "Item 3", end: "Item 3"), Links));
    }

    [Fact]
    public void A_start_that_never_appears_takes_nothing()
    {
        Assert.Empty(Run(ForEach(start: "nothing like this"), Links));
    }

    [Fact]
    public void An_end_before_the_start_is_not_noticed_until_the_start_was_seen()
    {
        Assert.Equal(["Item 4", "Item 5"], Run(ForEach(start: "Item 4", end: "Item 1"), Links));
    }

    [Fact]
    public void A_delimiter_also_matches_the_address()
    {
        var links = new[] { L("Download", "https://a.example/files/one"), L("Download", "https://a.example/files/two"), L("Download", "https://a.example/files/three") };

        Assert.Equal(["https://a.example/files/two", "https://a.example/files/three"], links.Where(LinkRange.From(ForEach(start: "files/two")).Admit).Select(l => l.Href).ToList());
    }

    [Theory]
    [InlineData(TextMatch.Exactly, "Item 2", "Item 2")]
    [InlineData(TextMatch.StartsWith, "Item", "Item 1")]
    [InlineData(TextMatch.EndsWith, "4", "Item 4")]
    [InlineData(TextMatch.Wildcard, "Item * 3", "")]
    [InlineData(TextMatch.Wildcard, "Item *3", "Item 3")]
    [InlineData(TextMatch.Regex, "^Item [45]$", "Item 4")]
    public void The_match_mode_applies_to_the_start_delimiter(TextMatch mode, string text, string expectedFirst)
    {
        var taken = Run(ForEach(start: text, startMode: mode), Links);

        Assert.Equal(expectedFirst, taken.FirstOrDefault() ?? "");
    }

    [Fact]
    public void Matching_ignores_case_unless_match_case_is_set()
    {
        Assert.Equal(3, Run(ForEach(start: "ITEM 3"), Links).Count);
        Assert.Empty(Run(ForEach(start: "ITEM 3", matchCase: true), Links));
    }

    [Fact]
    public void An_invalid_regular_expression_is_reported_as_a_step_failure()
    {
        Assert.Throws<StepFailedException>(() => LinkRange.From(ForEach(start: "([", startMode: TextMatch.Regex)));
    }

    [Fact]
    public void The_same_range_object_keeps_its_state_across_pages()
    {
        var range = LinkRange.From(ForEach(start: "Item 4", end: "Item 6"));
        var page1 = Links.Take(3).Where(range.Admit).ToList();
        var page2 = new[] { L("Item 4"), L("Item 5"), L("Item 6"), L("Item 7") }.Where(range.Admit).Select(l => l.Text).ToList();

        Assert.Empty(page1);
        Assert.Equal(["Item 4", "Item 5", "Item 6"], page2);
        Assert.True(range.Finished);
    }
}
