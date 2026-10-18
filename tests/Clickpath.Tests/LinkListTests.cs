using Clickpath.Engine;

namespace Clickpath.Tests;

public class LinkListTests
{
    [Fact]
    public void Every_line_with_a_web_address_is_one_link_in_order()
    {
        var (urls, ignored) = LinkList.Parse("https://a.example/1\nhttp://b.example/2\r\nhttps://c.example/3");

        Assert.Equal(["https://a.example/1", "http://b.example/2", "https://c.example/3"], urls);
        Assert.Empty(ignored);
    }

    [Fact]
    public void Blank_lines_and_comment_lines_are_skipped_without_complaint()
    {
        var (urls, ignored) = LinkList.Parse("# my list\n\n   \nhttps://a.example/1\n   # indented comment\n");

        Assert.Equal(["https://a.example/1"], urls);
        Assert.Empty(ignored);
    }

    [Fact]
    public void Surrounding_spaces_are_trimmed()
    {
        Assert.Equal(["https://a.example/1"], LinkList.Parse("   https://a.example/1\t ").Urls);
    }

    [Fact]
    public void A_bare_host_gets_https()
    {
        Assert.Equal(["https://example.com/page", "https://example.org"], LinkList.Parse("example.com/page\nexample.org").Urls);
    }

    [Fact]
    public void The_address_is_taken_out_of_a_line_with_a_title_in_front()
    {
        var (urls, ignored) = LinkList.Parse("Download one\thttps://a.example/1\nSecond https://a.example/2 (mirror)");

        Assert.Equal(["https://a.example/1", "https://a.example/2"], urls);
        Assert.Empty(ignored);
    }

    [Fact]
    public void Several_addresses_in_one_line_are_several_links()
    {
        Assert.Equal(["https://a.example/1", "https://a.example/2"], LinkList.Parse("https://a.example/1 https://a.example/2").Urls);
    }

    [Theory]
    [InlineData("just some words")]
    [InlineData("jd://something")]
    [InlineData("ftp://a.example/file")]
    [InlineData("C:\\Users\\me\\file.txt")]
    [InlineData("12345")]
    public void Lines_that_are_not_web_addresses_are_reported_and_not_used(string line)
    {
        var (urls, ignored) = LinkList.Parse($"https://a.example/1\n{line}\nhttps://a.example/2");

        Assert.Equal(["https://a.example/1", "https://a.example/2"], urls);
        Assert.Equal([line], ignored);
    }

    [Fact]
    public void Duplicates_are_kept_when_parsing_because_the_list_is_the_users_choice()
    {
        Assert.Equal(2, LinkList.Parse("https://a.example/1\nhttps://a.example/1").Urls.Count);
    }

    [Fact]
    public void An_empty_text_has_no_links()
    {
        Assert.Empty(LinkList.Parse("").Urls);
        Assert.Empty(LinkList.Parse("   \n  \n").Urls);
    }

    [Fact]
    public void Tidy_drops_blank_lines_and_repeated_addresses_but_keeps_comments_and_unreadable_lines()
    {
        var tidy = LinkList.Tidy("# first\n\n  https://a.example/1  \nhttps://a.example/2\n\nhttps://A.example/1\nnot a link\n# second\nhttps://a.example/2");

        Assert.Equal("# first\nhttps://a.example/1\nhttps://a.example/2\nnot a link\n# second", tidy);
    }

    [Fact]
    public void Tidy_leaves_a_clean_list_unchanged()
    {
        const string clean = "https://a.example/1\nhttps://a.example/2";

        Assert.Equal(clean, LinkList.Tidy(clean));
    }

    [Fact]
    public void Append_adds_only_addresses_that_are_not_in_the_list_yet_each_on_its_own_line()
    {
        var result = LinkList.Append("https://a.example/1\n", ["https://a.example/1", "https://a.example/2", "https://a.example/2", "https://a.example/3"]);

        Assert.Equal("https://a.example/1\nhttps://a.example/2\nhttps://a.example/3", result);
    }

    [Fact]
    public void Append_to_an_empty_list_starts_it()
    {
        Assert.Equal("https://a.example/1", LinkList.Append("", ["https://a.example/1"]));
    }

    [Theory]
    [InlineData("first\nsecond\nthird", 0, 0, 5)]
    [InlineData("first\nsecond\nthird", 3, 0, 5)]
    [InlineData("first\nsecond\nthird", 5, 0, 5)]       // at the end of a line: still that line
    [InlineData("first\nsecond\nthird", 6, 6, 6)]       // start of the second line
    [InlineData("first\nsecond\nthird", 9, 6, 6)]
    [InlineData("first\nsecond\nthird", 13, 13, 5)]
    [InlineData("first\nsecond\nthird", 18, 13, 5)]     // after the last character
    [InlineData("first\r\nsecond\r\nthird", 4, 0, 5)]
    [InlineData("first\r\nsecond\r\nthird", 7, 7, 6)]
    [InlineData("first\r\nsecond\r\nthird", 12, 7, 6)]
    [InlineData("first\r\nsecond\r\nthird", 16, 15, 5)]
    [InlineData("a\n\nb", 2, 2, 0)]                       // an empty line selects nothing
    [InlineData("  https://x.example/a  ", 5, 0, 23)]     // spaces are part of the line
    [InlineData("", 0, 0, 0)]
    public void A_double_click_selects_the_whole_line_without_its_line_break(string text, int index, int start, int length)
    {
        Assert.Equal((start, length), LinkList.LineAt(text, index));
    }

    [Fact]
    public void A_click_position_outside_the_text_is_clamped_to_it()
    {
        Assert.Equal((0, 3), LinkList.LineAt("abc", -4));
        Assert.Equal((0, 3), LinkList.LineAt("abc", 99));
    }

    [Fact]
    public void The_summary_counts_links_and_ignored_lines()
    {
        Assert.Equal((2, 1), LinkList.Count("https://a.example/1\nnope\nhttps://a.example/2\n# c"));
    }
}
