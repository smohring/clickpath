using System.Text.RegularExpressions;
using Clickpath.Browser;
using Clickpath.Models;

namespace Clickpath.Engine;

/// <summary>
/// The links of a loop that are worked off: from the first link matching the start delimiter to the one matching the end delimiter,
/// both included. Delimiters are compared with the link text and its address. Stateful, so it spans several pages.
/// </summary>
public sealed class LinkRange
{
    readonly Regex? _start, _end;
    bool _started;

    LinkRange(Regex? start, Regex? end)
    {
        _start = start;
        _end = end;
        _started = start is null;
    }

    /// <summary>The end link was taken; nothing after it is.</summary>
    public bool Finished { get; private set; }

    /// <summary>True if there is a delimiter at all.</summary>
    public bool IsLimited => _start is not null || _end is not null;

    public static LinkRange From(Step step) => new(Compile(step.StartText, step.StartMode, step), Compile(step.EndText, step.EndMode, step));

    /// <summary>Whether <paramref name="link"/> is to be processed; call it for every link in page order.</summary>
    public bool Admit(LinkMatch link)
    {
        if (Finished) return false;
        if (!_started)
        {
            if (!Matches(_start!, link)) return false;
            _started = true;
        }
        if (_end is not null && Matches(_end, link)) Finished = true;
        return true;
    }

    static bool Matches(Regex pattern, LinkMatch link) => pattern.IsMatch(link.Text) || pattern.IsMatch(link.Href);

    static Regex? Compile(string text, TextMatch mode, Step step)
    {
        var pattern = Step.PatternFor(text, mode);
        if (pattern.Length == 0) return null;
        try { return new Regex(pattern, step.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase); }
        catch (ArgumentException ex) { throw new StepFailedException($"Invalid pattern \"{text}\": {ex.Message}"); }
    }
}
