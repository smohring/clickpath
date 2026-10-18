using Clickpath.Browser;

namespace Clickpath.Tests;

public class PageChangeTests
{
    static PageSnapshot Page(string url, string words) => new(url, words.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet());

    const string Article = "welcome download file mirror premium account login register help contact imprint privacy terms news latest popular";

    [Fact]
    public void The_same_page_is_not_a_new_page()
    {
        Assert.False(PageChange.IsNewPage(Page("https://a.example/x", Article), Page("https://a.example/x", Article)));
    }

    [Fact]
    public void A_ticking_counter_or_a_dropdown_is_not_a_new_page()
    {
        var after = Article + " wait nineteen seconds";

        Assert.False(PageChange.IsNewPage(Page("https://a.example/x", Article), Page("https://a.example/x", after)));
        Assert.False(PageChange.IsNewPage(Page("https://a.example/x", Article), Page("https://a.example/x", Article.Replace("latest", "newest"))));
    }

    [Fact]
    public void Replacing_the_content_under_the_same_address_is_a_new_page()
    {
        var replaced = Page("https://a.example/x", "thank you your download will start now click here if it does not link ready");

        Assert.True(PageChange.IsNewPage(Page("https://a.example/x", Article), replaced));
    }

    [Fact]
    public void Replacing_half_of_the_content_is_a_new_page()
    {
        var words = Article.Split(' ');
        var half = string.Join(' ', words.Take(words.Length / 2).Concat(words.Skip(words.Length / 2).Select(w => "other" + w)));

        Assert.True(PageChange.IsNewPage(Page("https://a.example/x", Article), Page("https://a.example/x", half)));
    }

    [Fact]
    public void A_different_address_is_a_new_page_even_with_the_same_words()
    {
        Assert.True(PageChange.IsNewPage(Page("https://a.example/x", Article), Page("https://a.example/y", Article)));
        Assert.False(PageChange.IsNewPage(Page("https://a.example/x", Article), Page("https://a.example/x#top", Article)));
    }

    [Fact]
    public void Content_appearing_on_an_empty_page_is_a_new_page()
    {
        Assert.True(PageChange.IsNewPage(Page("https://a.example/x", ""), Page("https://a.example/x", Article)));
    }

    [Fact]
    public void Two_empty_pages_are_the_same()
    {
        Assert.False(PageChange.IsNewPage(Page("https://a.example/x", ""), Page("https://a.example/x", "")));
    }

    [Fact]
    public void The_changed_words_are_the_new_ones_plus_the_ones_that_are_gone()
    {
        var before = Page("https://a.example/x", "one two three four five six seven eight nine ten");
        var after = Page("https://a.example/x", "one two three four five six seven eight eleven twelve thirteen");

        Assert.Equal(0, PageChange.ChangedWordCount(before, before));
        Assert.Equal(5, PageChange.ChangedWordCount(before, after));   // nine, ten gone; eleven, twelve, thirteen new
    }

    [Fact]
    public void A_small_swap_inside_a_big_page_counts_by_the_number_of_words_not_by_a_share()
    {
        var chrome = string.Join(' ', Enumerable.Range(0, 800).Select(n => "menu" + n));
        var swapped = string.Join(' ', Enumerable.Range(0, 30).Select(n => "container" + n));
        var before = Page("https://a.example/x", chrome + " captcha human check");
        var after = Page("https://a.example/x", chrome + " " + swapped);

        Assert.Equal(33, PageChange.ChangedWordCount(before, after));   // 30 new + 3 gone, of 800+ words: a share would be 4 %
        Assert.True(PageChange.IsNewPage(before, after));               // the default is 20
        Assert.False(PageChange.IsNewPage(before, after, 50));
    }

    [Fact]
    public void A_handful_of_changed_words_is_not_a_new_page_unless_the_limit_is_low()
    {
        var chrome = string.Join(' ', Enumerable.Range(0, 100).Select(n => "menu" + n));
        var before = Page("https://a.example/x", chrome + " captcha human check");
        var after = Page("https://a.example/x", chrome + " download links container mirror premium click load");

        Assert.False(PageChange.IsNewPage(before, after));
        Assert.True(PageChange.IsNewPage(before, after, 5));
    }

    [Fact]
    public void The_source_length_ends_it_only_when_a_limit_is_set()
    {
        var words = Page("https://a.example/x", Article).Words;
        var before = new PageSnapshot("https://a.example/x", words, 10_000);
        var after = new PageSnapshot("https://a.example/x", words, 12_500);

        Assert.Equal(2_500, PageChange.SourceDelta(before, after));
        Assert.False(PageChange.IsNewPage(before, after));                  // off by default
        Assert.False(PageChange.IsNewPage(before, after, 20, 3_000));
        Assert.True(PageChange.IsNewPage(before, after, 20, 2_000));
        Assert.True(PageChange.IsNewPage(after, before, 20, 2_000));        // shrinking counts as well
    }
}
