namespace Clickpath.Engine;

/// <summary>
/// The hand-written link list of a "repeat for each link in my list" step: one link per line; blank lines and lines starting
/// with # are skipped; a line with a title in front ("Name https://...") yields the address in it.
/// </summary>
public static class LinkList
{
    static readonly char[] Separators = [' ', '\t', ' '];

    /// <summary>The web addresses in order (duplicates kept) and the lines that contain none.</summary>
    public static (IReadOnlyList<string> Urls, IReadOnlyList<string> Ignored) Parse(string text)
    {
        var urls = new List<string>();
        var ignored = new List<string>();
        foreach (var line in Lines(text))
        {
            if (IsSkipped(line)) continue;
            var found = AddressesIn(line);
            if (found.Count > 0) urls.AddRange(found);
            else ignored.Add(line);
        }
        return (urls, ignored);
    }

    /// <summary>Number of links and of ignored lines, for the summary under the editor.</summary>
    public static (int Links, int Ignored) Count(string text)
    {
        var (urls, ignored) = Parse(text);
        return (urls.Count, ignored.Count);
    }

    /// <summary>Trims lines, drops blank lines and lines whose addresses are all in the list already; comments and unreadable lines stay.</summary>
    public static string Tidy(string text)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<string>();
        foreach (var line in Lines(text))
        {
            if (line.Length == 0) continue;
            if (line.StartsWith('#')) { kept.Add(line); continue; }
            var found = AddressesIn(line);
            if (found.Count > 0 && found.All(u => seen.Contains(u))) continue;
            foreach (var url in found) seen.Add(url);
            kept.Add(line);
        }
        return string.Join('\n', kept);
    }

    /// <summary>Adds the addresses that are not in the list yet, each on its own line.</summary>
    public static string Append(string text, IEnumerable<string> urls)
    {
        var known = new HashSet<string>(Parse(text).Urls, StringComparer.OrdinalIgnoreCase);
        var lines = Lines(text).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        foreach (var url in urls.SelectMany(u => AddressesIn(u.Trim())))
            if (known.Add(url)) lines.Add(url);
        return string.Join('\n', lines);
    }

    /// <summary>Start and length of the line that contains <paramref name="index"/> (a caret position), without its line break; for selecting a whole line.</summary>
    public static (int Start, int Length) LineAt(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        var start = index == 0 ? 0 : text.LastIndexOfAny(['\r', '\n'], index - 1) + 1;
        var end = text.IndexOfAny(['\r', '\n'], index);
        return (start, (end < 0 ? text.Length : end) - start);
    }

    static IEnumerable<string> Lines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.Trim());

    static bool IsSkipped(string line) => line.Length == 0 || line.StartsWith('#');

    /// <summary>The http(s)/file addresses in the line; a single bare host like example.com/page counts too (https is added).</summary>
    static List<string> AddressesIn(string line)
    {
        var tokens = line.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        var explicitUrls = tokens.Where(IsWebAddress).ToList();
        if (explicitUrls.Count > 0) return explicitUrls;
        return tokens.Length == 1 && LooksLikeHost(tokens[0]) && IsWebAddress("https://" + tokens[0]) ? ["https://" + tokens[0]] : [];
    }

    static bool IsWebAddress(string token) =>
        Uri.TryCreate(token, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "file" && token.Contains("://");

    static bool LooksLikeHost(string token) => token.Contains('.') && !token.Contains("://") && !token.Contains('\\') && !token.EndsWith('.')
        && !token.All(c => char.IsDigit(c) || c == '.');
}
