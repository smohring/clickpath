namespace Clickpath.Browser;

/// <summary>What a page looks like at one moment: its address, the set of words it shows and the length of its generated source (the live DOM as HTML).</summary>
public sealed record PageSnapshot(string Url, IReadOnlySet<string> Words, int SourceLength = 0);

/// <summary>Tells a page whose content was swapped by JavaScript from one that merely changed a little.</summary>
public static class PageChange
{
    /// <summary>The default number of words that appeared or disappeared since the start for the page to count as changed (a clock or dropdown changes far fewer).</summary>
    public const int DefaultChangedWords = 20;

    /// <summary>A page whose words are mostly different counts as replaced however few words it has (a small "please wait" page can never reach the word limit).</summary>
    const double ReplacedShare = 0.6;

    /// <summary>
    /// A different address, at least <paramref name="changedWords"/> words that appeared or disappeared, most of the words replaced, or - if
    /// <paramref name="sourceChars"/> is above 0 - that many characters of difference in the length of the generated source. Any one of them is enough.
    /// </summary>
    public static bool IsNewPage(PageSnapshot before, PageSnapshot after, int changedWords = DefaultChangedWords, int sourceChars = 0)
    {
        if (BrowserTab.IsNewPage(before.Url, after.Url)) return true;
        var changed = ChangedWordCount(before, after);
        if (changed >= changedWords || (sourceChars > 0 && SourceDelta(before, after) >= sourceChars)) return true;
        var distinct = before.Words.Union(after.Words).Count();
        return distinct > 0 && changed >= ReplacedShare * distinct;
    }

    /// <summary>How many distinct words only one of the two snapshots has: the new ones plus the ones that are gone.</summary>
    public static int ChangedWordCount(PageSnapshot before, PageSnapshot after) =>
        after.Words.Count(w => !before.Words.Contains(w)) + before.Words.Count(w => !after.Words.Contains(w));

    /// <summary>How many characters the generated source grew or shrank by since the start.</summary>
    public static int SourceDelta(PageSnapshot before, PageSnapshot after) => Math.Abs(after.SourceLength - before.SourceLength);
}
