namespace Clickpath.Browser;

/// <summary>Inline completion for the address field: finishes what the user typed from the pages visited before, the way a browser does.</summary>
public static class AddressCompletion
{
    /// <summary>
    /// The typed text plus the missing rest of the first matching address, or null if nothing matches. Without a "/" in the typed text
    /// only the host is completed (typing "yo" gives "youtube.com"), with one the whole address; "www." and the scheme are left out unless typed.
    /// </summary>
    public static string? Complete(string typed, IEnumerable<string> urls)
    {
        if (string.IsNullOrWhiteSpace(typed) || typed != typed.TrimStart() || typed.Contains(' ')) return null;
        var withScheme = typed.Contains("://") || "https://".StartsWith(typed, StringComparison.OrdinalIgnoreCase) || "http://".StartsWith(typed, StringComparison.OrdinalIgnoreCase);
        foreach (var url in urls)
        {
            foreach (var form in Forms(url, withScheme, typed))
            {
                var candidate = typed.Contains('/') || typed.Contains("://") ? form : HostOf(form);
                if (candidate.Length > typed.Length && candidate.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                    return typed + candidate[typed.Length..];
            }
        }
        return null;
    }

    /// <summary>The ways an address can be written for matching: with the scheme if the user typed one, else without it, and without "www." too.</summary>
    static IEnumerable<string> Forms(string url, bool withScheme, string typed)
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) yield break;
        if (withScheme)
        {
            yield return url;
            yield break;
        }
        var bare = url[(url.IndexOf("://", StringComparison.Ordinal) + 3)..];
        if (bare.StartsWith("www.", StringComparison.OrdinalIgnoreCase) && !typed.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) yield return bare[4..];
        yield return bare;
    }

    /// <summary>Up to the first "/" of the address (or of the part after the scheme).</summary>
    static string HostOf(string form)
    {
        var start = form.Contains("://") ? form.IndexOf("://", StringComparison.Ordinal) + 3 : 0;
        var slash = form.IndexOf('/', start);
        return slash < 0 ? form : form[..slash];
    }
}
