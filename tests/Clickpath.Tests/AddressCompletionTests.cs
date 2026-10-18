using Clickpath.Browser;

namespace Clickpath.Tests;

public class AddressCompletionTests
{
    static readonly string[] Visited =
    [
        "https://www.youtube.com/watch?v=abc",
        "https://docs.example.org/guide/intro/",
        "http://router.local/",
    ];

    [Fact]
    public void Typing_the_start_of_a_host_completes_the_host_only()
    {
        Assert.Equal("youtube.com", AddressCompletion.Complete("yo", Visited));
        Assert.Equal("docs.example.org", AddressCompletion.Complete("doc", Visited));
        Assert.Equal("router.local", AddressCompletion.Complete("rou", Visited));
    }

    [Fact]
    public void Typing_a_path_completes_the_whole_address_without_the_scheme()
    {
        Assert.Equal("docs.example.org/guide/intro/", AddressCompletion.Complete("docs.example.org/gui", Visited));
    }

    [Fact]
    public void Typing_www_or_the_scheme_keeps_them_in_the_completion()
    {
        Assert.Equal("www.youtube.com", AddressCompletion.Complete("www.you", Visited));
        Assert.Equal("https://docs.example.org/guide/intro/", AddressCompletion.Complete("https://doc", Visited));
    }

    [Fact]
    public void The_typed_letters_keep_their_case_and_a_complete_host_has_nothing_to_add()
    {
        Assert.Equal("YouTube.com", AddressCompletion.Complete("YouTu", Visited));
        Assert.Null(AddressCompletion.Complete("youtube.com", Visited));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("zzz")]
    [InlineData("you tube")]
    [InlineData("about:bl")]
    public void Nothing_is_completed_when_nothing_matches_or_the_text_is_not_an_address(string typed)
    {
        Assert.Null(AddressCompletion.Complete(typed, Visited));
    }

    [Fact]
    public void Only_web_addresses_of_the_history_are_used()
    {
        Assert.Null(AddressCompletion.Complete("loc", ["file:///C:/local/page.html", "about:blank"]));
    }
}
