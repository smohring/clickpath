using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Clickpath.Localization;

namespace Clickpath.Models;

public enum StepType { Navigate, Click, ClickPage, OpenLink, ForEachLink, ForEachListedLink, ForEachElement, WaitFor, IfFound, Delay, MoveMouse, Extract, SendToJDownloader, RunScript, CloseTab, CloseAllTabs, StopRepeating }

/// <summary>What kind of element a step looks for.</summary>
public enum StepTarget { Link, Clickable, Anything, Css }

/// <summary>How the step's text is compared with the element text.</summary>
public enum TextMatch { Contains, StartsWith, EndsWith, Exactly, Wildcard, Regex }

/// <summary>What a click does when it opens a new window/tab.</summary>
public enum NewWindowMode { Follow, Stay, StayAndClose, CloseAndRepeat }

/// <summary>Everything the page scripts need to find elements.</summary>
/// <summary><paramref name="Near"/>: a pattern the surroundings of a match (its row, with image names, alt texts and link targets) must contain too.</summary>
public sealed record ElementQuery(string Selector, string Pattern, bool MatchCase, bool Deepest, string Near = "", int Index = 0);

public sealed record ExtractedItem(string Text, string Href, string Page);

/// <summary>One automation step. A single flat class so plan files stay simple; <see cref="Type"/> decides which fields apply.</summary>
public sealed class Step : INotifyPropertyChanged
{
    const string ClickableSelector = "a, button, [role=button], [role=link], [role=tab], summary, [onclick]";
    const string AnythingSelector = "body *:not(script):not(style):not(noscript)";

    StepType _type;
    StepTarget _target = StepTarget.Clickable;
    TextMatch _matchMode = TextMatch.Contains;
    string _url = "", _selector = "", _text = "", _script = "", _package = "Clickpath", _nextPageText = "", _startText = "", _endText = "", _links = "", _nearText = "";
    TextMatch _startMode = TextMatch.Contains, _endMode = TextMatch.Contains;
    int _maxPages = 20;
    bool _paginate, _keepOpenPages = true;
    int _timeoutMs = 10000, _delayMs = 1000, _moveMs = 3000, _clickEveryMs = 4000;
    bool _newTab, _closeNewWindow, _repeatUntilNoWindow, _humanMouse, _endOnNewPage, _randomClicks, _continueOnError = true, _matchCase, _negate, _autostart;

    public event PropertyChangedEventHandler? PropertyChanged;

    public StepType Type { get => _type; set { if (Set(ref _type, value)) RaiseAll(); } }
    public string Url { get => _url; set => Set(ref _url, value); }
    public StepTarget Target { get => _target; set { if (Set(ref _target, value)) RaiseAll(); } }
    public string Selector { get => _selector; set => Set(ref _selector, value); }
    public string Text { get => _text; set => Set(ref _text, value); }
    public TextMatch MatchMode { get => _matchMode; set => Set(ref _matchMode, value); }
    public bool MatchCase { get => _matchCase; set => Set(ref _matchCase, value); }
    public string Script { get => _script; set => Set(ref _script, value); }
    // All durations are milliseconds.
    public int TimeoutMs { get => _timeoutMs; set => Set(ref _timeoutMs, Math.Max(100, value)); }
    public int DelayMs { get => _delayMs; set => Set(ref _delayMs, Math.Max(0, value)); }
    public int MoveMs { get => _moveMs; set => Set(ref _moveMs, Math.Max(100, value)); }

    bool _moveForever;

    /// <summary>Move mouse without a time limit: keeps going until an end condition (page change, text) is met or the run is stopped.</summary>
    public bool MoveForever
    {
        get => _moveForever;
        set
        {
            if (!Set(ref _moveForever, value)) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UsesMoveDuration)));
        }
    }

    // Plan files written before the switch to milliseconds hold seconds; read only, never written.
    [JsonPropertyName("TimeoutSeconds"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LegacyTimeoutSeconds { get => null; set { if (value is { } s) TimeoutMs = s * 1000; } }
    [JsonPropertyName("MoveSeconds"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LegacyMoveSeconds { get => null; set { if (value is { } s) MoveMs = s * 1000; } }
    [JsonPropertyName("ClickEverySeconds"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LegacyClickEverySeconds { get => null; set { if (value is { } s) ClickEveryMs = s * 1000; } }
    /// <summary>Click: follow the window the click opens. Open link: open it in a new tab.</summary>
    public bool NewTab { get => _newTab; set => Set(ref _newTab, value); }

    /// <summary>Click, when not following: close the window the click opened.</summary>
    public bool CloseNewWindow { get => _closeNewWindow; set => Set(ref _closeNewWindow, value); }

    /// <summary>Click, when not following: close the window the click opened (an ad) and click again until a click opens none.</summary>
    public bool RepeatUntilNoWindow { get => _repeatUntilNoWindow; set => Set(ref _repeatUntilNoWindow, value); }

    [JsonIgnore]
    public NewWindowMode NewWindowMode
    {
        get => NewTab ? NewWindowMode.Follow
            : RepeatUntilNoWindow ? NewWindowMode.CloseAndRepeat
            : CloseNewWindow ? NewWindowMode.StayAndClose : NewWindowMode.Stay;
        set
        {
            NewTab = value == NewWindowMode.Follow;
            CloseNewWindow = value == NewWindowMode.StayAndClose;
            RepeatUntilNoWindow = value == NewWindowMode.CloseAndRepeat;
        }
    }
    /// <summary>Click: walk the pointer to the target along a hand-like path and press at a random spot inside it.</summary>
    public bool HumanMouse { get => _humanMouse; set => Set(ref _humanMouse, value); }

    bool _isMultiSelected;

    /// <summary>Part of the selection of several steps in the tree (shown with a tint); never saved.</summary>
    [JsonIgnore]
    public bool IsMultiSelected
    {
        get => _isMultiSelected;
        set
        {
            if (_isMultiSelected == value) return;
            _isMultiSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMultiSelected)));
        }
    }

    /// <summary>
    /// Search steps: only the match whose row also shows this - a text, the alt text or file name of an image (a hoster logo), a class or the
    /// host of a link. Lets a step pick "Download Mirror" of the one hoster it needs, wherever that hoster is listed. Empty = no restriction.
    /// </summary>
    public string NearText { get => _nearText; set => Set(ref _nearText, value ?? ""); }

    string _untilText = "";
    TextMatch _untilMode = TextMatch.Contains;
    int _untilWaitMs = 2000;

    /// <summary>Repeat for each match: stop as soon as visible text like this appears (checked after every round); empty = no condition.</summary>
    public string UntilText { get => _untilText; set => Set(ref _untilText, value ?? ""); }
    public TextMatch UntilMode { get => _untilMode; set => Set(ref _untilMode, value); }

    /// <summary>How long to wait after each round for the condition to appear (script may insert it a moment after the click); 0 = look once.</summary>
    public int UntilWaitMs { get => _untilWaitMs; set => Set(ref _untilWaitMs, Math.Max(0, value)); }

    [JsonIgnore] public bool UsesUntil => Type == StepType.ForEachElement;

    /// <summary>The query for the stop condition: the innermost visible element whose text matches; null if there is no condition.</summary>
    public ElementQuery? ToUntilQuery(Func<string, string>? expand = null)
    {
        var text = expand?.Invoke(UntilText) ?? UntilText;
        return string.IsNullOrWhiteSpace(text) ? null : new(AnythingSelector, PatternFor(text, UntilMode), MatchCase, Deepest: true);
    }

    bool _clickEachMatch = true;

    /// <summary>Repeat for each match: click the match first (with this step's window settings), then run the steps below it.</summary>
    public bool ClickEachMatch { get => _clickEachMatch; set => Set(ref _clickEachMatch, value); }

    /// <summary>Which match a search takes (0 = the first); only set by the element loop for the click of one round, never saved.</summary>
    [JsonIgnore] public int MatchIndex { get; set; }

    /// <summary>Repeat for each link in my list: the links, one per line (see <see cref="Engine.LinkList"/>).</summary>
    public string Links
    {
        get => _links;
        set
        {
            if (!Set(ref _links, value ?? "")) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LinkListSummary)));
        }
    }

    /// <summary>Close all windows: spare the pages the surrounding "for each link" loops still need (their list pages).</summary>
    public bool KeepOpenPages { get => _keepOpenPages; set => Set(ref _keepOpenPages, value); }

    /// <summary>For each link: after the links of a page, go to the next page and continue there.</summary>
    public bool Paginate
    {
        get => _paginate;
        set
        {
            if (Set(ref _paginate, value)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UsesPaginationOptions)));
        }
    }

    /// <summary>Text of the "next page" button or link; empty = detect it automatically.</summary>
    public string NextPageText { get => _nextPageText; set => Set(ref _nextPageText, value ?? ""); }

    /// <summary>Safety limit for the number of pages that are visited.</summary>
    public int MaxPages { get => _maxPages; set => Set(ref _maxPages, Math.Max(1, value)); }

    /// <summary>For each link: skip links until one matches (its text or address); empty = start with the first.</summary>
    public string StartText { get => _startText; set => Set(ref _startText, value ?? ""); }
    public TextMatch StartMode { get => _startMode; set => Set(ref _startMode, value); }

    /// <summary>For each link: stop after the link that matches (its text or address); empty = go to the last.</summary>
    public string EndText { get => _endText; set => Set(ref _endText, value ?? ""); }
    public TextMatch EndMode { get => _endMode; set => Set(ref _endMode, value); }

    /// <summary>Move mouse: now and then click into an empty spot of the page.</summary>
    public bool RandomClicks
    {
        get => _randomClicks;
        set
        {
            if (Set(ref _randomClicks, value)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UsesClickRate)));
        }
    }

    /// <summary>Average time between two random clicks; the real gap varies between half and one and a half times this.</summary>
    public int ClickEveryMs { get => _clickEveryMs; set => Set(ref _clickEveryMs, Math.Max(100, value)); }

    /// <summary>Pause / move mouse: stop waiting as soon as the page changes - a new page loads, a window opens, something is handed over or script replaces the content.</summary>
    public bool EndOnNewPage
    {
        get => _endOnNewPage;
        set
        {
            if (Set(ref _endOnNewPage, value)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UsesChangeThreshold)));
        }
    }

    int _changedWords = Browser.PageChange.DefaultChangedWords;
    int _sourceChars;

    /// <summary>With "end on page change": the step also ends when the length of the generated page source (the live DOM as HTML) changes by this many characters; 0 = off.</summary>
    public int SourceChars { get => _sourceChars; set => Set(ref _sourceChars, Math.Clamp(value, 0, 100_000_000)); }

    /// <summary>With "end on page change": this many words that appeared or disappeared since the start end the step (default 20), whatever the length of the page.</summary>
    public int ChangedWords { get => _changedWords; set => Set(ref _changedWords, Math.Clamp(value, 1, 100000)); }
    public bool ContinueOnError { get => _continueOnError; set => Set(ref _continueOnError, value); }
    public bool Negate { get => _negate; set => Set(ref _negate, value); }
    public string Package { get => _package; set => Set(ref _package, value); }
    public bool Autostart { get => _autostart; set => Set(ref _autostart, value); }

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public ObservableCollection<Step> Children { get; } = new();

    [JsonIgnore] public bool UsesUrl => Type == StepType.Navigate;
    [JsonIgnore] public bool UsesMatch => Type is StepType.Click or StepType.OpenLink or StepType.ForEachLink or StepType.ForEachElement or StepType.WaitFor or StepType.IfFound or StepType.Extract or StepType.SendToJDownloader;
    [JsonIgnore] public bool UsesCss => UsesMatch && Target == StepTarget.Css;
    [JsonIgnore] public bool UsesTimeout => UsesMatch;
    [JsonIgnore] public bool UsesNewTab => Type == StepType.OpenLink;
    [JsonIgnore] public bool UsesNewWindow => Type is StepType.Click or StepType.ClickPage or StepType.ForEachElement;
    [JsonIgnore] public bool UsesHumanMouse => UsesNewWindow;
    [JsonIgnore] public bool UsesTypeSwitch => CanHaveChildren;
    [JsonIgnore] public bool ShowsPlainTitle => !CanHaveChildren;
    [JsonIgnore] public bool UsesLinkList => Type == StepType.ForEachListedLink;

    /// <summary>"12 links" or "12 links, 2 lines ignored", shown under the list editor.</summary>
    [JsonIgnore]
    public string LinkListSummary
    {
        get
        {
            var (links, ignored) = Engine.LinkList.Count(Links);
            var text = string.Format(Loc.T("Summary.Links"), links);
            return ignored > 0 ? $"{text}, {string.Format(Loc.T("Summary.Ignored"), ignored)}" : text;
        }
    }

    [JsonIgnore] public bool UsesKeepOpenPages => Type == StepType.CloseAllTabs;
    [JsonIgnore] public bool UsesForEachOptions => Type == StepType.ForEachLink;
    [JsonIgnore] public bool UsesPaginationOptions => UsesForEachOptions && Paginate;
    [JsonIgnore] public bool UsesRandomClicks => Type == StepType.MoveMouse;
    [JsonIgnore] public bool UsesClickRate => UsesRandomClicks && RandomClicks;
    [JsonIgnore] public bool UsesEndOnNewPage => Type is StepType.Delay or StepType.MoveMouse;
    [JsonIgnore] public bool UsesChangeThreshold => UsesEndOnNewPage && EndOnNewPage;

    /// <summary>
    /// How long the step can wait at most, for the countdown on its row: its length for a pause / mouse move, its timeout for steps that look for
    /// something on the page; null for steps without a limit (repeat steps run as long as their list is, a navigation has its own limit).
    /// </summary>
    [JsonIgnore]
    public int? WaitLimitMs => Type switch
    {
        StepType.Delay => DelayMs,
        StepType.MoveMouse => MoveForever ? null : MoveMs,
        StepType.ForEachLink or StepType.ForEachListedLink or StepType.ForEachElement => null,
        _ => UsesTimeout ? TimeoutMs : null,
    };

    /// <summary>A mouse move without a time limit: there is no time left to show, only how long it has been going.</summary>
    [JsonIgnore] public bool WaitLimitIsOpen => Type == StepType.MoveMouse && MoveForever;

    /// <summary>The limit is a timeout (the step usually ends earlier, so its countdown only shows after a moment) rather than the planned length.</summary>
    [JsonIgnore] public bool WaitLimitIsTimeout => Type is not (StepType.Delay or StepType.MoveMouse);

    // ---- runtime state while a run executes the step (never saved)

    bool _isRunning;
    double _progress;
    string _remainingText = "";

    /// <summary>The step is being executed now; a repeat step stays marked while the steps inside it run.</summary>
    [JsonIgnore]
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning == value) return;
            _isRunning = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
        }
    }

    /// <summary>Share of the wait limit that has passed (0..1).</summary>
    [JsonIgnore] public double Progress => _progress;

    /// <summary>"1:27 left" / "6 s until timeout" while a step with a limit runs; empty otherwise.</summary>
    [JsonIgnore] public string RemainingText => _remainingText;

    [JsonIgnore] public bool HasCountdown => _remainingText.Length > 0;

    bool _indeterminate;
    string _changeText = "";

    /// <summary>"page changed: 7 words differ (ends at 20)" while a step watches the page for changes; empty otherwise.</summary>
    [JsonIgnore] public string ChangeText => _changeText;

    [JsonIgnore] public bool HasChangeText => _changeText.Length > 0;

    /// <summary>Shows how far the page has changed from its state at the start of the step, next to the threshold that ends it.</summary>
    public void ShowChange(int changedWords, int sourceDelta)
    {
        var text = string.Format(Loc.T("Run.Changed"), changedWords, ChangedWords)
            + string.Format(Loc.T("Run.SourceChanged"), sourceDelta, SourceChars > 0 ? string.Format(Loc.T("Run.SourceLimit"), SourceChars) : "");
        if (text == _changeText) return;
        _changeText = text;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChangeText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasChangeText)));
    }

    /// <summary>The bar has no end to run towards (a mouse move without a time limit).</summary>
    [JsonIgnore] public bool CountdownIndeterminate => _indeterminate;

    /// <summary>For a step without a limit: "running for 0:47" and a bar that just keeps moving.</summary>
    public void ShowElapsed(int elapsedMs)
    {
        _indeterminate = true;
        _remainingText = string.Format(Loc.T("Run.Elapsed"), $"{elapsedMs / 60_000}:{elapsedMs / 1000 % 60:00}");
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountdownIndeterminate)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RemainingText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasCountdown)));
    }

    /// <summary>Shows how much of the wait limit has passed; called while the step runs.</summary>
    public void ShowCountdown(int totalMs, int elapsedMs, bool isTimeout)
    {
        var left = Math.Max(0, totalMs - elapsedMs);
        _progress = totalMs <= 0 ? 1 : Math.Clamp((double)elapsedMs / totalMs, 0, 1);
        var time = left >= 60_000 ? $"{left / 60_000}:{left / 1000 % 60:00}" : $"{(left + 999) / 1000} s";
        _remainingText = string.Format(Loc.T(isTimeout ? "Run.TimeoutLeft" : "Run.Left"), time);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Progress)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RemainingText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasCountdown)));
    }

    public void ClearCountdown()
    {
        if (_changeText.Length > 0)
        {
            _changeText = "";
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChangeText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasChangeText)));
        }
        if (_remainingText.Length == 0) return;
        _progress = 0;
        _remainingText = "";
        _indeterminate = false;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountdownIndeterminate)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Progress)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RemainingText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasCountdown)));
    }

    /// <summary>Pause / move mouse also take the "end as soon as this text appears" condition (<see cref="UntilText"/>).</summary>
    [JsonIgnore] public bool UsesEndText => Type is StepType.Delay or StepType.MoveMouse;
    [JsonIgnore] public bool UsesContinueOnError => Type is StepType.ForEachLink or StepType.ForEachListedLink or StepType.ForEachElement;
    [JsonIgnore] public bool UsesClickEachMatch => Type == StepType.ForEachElement;
    [JsonIgnore] public bool UsesDelay => Type == StepType.Delay;
    [JsonIgnore] public bool UsesMoveDuration => Type == StepType.MoveMouse && !MoveForever;
    [JsonIgnore] public bool UsesMoveForever => Type == StepType.MoveMouse;
    [JsonIgnore] public bool UsesScript => Type == StepType.RunScript;
    [JsonIgnore] public bool UsesNegate => Type == StepType.IfFound;
    [JsonIgnore] public bool UsesPackage => Type == StepType.SendToJDownloader;
    [JsonIgnore] public bool CanHaveChildren => AllowsChildren(Type);

    /// <summary>The steps that hold other steps: the loops and the condition.</summary>
    public static bool AllowsChildren(StepType type) => type is StepType.ForEachLink or StepType.ForEachListedLink or StepType.ForEachElement or StepType.IfFound;

    /// <summary>Whether the kind of this step can be changed to <paramref name="type"/> without losing the steps inside it.</summary>
    public bool CanBecome(StepType type) => type == Type || AllowsChildren(type) || Children.Count == 0;

    /// <summary>Changes the kind of the step and keeps its children and settings; the default wait and target follow the kind unless the user changed them.</summary>
    public bool ConvertTo(StepType type)
    {
        if (!CanBecome(type)) return false;
        var old = Type;
        var keepsTimeout = TimeoutMs != DefaultTimeoutMs(old);
        var keepsTarget = Target != DefaultTarget(old);
        Type = type;
        if (!keepsTimeout) TimeoutMs = DefaultTimeoutMs(type);
        if (!keepsTarget) Target = DefaultTarget(type);
        return true;
    }

    static int DefaultTimeoutMs(StepType type) => type == StepType.IfFound ? 2000 : 10000;

    static StepTarget DefaultTarget(StepType type) =>
        type is StepType.OpenLink or StepType.ForEachLink or StepType.Extract or StepType.SendToJDownloader ? StepTarget.Link : StepTarget.Clickable;
    [JsonIgnore] public string TimeoutLabel => Loc.T(Type == StepType.IfFound ? "Field.TimeoutIf" : "Field.Timeout");
    [JsonIgnore] public bool IsRegexMode => MatchMode == TextMatch.Regex;

    [JsonIgnore] public string Title => Loc.T("Step." + Type);
    [JsonIgnore] public string Icon => IconFor(Type);

    [JsonIgnore]
    public string Subtitle
    {
        get
        {
            var text = string.IsNullOrWhiteSpace(Text) ? Loc.T("Match.Any") : $"{Loc.T("Match." + MatchMode)} “{Text}”";
            var what = Target == StepTarget.Css ? Selector : Loc.T("Target." + Target);
            var match = $"{what} · {text}" + (NearText.Length > 0 ? $" · {Loc.T("Sub.Near")} “{NearText}”" : "");
            return Type switch
            {
                StepType.Navigate => Url,
                StepType.Click => $"{match}{WindowSuffix}",
                StepType.ForEachElement => $"{match}{WindowSuffix}{(UntilText.Length > 0 ? $" · {Loc.T("Sub.Until")} “{UntilText}”" : "")}",
                StepType.ClickPage => $"{Loc.T("Sub.PageCenter")}{WindowSuffix}",
                StepType.OpenLink => NewTab ? $"{match} {Loc.T("Sub.NewTab")}" : match,
                StepType.IfFound => Negate ? $"{Loc.T("Sub.Not")} {match}" : match,
                StepType.SendToJDownloader => $"{match} → {Package}",
                StepType.ForEachLink => $"{match}{LoopSuffix}",
                StepType.ForEachListedLink => string.Format(Loc.T("Summary.Links"), Engine.LinkList.Count(Links).Links),
                StepType.CloseAllTabs => KeepOpenPages ? Loc.T("Sub.KeepsPages") : "",
                StepType.WaitFor or StepType.Extract => match,
                StepType.Delay => $"{DelayMs} ms{EndSuffix}",
                StepType.MoveMouse => $"{(MoveForever ? Loc.T("Sub.NoLimit") : $"{MoveMs} ms")}{(RandomClicks ? $" · {Loc.T("Sub.RandomClicks")}" : "")}{EndSuffix}",
                StepType.RunScript => Script.Split('\n')[0].Trim(),
                _ => "",
            };
        }
    }

    string EndSuffix => (EndOnNewPage ? $" {Loc.T("Sub.EndOnNewPage")}" : "") + (UntilText.Length > 0 ? $" · {Loc.T("Sub.Until")} “{UntilText}”" : "");

    string LoopSuffix =>
        (Paginate ? $" · {Loc.T("Sub.AllPages")}" : "") +
        (StartText.Length > 0 || EndText.Length > 0 ? $" · {Loc.T("Sub.Range")}" : "");

    string WindowSuffix => NewWindowMode switch
    {
        NewWindowMode.Follow => $" {Loc.T("Sub.NewTab")}",
        NewWindowMode.StayAndClose => $" {Loc.T("Sub.CloseNew")}",
        NewWindowMode.CloseAndRepeat => $" {Loc.T("Sub.Repeat")}",
        _ => "",
    };

    [JsonIgnore] public string Summary => string.IsNullOrEmpty(Subtitle) ? Title : $"{Title}: {Subtitle}";

    /// <summary>The automation name of a row in the step tree is its ToString.</summary>
    public override string ToString() => Summary;

    /// <summary>The regular expression (JavaScript syntax) that expresses the text and match mode; <paramref name="expand"/> fills in variables.</summary>
    public string BuildPattern(Func<string, string>? expand = null) => PatternFor(expand?.Invoke(Text) ?? Text, MatchMode);

    /// <summary>The regular expression for <paramref name="raw"/> compared the way <paramref name="mode"/> says; empty text gives no pattern.</summary>
    public static string PatternFor(string raw, TextMatch mode)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        if (mode == TextMatch.Regex) return raw;
        var text = Regex.Replace(raw.Trim(), @"\s+", " ");
        return mode switch
        {
            TextMatch.StartsWith => "^" + Escape(text),
            TextMatch.EndsWith => Escape(text) + "$",
            TextMatch.Exactly => "^" + Escape(text) + "$",
            TextMatch.Wildcard => "^" + string.Join(".*", text.Split('*').Select(Escape)) + "$",
            _ => Escape(text),
        };
    }

    public ElementQuery ToQuery(Func<string, string>? expand = null) => new(
        Target switch
        {
            StepTarget.Link => "a[href]",
            StepTarget.Clickable => ClickableSelector,
            StepTarget.Anything => AnythingSelector,
            _ => Selector,
        },
        BuildPattern(expand), MatchCase, Deepest: Target is StepTarget.Clickable or StepTarget.Anything,
        Near: PatternFor(expand?.Invoke(NearText) ?? NearText, TextMatch.Contains), Index: MatchIndex);

    public static Step Create(StepType type) => new()
    {
        Type = type,
        Target = DefaultTarget(type),
        Url = type == StepType.Navigate ? "https://" : "",
        TimeoutMs = DefaultTimeoutMs(type),
    };

    static string Escape(string s) => Regex.Replace(s, @"[.*+?^${}()|[\]\\/]", "\\$&");

    static string IconFor(StepType type) => type switch
    {
        StepType.Navigate => "",
        StepType.Click => "",
        StepType.ClickPage => "",
        StepType.OpenLink => "",
        StepType.ForEachLink => "",
        StepType.WaitFor => "",
        StepType.ForEachListedLink => "",
        StepType.ForEachElement => "",
        StepType.StopRepeating => "",
        StepType.CloseAllTabs => "",
        StepType.IfFound => "",
        StepType.Delay => "",
        StepType.MoveMouse => "",
        StepType.Extract => "",
        StepType.SendToJDownloader => "",
        StepType.RunScript => "",
        _ => "",
    };

    bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NewWindowMode)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Subtitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
        return true;
    }

    void RaiseAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
