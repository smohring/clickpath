using Clickpath.Browser;
using Clickpath.Models;

namespace Clickpath.Engine;

/// <summary>Thrown by "stop repeating" and caught by the innermost repeat step around it.</summary>
sealed class LoopExit : Exception;

/// <summary>Executes steps against a stack of tabs; the top of the stack is the page every step acts on.</summary>
public sealed class StepRunner(BrowserHost host, Action<string> log, Action<ExtractedItem> collect)
{
    sealed class Context(BrowserTab start)
    {
        public List<BrowserTab> Stack { get; } = [start];
        public BrowserTab Current => Stack[^1];
        public Dictionary<string, string> Vars { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>For every running "for each link": how many stack tabs lie below it (its list page and what the list page needs).</summary>
        public Stack<int> LoopDepths { get; } = new();
        public int KeepDepth => LoopDepths.Count > 0 ? LoopDepths.Peek() : 0;

        /// <summary>Replaces {text} and {href} (the current "for each" link) in <paramref name="s"/>.</summary>
        public string Expand(string s) => Vars.Aggregate(s, (acc, v) => acc.Replace("{" + v.Key + "}", v.Value, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Extra pause after every step, e.g. to slow down for pages that react slowly.</summary>
    public int PauseMs { get; set; }

    /// <summary>Outline the element for a moment before clicking it, so you can see where it clicks.</summary>
    public bool ShowClicks { get; set; }

    public async Task RunAsync(IReadOnlyList<Step> steps, BrowserTab start, CancellationToken ct) =>
        await RunStepsAsync(steps, new Context(start), "", ct);

    /// <summary>A step starts / ends executing (also the steps inside a repeat step); the window shows which one is current.</summary>
    public event Action<Step>? StepStarted;
    public event Action<Step>? StepFinished;

    /// <summary>
    /// Feeds the countdown on the step's row four times a second: at once for a pause or mouse move (the planned length), after a second for steps
    /// that look for something (the time left until their timeout - most of them finish before that and show nothing).
    /// </summary>
    static async Task TickAsync(Step step, CancellationToken token)
    {
        var open = step.WaitLimitIsOpen;
        if (step.WaitLimitMs is not { } limit && !open) return;
        var showAfterMs = step.WaitLimitIsTimeout ? 1000 : 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            while (true)
            {
                if (open) step.ShowElapsed((int)watch.ElapsedMilliseconds);
                else if (watch.ElapsedMilliseconds >= showAfterMs) step.ShowCountdown(step.WaitLimitMs!.Value, (int)watch.ElapsedMilliseconds, step.WaitLimitIsTimeout);
                await Task.Delay(250, token);
            }
        }
        catch (OperationCanceledException) { }
    }

    async Task RunStepsAsync(IReadOnlyList<Step> steps, Context ctx, string indent, CancellationToken ct)
    {
        foreach (var step in steps)
        {
            ct.ThrowIfCancellationRequested();
            log($"{indent}▶ {step.Summary}");
            step.IsRunning = true;
            StepStarted?.Invoke(step);
            using var ticking = new CancellationTokenSource();
            var ticker = TickAsync(step, ticking.Token);
            try { await ExecuteAsync(step, ctx, indent, ct); }
            finally
            {
                ticking.Cancel();
                await ticker;
                step.IsRunning = false;
                step.ClearCountdown();
                StepFinished?.Invoke(step);
            }
            if (PauseMs > 0) await Task.Delay(PauseMs, ct);
        }
    }

    async Task ExecuteAsync(Step step, Context ctx, string indent, CancellationToken ct)
    {
        var tab = ctx.Current;
        var timeout = TimeSpan.FromMilliseconds(step.TimeoutMs);
        var query = step.ToQuery(ctx.Expand);
        switch (step.Type)
        {
            case StepType.Navigate:
                await tab.NavigateAsync(ctx.Expand(step.Url), ct);
                break;

            case StepType.Click:
            case StepType.ClickPage:
                await ClickAsync(step, ctx, timeout, ct);
                break;

            case StepType.OpenLink:
            {
                var link = await PollAsync(async () => (await tab.FindAsync(query, false)).FirstOrDefault(l => l.Href.Length > 0),
                    timeout, "a matching link with a URL", ct);
                if (step.NewTab) Push(ctx, await OpenAsync(link.Href, ct));
                else await tab.NavigateAsync(link.Href, ct);
                break;
            }

            case StepType.ForEachLink:
                await ForEachAsync(step, ctx, timeout, indent, ct);
                break;

            case StepType.ForEachListedLink:
                await ForEachListedAsync(step, ctx, indent, ct);
                break;

            case StepType.ForEachElement:
                await ForEachElementAsync(step, ctx, timeout, indent, ct);
                break;

            case StepType.StopRepeating:
                if (ctx.LoopDepths.Count == 0) log($"{indent}  not inside a repeat step - ignored");
                else throw new LoopExit();
                break;

            case StepType.WaitFor:
                await PollAsync(async () => (await tab.FindAsync(query, true)).Count > 0 ? "found" : null,
                    timeout, "a matching visible element", ct);
                break;

            case StepType.IfFound:
            {
                var found = await IsFoundAsync(tab, query, timeout, ct);
                if (found != step.Negate) await RunStepsAsync(step.Children, ctx, indent + "  ", ct);
                else log($"{indent}  (condition not met, skipped)");
                break;
            }

            case StepType.Delay:
                await WaitingAsync(step, tab, step.ToUntilQuery(ctx.Expand), token => Task.Delay(step.DelayMs, token), ct);
                break;

            case StepType.MoveMouse:
            {
                TimeSpan? clickEvery = step.RandomClicks ? TimeSpan.FromMilliseconds(step.ClickEveryMs) : null;
                var until = step.ToUntilQuery(ctx.Expand);
                if (step.MoveForever && !step.EndOnNewPage && until is null)
                    throw new StepFailedException("\"No time limit\" needs an end condition: switch on \"end as soon as the page changes\" or give a text");
                var duration = step.MoveForever ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(step.MoveMs);
                await WaitingAsync(step, tab, until, token => tab.MoveMouseRandomlyAsync(duration, clickEvery, token), ct);
                break;
            }

            case StepType.Extract:
            {
                var matches = await PollAsync(async () => (await tab.FindAsync(query, false)) is { Count: > 0 } m ? m : null,
                    timeout, "matching elements", ct);
                foreach (var m in matches) collect(new ExtractedItem(m.Text, m.Href, tab.Url));
                log($"{indent}  {matches.Count} item(s) extracted");
                break;
            }

            case StepType.SendToJDownloader:
            {
                var matches = await PollAsync(async () => (await tab.FindAsync(query, false)) is { Count: > 0 } m ? m : null,
                    timeout, "matching links", ct);
                var urls = matches.Select(m => m.Href).Where(h => h.Length > 0).Distinct().ToList();
                if (urls.Count == 0) throw new StepFailedException("The matches have no link address to send");
                var package = ctx.Expand(step.Package);
                await JDownloader.SendAsync(urls, package, step.Autostart, ct);
                log($"{indent}  {urls.Count} link(s) sent to JDownloader (package \"{package}\")");
                break;
            }

            case StepType.RunScript:
            {
                var result = await tab.EvalAsync(ctx.Expand(step.Script));
                if (result.ValueKind is not System.Text.Json.JsonValueKind.Null and not System.Text.Json.JsonValueKind.Undefined)
                    log($"{indent}  → {result}");
                break;
            }

            case StepType.CloseAllTabs:
            {
                var keep = step.KeepOpenPages ? ctx.Stack.Take(ctx.KeepDepth).ToList() : [];
                var (current, closed) = await host.CloseAllAsync(keep);
                ctx.Stack.Clear();
                ctx.Stack.AddRange(keep.Count > 0 ? keep : [current]);
                log(keep.Count > 0
                    ? $"  closed {closed} window(s); kept {keep.Count} page(s) the loop still needs"
                    : $"  closed {closed} window(s); continuing on a blank tab");
                break;
            }

            case StepType.CloseTab:
                if (ctx.Stack.Count < 2) throw new StepFailedException("Cannot close the start tab");
                UnwindTo(ctx, ctx.Stack.Count - 1);
                break;
        }
    }

    /// <summary>
    /// Runs a timed step; with "end on page change" it is cut short when the tab loads a page, opens a window, hands something over or script
    /// replaces enough of the content, and with an <paramref name="until"/> text as soon as that text is visible.
    /// </summary>
    async Task WaitingAsync(Step step, BrowserTab tab, ElementQuery? until, Func<CancellationToken, Task> action, CancellationToken ct)
    {
        if (!step.EndOnNewPage && until is null)
        {
            await action(ct);
            return;
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var reason = "";
        void End(string why)
        {
            if (reason.Length == 0) reason = why;
            stop.Cancel();
        }
        var popupOpened = false;
        void OnPopup(BrowserTab _, BrowserTab __)
        {
            popupOpened = true;
            End("a new window opened");
        }
        void OnHandedOver(BrowserTab _, string what) => End($"handed over: {what}");
        void OnNavigation(BrowserTab _) => End("a new page started loading");
        Task watcher = Task.CompletedTask;
        PageSnapshot? start = null, latest = null;
        string? startDescription = null;
        try
        {
            if (step.EndOnNewPage)
            {
                tab.PopupOpened += OnPopup;
                tab.HandedOver += OnHandedOver;
                tab.NavigationBegan += OnNavigation;
                if (await tab.SnapshotAsync() is { } before)
                {
                    start = before;
                    startDescription = await tab.DescribeAsync();
                    watcher = WatchContentAsync(tab, before, step, now =>
                    {
                        latest = now;
                        step.ShowChange(PageChange.ChangedWordCount(before, now), PageChange.SourceDelta(before, now));
                    }, () => End("the page content was replaced"), stop.Token);
                }
                else log("  could not read the text of the page - detecting changed content is off for this step");
            }
            if (until is not null)
                watcher = Task.WhenAll(watcher, WatchTextAsync(tab, until, () => End("the text appeared"), stop.Token));
            await action(stop.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && reason.Length > 0)
        {
            log($"  {reason}: continuing with the next step");
            if (popupOpened) host.Select(tab);   // the popup took the screen; mouse input hangs in a hidden tab
        }
        finally
        {
            if (reason.Length == 0 && start is not null && latest is not null)   // ran to its end or was stopped: say how far the page had changed
            {
                log($"  no page change detected: {PageChange.ChangedWordCount(start, latest)} words differ, source {PageChange.SourceDelta(start, latest)} characters different (counts from {step.ChangedWords} words{(step.SourceChars > 0 ? $" or {step.SourceChars} characters" : "")})");
                log($"  page at the start: {startDescription ?? "not readable"}");
                log($"  page now:          {await tab.DescribeAsync() ?? "not readable"}");
            }
            stop.Cancel();
            await watcher;
            tab.PopupOpened -= OnPopup;
            tab.HandedOver -= OnHandedOver;
            tab.NavigationBegan -= OnNavigation;
        }
    }

    /// <summary>Compares the page with its state at the start every 300 ms; calls <paramref name="replaced"/> once script has swapped the content.</summary>
    static async Task WatchContentAsync(BrowserTab tab, PageSnapshot before, Step step, Action<PageSnapshot> seen, Action replaced, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                await Task.Delay(300, ct);
                if (await tab.SnapshotAsync() is not { } now) continue;
                seen(now);
                if (PageChange.IsNewPage(before, now, step.ChangedWords, step.SourceChars))
                {
                    replaced();
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Looks every 300 ms for the text; calls <paramref name="found"/> once a visible match shows (also when script inserts it into the same page).</summary>
    static async Task WatchTextAsync(BrowserTab tab, ElementQuery query, Action found, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                try
                {
                    if (await IsFoundAsync(tab, query, TimeSpan.Zero, ct))
                    {
                        found();
                        return;
                    }
                }
                catch (StepFailedException) { }   // the page is changing under us: look again
                await Task.Delay(300, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    const int MaxRepeatedClicks = 10;

    /// <summary>One click; with "repeat" the windows it opens (ads) are closed and the click is repeated until one opens none.</summary>
    async Task ClickAsync(Step step, Context ctx, TimeSpan timeout, CancellationToken ct)
    {
        if (!step.RepeatUntilNoWindow || step.NewTab)
        {
            await ClickOnceAsync(step, ctx, timeout, step.CloseNewWindow, ct);
            return;
        }

        for (var n = 1; n <= MaxRepeatedClicks; n++)
        {
            if (!await ClickOnceAsync(step, ctx, timeout, closeNewWindow: true, ct))
            {
                log($"  no window opened after click {n}: done");
                return;
            }
            log($"  click {n} opened a window (closed); clicking again");
        }
        throw new StepFailedException($"The click still opened a window after {MaxRepeatedClicks} clicks");
    }

    /// <summary>Returns true if the click opened a window that was left behind (not followed).</summary>
    async Task<bool> ClickOnceAsync(Step step, Context ctx, TimeSpan timeout, bool closeNewWindow, CancellationToken ct)
    {
        var tab = ctx.Current;
        var popup = new TaskCompletionSource<BrowserTab>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handedOver = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnPopup(BrowserTab _, BrowserTab p) => popup.TrySetResult(p);
        void OnHandedOver(BrowserTab _, string what) => handedOver.TrySetResult(what);
        tab.PopupOpened += OnPopup;
        tab.HandedOver += OnHandedOver;
        try
        {
            var query = step.ToQuery(ctx.Expand);
            await PollAsync(async () =>
            {
                if (step.Type == StepType.ClickPage)
                {
                    await tab.ClickViewportCenterAsync(ShowClicks, step.HumanMouse, ct);
                    return "clicked";
                }
                if (ShowClicks)
                {
                    if ((await tab.HighlightAsync(query, true, 900)).Count == 0) return null;
                    await Task.Delay(700, ct);
                }
                return await tab.ClickFirstAsync(query, step.HumanMouse, ct) ? "clicked" : null;
            }, timeout, "a clickable element", ct);

            if (step.NewTab)
            {
                var first = await Task.WhenAny(popup.Task, handedOver.Task, Task.Delay(timeout, ct));
                if (first == popup.Task)
                {
                    var opened = await popup.Task;
                    Push(ctx, opened);
                    await opened.SettlePopupAsync(ct);
                }
                else if (first == handedOver.Task) log($"  handed over (other app or download): {await handedOver.Task}");
                else throw new StepFailedException("The click did not open a new tab/window");
                return false;
            }

            await tab.SettleAsync(ct);
            await Task.WhenAny(popup.Task, Task.Delay(250, ct));   // a window can open a moment after the click
            var opensWindow = popup.Task.IsCompletedSuccessfully;
            if (opensWindow)
            {
                var opened = await popup.Task;
                if (closeNewWindow) host.Close(opened);
                host.Select(tab);
                if (!step.RepeatUntilNoWindow)
                    log(closeNewWindow ? "  a new window opened and was closed; staying on this tab" : "  a new window opened; staying on this tab");
            }
            if (handedOver.Task.IsCompleted) log($"  handed over (other app or download): {await handedOver.Task}");
            return opensWindow;
        }
        finally
        {
            tab.PopupOpened -= OnPopup;
            tab.HandedOver -= OnHandedOver;
        }
    }

    /// <summary>
    /// Opens every matching link in its own tab and runs the children there. With pagination it then moves the list tab to the next
    /// page and goes on; the range limits which links (across all pages) are worked off.
    /// </summary>
    async Task ForEachAsync(Step step, Context ctx, TimeSpan timeout, string indent, CancellationToken ct)
    {
        var list = ctx.Current;
        var range = LinkRange.From(step);
        var query = step.ToQuery(ctx.Expand);
        var seen = new HashSet<string>();   // addresses already listed, so a link repeated on every page counts once

        var depth = ctx.Stack.Count;
        var outer = ctx.Vars.ToDictionary(v => v.Key, v => v.Value);
        var n = 0;
        var page = 1;
        var left = false;   // a "stop repeating" step ended the loop
        ctx.LoopDepths.Push(depth);
        try
        {
            while (true)
            {
                var links = await NewLinksAsync(list, query, seen, timeout, required: page == 1, ct);
                if (links is null)
                {
                    log($"{indent}  no new links on page {page}: done");
                    break;
                }
                foreach (var link in links) seen.Add(link.Href);
                log(step.Paginate ? $"{indent}  page {page}: {links.Count} link(s)" : $"{indent}  {links.Count} link(s) to process");

                foreach (var link in links)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!range.Admit(link)) continue;
                    UnwindTo(ctx, depth);
                    ctx.Vars["text"] = link.Text;
                    ctx.Vars["href"] = link.Href;
                    log($"{indent}  [{++n}] {(link.Text.Length > 0 ? link.Text : link.Href)}");
                    left = await RunLinkAsync(step, ctx, link.Href, indent, ct);
                    if (left) break;
                }

                if (left) break;
                if (range.Finished)
                {
                    log($"{indent}  reached the end link: done");
                    break;
                }
                if (!step.Paginate) break;
                if (page >= step.MaxPages)
                {
                    log($"{indent}  reached the limit of {step.MaxPages} page(s): done");
                    break;
                }

                UnwindTo(ctx, depth);
                if (!await list.GoToNextPageAsync(step.NextPageText, ct))
                {
                    log($"{indent}  no further page: done");
                    break;
                }
                page++;
            }
            if (n == 0 && range.IsLimited) log($"{indent}  no link was inside the start/end limits");
        }
        finally
        {
            ctx.LoopDepths.Pop();
            LeaveLoop(ctx, depth, left || ct.IsCancellationRequested);
            ctx.Vars.Clear();
            foreach (var (key, value) in outer) ctx.Vars[key] = value;
        }
    }

    /// <summary>
    /// Opens <paramref name="href"/> in a new tab and runs the loop's children there; with "continue on error" a failure only skips this link.
    /// True if a "stop repeating" step asked to leave the loop.
    /// </summary>
    async Task<bool> RunLinkAsync(Step step, Context ctx, string href, string indent, CancellationToken ct)
    {
        try
        {
            var tab = await OpenAsync(href, ct);
            Push(ctx, tab);
            await RunStepsAsync(step.Children, ctx, indent + "    ", ct);
        }
        catch (LoopExit)
        {
            log($"{indent}  left the loop");
            return true;
        }
        catch (Exception ex) when (step.ContinueOnError && ex is StepFailedException or TimeoutException)
        {
            log($"{indent}    ✗ {ex.Message}");
        }
        return false;
    }

    /// <summary>
    /// Clicks every element that matches (the first, second, ... one, found again before each click so a changing page does no harm)
    /// and runs the children after each click on the same tab; "stop repeating" inside leaves the loop.
    /// </summary>
    async Task ForEachElementAsync(Step step, Context ctx, TimeSpan timeout, string indent, CancellationToken ct)
    {
        var tab = ctx.Current;
        var query = step.ToQuery(ctx.Expand);
        var matches = await PollAsync(async () => (await tab.FindAsync(query, true)) is { Count: > 0 } m ? m : null, timeout, "matching elements", ct);
        log($"{indent}  {matches.Count} element(s)");

        var until = step.ToUntilQuery(ctx.Expand);
        var untilWait = TimeSpan.FromMilliseconds(step.UntilWaitMs);
        var depth = ctx.Stack.Count;
        var outer = ctx.Vars.ToDictionary(v => v.Key, v => v.Value);
        var left = false;   // a "stop repeating" step ended the loop: the page it is on stays open
        ctx.LoopDepths.Push(depth);
        try
        {
            if (until is not null && await IsFoundAsync(tab, until, TimeSpan.Zero, ct))
            {
                log($"{indent}  \"{step.UntilText}\" is already there: nothing to repeat");
                return;
            }
            for (var i = 0; i < matches.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                UnwindTo(ctx, depth);
                ctx.Vars["text"] = matches[i].Text;
                ctx.Vars["href"] = matches[i].Href;
                ctx.Vars["index"] = (i + 1).ToString();
                log($"{indent}  [{i + 1}/{matches.Count}] {matches[i].Text}");
                try
                {
                    if (step.ClickEachMatch) await ClickAsync(ClickOf(step, i), ctx, timeout, ct);
                    await RunStepsAsync(step.Children, ctx, indent + "    ", ct);
                }
                catch (LoopExit)
                {
                    log($"{indent}  left the loop");
                    left = true;
                    break;
                }
                catch (Exception ex) when (step.ContinueOnError && ex is StepFailedException or TimeoutException)
                {
                    log($"{indent}    ✗ {ex.Message}");
                }

                if (until is not null && await IsFoundAsync(tab, until, untilWait, ct))
                {
                    log($"{indent}  \"{step.UntilText}\" appeared: leaving the loop");
                    break;
                }
            }
        }
        finally
        {
            ctx.LoopDepths.Pop();
            LeaveLoop(ctx, depth, left || ct.IsCancellationRequested);
            ctx.Vars.Clear();
            foreach (var (key, value) in outer) ctx.Vars[key] = value;
        }
    }

    /// <summary>
    /// Ends a loop: closes the tabs it opened - except after "stop repeating", where the page the run has arrived on stays and the run goes on there,
    /// and when the user stopped the run, which leaves the page it was working on open to look at.
    /// </summary>
    void LeaveLoop(Context ctx, int depth, bool keepPages)
    {
        if (keepPages) host.Select(ctx.Current);
        else UnwindTo(ctx, depth);
    }

    /// <summary>The click of round <paramref name="index"/> of an element loop: a click step with the loop's search and window settings on the index-th match.</summary>
    static Step ClickOf(Step loop, int index) => new()
    {
        Type = StepType.Click, Target = loop.Target, Selector = loop.Selector, Text = loop.Text, MatchMode = loop.MatchMode,
        MatchCase = loop.MatchCase, NearText = loop.NearText, TimeoutMs = loop.TimeoutMs, MatchIndex = index,
        NewTab = loop.NewTab, CloseNewWindow = loop.CloseNewWindow, RepeatUntilNoWindow = loop.RepeatUntilNoWindow, HumanMouse = loop.HumanMouse,
    };

    /// <summary>Opens every address of the step's hand-written list in its own tab and runs the children there.</summary>
    async Task ForEachListedAsync(Step step, Context ctx, string indent, CancellationToken ct)
    {
        var (urls, ignored) = LinkList.Parse(step.Links);
        foreach (var line in ignored) log($"{indent}  ignored (not a web address): {line}");
        if (urls.Count == 0) throw new StepFailedException("The link list has no web address");
        log($"{indent}  {urls.Count} link(s) to process");

        var depth = ctx.Stack.Count;
        var outer = ctx.Vars.ToDictionary(v => v.Key, v => v.Value);
        var left = false;
        ctx.LoopDepths.Push(depth);
        try
        {
            var n = 0;
            foreach (var url in urls)
            {
                ct.ThrowIfCancellationRequested();
                UnwindTo(ctx, depth);
                ctx.Vars["text"] = url;
                ctx.Vars["href"] = url;
                log($"{indent}  [{++n}/{urls.Count}] {url}");
                if (left = await RunLinkAsync(step, ctx, url, indent, ct)) break;
            }
        }
        finally
        {
            ctx.LoopDepths.Pop();
            LeaveLoop(ctx, depth, left || ct.IsCancellationRequested);
            ctx.Vars.Clear();
            foreach (var (key, value) in outer) ctx.Vars[key] = value;
        }
    }

    /// <summary>The matching links with an address that are not in <paramref name="seen"/> yet; null if none show up (a missing first page is an error).</summary>
    static async Task<List<LinkMatch>?> NewLinksAsync(BrowserTab list, ElementQuery query, HashSet<string> seen, TimeSpan timeout, bool required, CancellationToken ct)
    {
        try
        {
            return await PollAsync(async () =>
                (await list.FindAsync(query, false)).Where(l => l.Href.Length > 0 && !seen.Contains(l.Href)).DistinctBy(l => l.Href).ToList() is { Count: > 0 } m ? m : null,
                timeout, "matching links with a URL", ct);
        }
        catch (StepFailedException) when (!required)
        {
            return null;
        }
    }

    /// <summary>True if a visible match shows up within <paramref name="timeout"/>.</summary>
    static async Task<bool> IsFoundAsync(BrowserTab tab, ElementQuery query, TimeSpan timeout, CancellationToken ct)
    {
        var end = DateTime.UtcNow + timeout;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if ((await tab.FindAsync(query, true)).Count > 0) return true;
            if (DateTime.UtcNow >= end) return false;
            await Task.Delay(250, ct);
        }
    }

    async Task<BrowserTab> OpenAsync(string url, CancellationToken ct)
    {
        var tab = await host.OpenTabAsync();
        await tab.NavigateAsync(url, ct);
        return tab;
    }

    void Push(Context ctx, BrowserTab tab)
    {
        ctx.Stack.Add(tab);
        host.Select(tab);
    }

    void UnwindTo(Context ctx, int depth)
    {
        while (ctx.Stack.Count > depth)
        {
            host.Close(ctx.Stack[^1]);
            ctx.Stack.RemoveAt(ctx.Stack.Count - 1);
        }
        host.Select(ctx.Current);
    }

    static async Task<T> PollAsync<T>(Func<Task<T?>> attempt, TimeSpan timeout, string what, CancellationToken ct) where T : class
    {
        var end = DateTime.UtcNow + timeout;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (await attempt() is { } value) return value;
            if (DateTime.UtcNow >= end) throw new StepFailedException($"Timed out after {timeout.TotalMilliseconds:0} ms waiting for {what}");
            await Task.Delay(250, ct);
        }
    }
}
