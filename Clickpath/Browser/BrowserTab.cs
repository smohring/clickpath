using System.Text.Json;
using Clickpath.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Clickpath.Browser;

public sealed record LinkMatch(string Text, string Href);
public sealed record PickedElement(string Selector, string Text, string Href);
public sealed record RecordedClick(string Selector, string Text, bool IsLink, string Href);

public sealed class StepFailedException(string message) : Exception(message);

/// <summary>One browser tab: a WebView2 plus the page operations the step engine needs.</summary>
public sealed class BrowserTab
{
    static readonly Random Rng = new();
    bool _navigating;
    readonly System.Diagnostics.Stopwatch _loadTimer = new();
    MousePath.Point? _pointer;   // where the pointer last was in this tab

    public WebView2 View { get; } = new();
    public bool IsClosed { get; internal set; }

    public BrowserTab() => View.ZoomFactorChanged += (_, _) => ZoomChanged?.Invoke(this);

    /// <summary>The page zoom changed: by the user (Ctrl+plus/minus/wheel), by <see cref="ZoomFactor"/>, or reset by WebView2 for a new site.</summary>
    public event Action<BrowserTab>? ZoomChanged;

    long _settleUntil;

    /// <summary>True while a page is loading and shortly after: WebView2 resets the zoom for a new site then, which must not count as the user's choice.</summary>
    public bool ZoomSettling => _navigating || Environment.TickCount64 < _settleUntil;

    /// <summary>Page zoom of this tab (1 = 100 %); the control keeps it per tab, Ctrl+plus/minus/wheel change it as well.</summary>
    public double ZoomFactor
    {
        get => IsClosed || View.CoreWebView2 is null ? 1 : View.ZoomFactor;
        set { if (!IsClosed && View.CoreWebView2 is not null) View.ZoomFactor = value; }
    }

    /// <summary>Opened by a page (window.open) rather than by the user.</summary>
    public bool IsPopup { get; internal set; }
    public CoreWebView2 Core => IsClosed ? throw new StepFailedException("The tab was closed") : View.CoreWebView2;
    public string Url => IsClosed ? "" : View.CoreWebView2?.Source ?? "";
    public string Title => IsClosed ? "" : View.CoreWebView2?.DocumentTitle ?? "";
    public bool CanGoBack => !IsClosed && View.CoreWebView2?.CanGoBack == true;
    public bool CanGoForward => !IsClosed && View.CoreWebView2?.CanGoForward == true;

    public event Action<BrowserTab>? Changed;
    /// <summary>A page finished loading successfully.</summary>
    public event Action<BrowserTab>? PageLoaded;
    public event Action<BrowserTab, BrowserTab>? PopupOpened;
    public event Action<PickedElement>? Picked;
    public event Action<BrowserTab, RecordedClick>? Clicked;
    /// <summary>Something was handed over instead of shown: a link for another app, or a file download.</summary>
    public event Action<BrowserTab, string>? HandedOver;

    /// <summary>The tab starts loading a different page (not just another fragment of the current one).</summary>
    public event Action<BrowserTab>? NavigationBegan;

    internal void RaisePopupOpened(BrowserTab popup) => PopupOpened?.Invoke(this, popup);

    /// <summary>True if navigating from <paramref name="from"/> to <paramref name="to"/> loads another page, i.e. more than the #fragment differs.</summary>
    public static bool IsNewPage(string from, string to) => WithoutFragment(from) != WithoutFragment(to);

    static string WithoutFragment(string url) => url.Split('#')[0];
    internal void RaiseHandedOver(string what) => HandedOver?.Invoke(this, what);

    /// <summary>True for pages WebView2 shows itself; anything else (jd://, mailto:, ...) is handed to another app.</summary>
    public static bool IsWebUri(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" or "file" or "about" or "blob" or "data";

    internal async Task AttachAsync()
    {
        await Core.AddScriptToExecuteOnDocumentCreatedAsync(PageScripts.ClickRecorder);
        Core.ProcessFailed += (_, e) => AppLog.Write($"WebView2 process failed: {e.ProcessFailedKind}, reason {e.Reason}, exit code {e.ExitCode}, page {Url}");
        Core.NavigationStarting += (_, e) =>
        {
            if (!IsWebUri(e.Uri)) return;
            var newPage = IsNewPage(Url, e.Uri);
            _navigating = true;
            _loadTimer.Restart();
            if (newPage) NavigationBegan?.Invoke(this);
        };
        Core.NavigationCompleted += (_, e) =>
        {
            _navigating = false;
            _settleUntil = Environment.TickCount64 + 800;
            AppLog.Write($"page load {_loadTimer.ElapsedMilliseconds} ms ({(e.IsSuccess ? "ok" : e.WebErrorStatus.ToString())}): {Url}");
            Changed?.Invoke(this);
            if (e.IsSuccess) PageLoaded?.Invoke(this);
        };
        Core.FrameCreated += (_, e) => TrackFrame(e.Frame);
        Core.SourceChanged += (_, _) => Changed?.Invoke(this);
        Core.DocumentTitleChanged += (_, _) => Changed?.Invoke(this);
        Core.WebMessageReceived += (_, e) => OnMessage(e.WebMessageAsJson);
    }

    void OnMessage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return;
        var selector = root.GetProperty("selector").GetString() ?? "";
        var text = root.GetProperty("text").GetString() ?? "";
        var href = root.GetProperty("href").GetString() ?? "";
        switch (root.GetProperty("kind").GetString())
        {
            case "pick": Picked?.Invoke(new PickedElement(selector, text, href)); break;
            case "click": Clicked?.Invoke(this, new RecordedClick(selector, text, root.GetProperty("isLink").GetBoolean(), href)); break;
        }
    }

    public static string NormalizeUrl(string url)
    {
        url = url.Trim();
        return url.Contains("://") || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ? url : "https://" + url;
    }

    public async Task NavigateAsync(string url, CancellationToken ct, int timeoutSeconds = 60)
    {
        var done = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        ulong? id = null;
        EventHandler<CoreWebView2NavigationStartingEventArgs> started = (_, e) => id ??= e.NavigationId;
        EventHandler<CoreWebView2NavigationCompletedEventArgs> completed = (_, e) => { if (id == e.NavigationId) done.TrySetResult(e); };
        var core = Core;
        core.NavigationStarting += started;
        core.NavigationCompleted += completed;
        try
        {
            core.Navigate(NormalizeUrl(url));
            var result = await done.Task.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds), ct);
            if (!result.IsSuccess) throw new StepFailedException($"Navigation to {url} failed: {result.WebErrorStatus}");
        }
        catch (TimeoutException) { throw new StepFailedException($"Navigation to {url} did not finish within {timeoutSeconds}s"); }
        finally
        {
            if (!IsClosed)
            {
                core.NavigationStarting -= started;
                core.NavigationCompleted -= completed;
            }
        }
    }

    /// <summary>After an action: give a triggered navigation time to start, then wait for it to finish.</summary>
    public async Task SettleAsync(CancellationToken ct, int timeoutSeconds = 60)
    {
        await Task.Delay(400, ct);
        var end = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (_navigating && DateTime.UtcNow < end) await Task.Delay(100, ct);
    }

    /// <summary>For a freshly opened popup: wait until it has left about:blank (up to 10 s), then settle.</summary>
    public async Task SettlePopupAsync(CancellationToken ct)
    {
        var end = DateTime.UtcNow.AddSeconds(10);
        while (Url is "" or "about:blank" && DateTime.UtcNow < end) await Task.Delay(100, ct);
        await SettleAsync(ct);
    }

    public async Task<JsonElement> EvalAsync(string script)
    {
        var json = await Core.ExecuteScriptAsync(script);
        using var doc = JsonDocument.Parse(string.IsNullOrEmpty(json) ? "null" : json);
        return doc.RootElement.Clone();
    }

    /// <summary>The page as it looks now, or null while it cannot be read (e.g. in the middle of a navigation).</summary>
    /// <summary>One line on how the page is built (text length, elements, shadow roots, frames, start of the text); null if it cannot be read.</summary>
    public async Task<string?> DescribeAsync()
    {
        try
        {
            var r = await EvalAsync(PageScripts.Diagnose);
            if (r.ValueKind != JsonValueKind.Object) return null;
            var frames = r.GetProperty("frames").EnumerateArray().Select(f => f.GetString()).ToList();
            return $"{r.GetProperty("textLength").GetInt32()} characters of text, {r.GetProperty("elements").GetInt32()} elements, "
                 + $"{r.GetProperty("shadow").GetInt32()} shadow root(s), {frames.Count} frame(s){(frames.Count > 0 ? ": " + string.Join("; ", frames) : "")}; text starts: “{r.GetProperty("sample").GetString()}”";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or System.Runtime.InteropServices.COMException or StepFailedException)
        {
            return null;
        }
    }

    readonly List<CoreWebView2Frame> _frames = [];

    /// <summary>Remembers the frames of the page (also nested ones) so their text can be read; WebView2 allows it for every origin.</summary>
    void TrackFrame(CoreWebView2Frame frame)
    {
        _frames.Add(frame);
        frame.Destroyed += (_, _) => _frames.Remove(frame);
        frame.FrameCreated += (_, e) => TrackFrame(e.Frame);
    }

    /// <summary>The page's words and source length, including those of its visible frames (a captcha or a content box often lives in one).</summary>
    public async Task<PageSnapshot?> SnapshotAsync()
    {
        try
        {
            var result = await EvalAsync(PageScripts.Snapshot);
            if (result.ValueKind != JsonValueKind.Object) return null;
            var words = result.GetProperty("words").EnumerateArray().Select(w => w.GetInt32().ToString()).ToHashSet();
            var length = result.GetProperty("len").GetInt32();
            foreach (var frame in _frames.ToList())
            {
                try
                {
                    if (frame.IsDestroyed() != 0) continue;
                    using var inner = JsonDocument.Parse(await frame.ExecuteScriptAsync(PageScripts.FrameSnapshot));
                    if (inner.RootElement.ValueKind != JsonValueKind.Object) continue;
                    foreach (var w in inner.RootElement.GetProperty("words").EnumerateArray()) words.Add(w.GetInt32().ToString());
                    length += inner.RootElement.GetProperty("len").GetInt32();
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ObjectDisposedException or JsonException) { }
            }
            return new PageSnapshot(result.GetProperty("url").GetString() ?? "", words, length);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or System.Runtime.InteropServices.COMException or StepFailedException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<LinkMatch>> FindAsync(ElementQuery query, bool visibleOnly) =>
        ReadItems(await EvalAsync(PageScripts.Find(query, visibleOnly)));

    /// <summary>Outlines the matches on the page for a while and returns them (first = the one a click would use).</summary>
    public async Task<IReadOnlyList<LinkMatch>> HighlightAsync(ElementQuery query, bool visibleOnly, int durationMs) =>
        ReadItems(await EvalAsync(PageScripts.Highlight(query, visibleOnly, durationMs)));

    /// <summary>Clicks the first visible match with a real mouse event (so popups get a user gesture). False if nothing matched yet.</summary>
    public async Task<bool> ClickFirstAsync(ElementQuery query, bool human = false, CancellationToken ct = default)
    {
        var spot = await EvalAsync(PageScripts.Locate(query));
        ThrowOnScriptError(spot);
        if (spot.ValueKind != JsonValueKind.Object || !spot.GetProperty("found").GetBoolean()) return false;

        if (!spot.GetProperty("direct").GetBoolean())
            return (await EvalAsync(PageScripts.JsClick(query))).ValueKind == JsonValueKind.True;

        var centre = new MousePath.Point(spot.GetProperty("x").GetDouble(), spot.GetProperty("y").GetDouble());
        var target = human ? MousePath.ClickPoint(centre, spot.GetProperty("w").GetDouble(), spot.GetProperty("h").GetDouble(), Rng) : centre;
        await ClickAtAsync(target, human, ct);
        return true;
    }

    /// <summary>A real click at <paramref name="target"/>; with <paramref name="human"/> the pointer first walks there and the button is held briefly.</summary>
    async Task ClickAtAsync(MousePath.Point target, bool human, CancellationToken ct)
    {
        if (human)
        {
            var (width, height) = await ViewportAsync();
            foreach (var point in MousePath.Journey(_pointer ?? RandomSpot(width, height), target, width, height, Rng))
            {
                ct.ThrowIfCancellationRequested();
                await MouseAsync("mouseMoved", point.X, point.Y);
                await Task.Delay(Rng.Next(7, 15), ct);
            }
            await Task.Delay(Rng.Next(40, 160), ct);
        }
        await MouseAsync("mouseMoved", target.X, target.Y);
        await PressAsync(target, human ? Rng.Next(50, 130) : 0, ct);
    }

    /// <summary>Presses and releases the left button where the pointer is, holding it for <paramref name="holdMs"/>.</summary>
    async Task PressAsync(MousePath.Point at, int holdMs, CancellationToken ct)
    {
        await MouseAsync("mousePressed", at.X, at.Y);
        if (holdMs > 0) await Task.Delay(holdMs, ct);
        await MouseAsync("mouseReleased", at.X, at.Y);
    }

    async Task<MousePath.Point?> EmptySpotAsync()
    {
        var spot = await EvalAsync(PageScripts.EmptySpot);
        return spot.ValueKind == JsonValueKind.Object ? new MousePath.Point(spot.GetProperty("x").GetDouble(), spot.GetProperty("y").GetDouble()) : null;
    }

    /// <summary>
    /// Goes to the next page of a paginated list: navigates to the "next" link's address, or clicks the control if it has none
    /// (script-driven lists). False if there is no usable next control or it is disabled (the last page).
    /// </summary>
    public async Task<bool> GoToNextPageAsync(string customText, CancellationToken ct)
    {
        var next = await EvalAsync(PageScripts.NextPage(customText));
        if (next.ValueKind != JsonValueKind.Object || !next.GetProperty("found").GetBoolean() || next.GetProperty("disabled").GetBoolean()) return false;

        var href = next.GetProperty("href").GetString() ?? "";
        if (IsWebUri(href) && IsNewPage(Url, href))
        {
            await NavigateAsync(href, ct);
            return true;
        }

        var x = next.GetProperty("x").GetDouble();
        if (x < 0) return false;
        await ClickAtAsync(new MousePath.Point(x, next.GetProperty("y").GetDouble()), false, ct);
        await SettleAsync(ct);
        return true;
    }

    /// <summary>
    /// Wanders over the page for <paramref name="duration"/> like a hand: curved paths, pauses, from where the pointer last was.
    /// With <paramref name="clickEvery"/> it now and then walks to an empty spot and clicks there.
    /// </summary>
    public async Task MoveMouseRandomlyAsync(TimeSpan duration, TimeSpan? clickEvery, CancellationToken ct)
    {
        var (width, height) = await ViewportAsync();
        var here = _pointer is { } last
            ? new MousePath.Point(Math.Clamp(last.X, 0, width), Math.Clamp(last.Y, 0, height))
            : RandomSpot(width, height);
        var end = duration == Timeout.InfiniteTimeSpan ? DateTime.MaxValue : DateTime.UtcNow + duration;
        var nextClick = clickEvery is { } every ? DateTime.UtcNow + MousePath.ClickGap(every, Rng) : DateTime.MaxValue;
        while (DateTime.UtcNow < end)
        {
            var clickDue = DateTime.UtcNow >= nextClick;
            var empty = clickDue ? await EmptySpotAsync() : null;
            var target = empty ?? RandomSpot(width, height);
            foreach (var point in MousePath.Journey(here, target, width, height, Rng))
            {
                if (DateTime.UtcNow >= end) return;
                ct.ThrowIfCancellationRequested();
                await MouseAsync("mouseMoved", point.X, point.Y);
                await Task.Delay(Rng.Next(7, 15), ct);
            }
            here = target;
            if (clickDue)
            {
                AppLog.Write(empty is null ? "random click: no empty spot found" : $"random click at {target.X:F0},{target.Y:F0}");
                if (empty is not null) await PressAsync(target, Rng.Next(50, 130), ct);
                nextClick = DateTime.UtcNow + MousePath.ClickGap(clickEvery!.Value, Rng);
            }
            var dwell = TimeSpan.FromMilliseconds(Rng.NextDouble() < 0.15 ? Rng.Next(500, 1200) : Rng.Next(60, 350));
            await Task.Delay(TimeSpan.FromTicks(Math.Min(dwell.Ticks, Math.Max(0, (end - DateTime.UtcNow).Ticks))), ct);
        }
    }

    static MousePath.Point RandomSpot(double width, double height) =>
        new(width * (0.05 + 0.9 * Rng.NextDouble()), height * (0.05 + 0.9 * Rng.NextDouble()));

    /// <summary>Clicks the middle of the visible page with a real mouse event (a person: somewhere near it), optionally marking the spot first.</summary>
    public async Task ClickViewportCenterAsync(bool flash, bool human, CancellationToken ct)
    {
        var (width, height) = await ViewportAsync();
        var middle = new MousePath.Point(width / 2, height / 2);
        var target = human ? MousePath.ClickPoint(middle, width / 2, height / 2, Rng) : middle;
        if (flash)
        {
            await EvalAsync(PageScripts.FlashPoint(target.X, target.Y, 900));
            await Task.Delay(700, ct);
        }
        await ClickAtAsync(target, human, ct);
    }

    async Task<(double Width, double Height)> ViewportAsync()
    {
        var size = await EvalAsync(PageScripts.ViewportSize);
        return size.ValueKind == JsonValueKind.Object ? (size.GetProperty("w").GetDouble(), size.GetProperty("h").GetDouble()) : (800, 600);
    }

    public Task StartPickAsync() => Core.ExecuteScriptAsync(PageScripts.Pick);

    Task MouseAsync(string type, double x, double y)
    {
        _pointer = new MousePath.Point(x, y);
        var p = JsonSerializer.Serialize(new
        {
            type, x, y, button = type == "mouseMoved" ? "none" : "left",
            buttons = type == "mousePressed" ? 1 : 0, clickCount = type == "mouseMoved" ? 0 : 1,
        });
        return Core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", p);
    }

    static IReadOnlyList<LinkMatch> ReadItems(JsonElement result)
    {
        ThrowOnScriptError(result);
        if (result.ValueKind != JsonValueKind.Object) return [];
        return result.GetProperty("items").EnumerateArray()
            .Select(i => new LinkMatch(i.GetProperty("text").GetString() ?? "", i.GetProperty("href").GetString() ?? ""))
            .ToList();
    }

    static void ThrowOnScriptError(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("error", out var err))
            throw new StepFailedException(err.GetString() ?? "Script error");
    }
}
