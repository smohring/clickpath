using System.IO;
using Clickpath.Browser;

namespace Clickpath.Tests;

public sealed class HistoryStoreTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"lf-history-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_path);

    HistoryStore Store(int capacity = 40) => new(_path) { Capacity = capacity };

    [Fact]
    public void The_newest_page_comes_first()
    {
        var store = Store();
        store.Record("https://a.example/", "A");
        store.Record("https://b.example/", "B");

        Assert.Equal(["https://b.example/", "https://a.example/"], store.Entries.Select(e => e.Url));
    }

    [Fact]
    public void Visiting_a_page_again_moves_it_to_the_top_instead_of_adding_it_twice()
    {
        var store = Store();
        store.Record("https://a.example/", "A");
        store.Record("https://b.example/", "B");
        store.Record("https://a.example/", "A again");

        Assert.Equal(2, store.Entries.Count);
        Assert.Equal("A again", store.Entries[0].Title);
    }

    [Fact]
    public void The_fragment_does_not_make_a_page_a_different_page()
    {
        var store = Store();
        store.Record("https://a.example/page#top", "A");
        store.Record("https://a.example/page#bottom", "A");

        Assert.Single(store.Entries);
    }

    [Fact]
    public void Only_the_newest_pages_up_to_the_capacity_are_kept()
    {
        var store = Store(capacity: 5);
        for (var i = 1; i <= 8; i++) store.Record($"https://a.example/{i}", $"P{i}");

        Assert.Equal(["P8", "P7", "P6", "P5", "P4"], store.Entries.Select(e => e.Title));
    }

    [Fact]
    public void The_capacity_stays_within_sensible_limits()
    {
        var store = Store();

        store.Capacity = 0;
        Assert.Equal(5, store.Capacity);
        store.Capacity = 100000;
        Assert.Equal(500, store.Capacity);
    }

    [Fact]
    public void Lowering_the_capacity_drops_the_oldest_pages()
    {
        var store = Store();
        for (var i = 1; i <= 8; i++) store.Record($"https://a.example/{i}", $"P{i}");

        store.Capacity = 5;

        Assert.Equal(["P8", "P7", "P6", "P5", "P4"], store.Entries.Select(e => e.Title));
    }

    [Theory]
    [InlineData("about:blank")]
    [InlineData("data:text/html,hi")]
    [InlineData("jd://something")]
    [InlineData("")]
    public void Pages_that_are_not_web_pages_are_not_recorded(string url)
    {
        var store = Store();

        Assert.False(store.Record(url, "x"));
        Assert.Empty(store.Entries);
    }

    [Fact]
    public void A_title_that_arrives_late_is_filled_in()
    {
        var store = Store();
        store.Record("https://a.example/", "");

        store.UpdateTitle("https://a.example/", "A");

        Assert.Equal("A", store.Entries[0].Title);
    }

    [Fact]
    public void A_title_for_an_unknown_page_is_ignored()
    {
        var store = Store();

        store.UpdateTitle("https://unknown.example/", "X");

        Assert.Empty(store.Entries);
    }

    [Fact]
    public void Search_matches_title_and_address_ignoring_case()
    {
        var store = Store();
        store.Record("https://a.example/files", "Download center");
        store.Record("https://b.example/news", "Daily News");

        Assert.Equal(["Download center"], store.Search("DOWNLOAD").Select(e => e.Title));
        Assert.Equal(["Daily News"], store.Search("b.example").Select(e => e.Title));
        Assert.Equal(2, store.Search("").Count());
    }

    [Fact]
    public void Entries_and_settings_survive_a_restart()
    {
        var store = Store(capacity: 25);
        store.IncludeRuns = true;
        store.Record("https://a.example/", "A");

        var again = new HistoryStore(_path);

        Assert.Equal(25, again.Capacity);
        Assert.True(again.IncludeRuns);
        Assert.Equal("A", again.Entries.Single().Title);
    }

    [Fact]
    public void A_damaged_file_gives_an_empty_history_with_default_settings()
    {
        File.WriteAllText(_path, "{ this is not json");

        var store = new HistoryStore(_path);

        Assert.Empty(store.Entries);
        Assert.Equal(HistoryStore.DefaultCapacity, store.Capacity);
        Assert.False(store.IncludeRuns);
    }

    [Fact]
    public void Clearing_removes_all_pages_but_keeps_the_settings()
    {
        var store = Store(capacity: 25);
        store.Record("https://a.example/", "A");

        store.Clear();

        Assert.Empty(new HistoryStore(_path).Entries);
        Assert.Equal(25, new HistoryStore(_path).Capacity);
    }
}
