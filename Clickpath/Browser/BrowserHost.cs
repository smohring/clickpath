using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Clickpath.Localization;
using Microsoft.Web.WebView2.Core;

namespace Clickpath.Browser;

/// <summary>Owns the tab control and the shared WebView2 environment (one profile, so logins persist).</summary>
public sealed class BrowserHost(TabControl tabs)
{
    static readonly string[] ContainerExtensions = [".dlc", ".ccf", ".rsdf"];

    readonly Dictionary<BrowserTab, TabItem> _items = new();
    CoreWebView2Environment? _env;

    /// <summary>The tab shown now, or null if there is none or it is already closed.</summary>
    public BrowserTab? Selected => (tabs.SelectedItem as TabItem)?.Tag is BrowserTab { IsClosed: false } tab ? tab : null;

    /// <summary>Raised when the selected tab changes or any tab's URL/title changes.</summary>
    public event Action<BrowserTab>? TabChanged;
    public event Action<BrowserTab>? PageLoaded;
    public event Action<PickedElement>? Picked;
    public event Action<BrowserTab, RecordedClick>? Clicked;

    /// <summary>Raised when the user picks "add to link list" in the context menu of a link.</summary>
    public event Action<string>? LinkToList;

    /// <summary>Unattended runs cancel hand-overs to other apps (nobody is there to answer the system prompt).</summary>
    public bool CancelExternalLaunches { get; set; }

    /// <summary>Folder whose sub-folders each hold one unpacked browser extension (manifest.json inside): uBlock Origin, downloaded by <see cref="UBlockInstaller"/>.</summary>
    public static string ExtensionsFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clickpath", "extensions");

    /// <summary>The page zoom of the whole browser (1 = 100 %): every tab follows it, also after a navigation, and new tabs start with it.</summary>
    public double Zoom { get; private set; } = 1;

    public event Action? ZoomChanged;

    /// <summary>Sets the zoom of all tabs; Ctrl+plus/minus/wheel in any tab end up here too (see <see cref="OnTabZoom"/>).</summary>
    public void SetZoom(double factor)
    {
        Zoom = Math.Clamp(factor, 0.25, 5);
        foreach (var tab in _items.Keys) ApplyZoom(tab);
        ZoomChanged?.Invoke();
    }

    void ApplyZoom(BrowserTab tab)
    {
        if (Math.Abs(tab.ZoomFactor - Zoom) > 0.001) tab.ZoomFactor = Zoom;
    }

    /// <summary>A tab's zoom changed: while it loads (WebView2 resets it for a new site) the global value is put back, otherwise it is the user's new choice for all tabs.</summary>
    void OnTabZoom(BrowserTab tab)
    {
        if (tab.IsClosed || Math.Abs(tab.ZoomFactor - Zoom) < 0.001) return;
        if (tab.ZoomSettling) ApplyZoom(tab);
        else SetZoom(tab.ZoomFactor);
    }

    /// <summary>Whether the extensions are switched on; set before the first tab opens, changed later with <see cref="SetBlockAdsAsync"/>.</summary>
    public bool BlockAds { get; set; }

    Task<List<CoreWebView2BrowserExtension>>? _extensions;

    public async Task<BrowserTab> OpenTabAsync(string? url = null)
    {
        _env ??= await CoreWebView2Environment.CreateAsync(null, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clickpath", "WebView2"),
            new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true });

        var opening = Stopwatch.StartNew();
        var tab = new BrowserTab();
        var item = BuildItem(tab);
        _items[tab] = item;
        tabs.Items.Add(item);
        tabs.SelectedItem = item;

        await tab.View.EnsureCoreWebView2Async(_env);
        await tab.AttachAsync();
        ApplyZoom(tab);
        tab.Core.ContentLoading += (_, _) => ApplyZoom(tab);
        tab.ZoomChanged += OnTabZoom;
        await (_extensions ??= InstallExtensionsAsync(tab.Core.Profile));
        AppLog.Write($"tab opened in {opening.ElapsedMilliseconds} ms");
        tab.Core.NewWindowRequested += (_, e) => OnNewWindow(tab, e);
        tab.Core.LaunchingExternalUriScheme += (_, e) =>
        {
            AppLog.Write($"link handed to another app: {e.Uri}{(CancelExternalLaunches ? " (cancelled, unattended run)" : "")}");
            if (CancelExternalLaunches) e.Cancel = true;
            tab.RaiseHandedOver(e.Uri);
        };
        tab.Core.DownloadStarting += (_, e) => OnDownload(tab, e);
        tab.Core.WindowCloseRequested += (_, _) => Close(tab);
        tab.Core.ContextMenuRequested += (_, e) => AddLinkMenuItem(tab, e);
        tab.Changed += t => { UpdateHeader(t); TabChanged?.Invoke(t); };
        tab.PageLoaded += t => PageLoaded?.Invoke(t);
        tab.Picked += p => Picked?.Invoke(p);
        tab.Clicked += (t, c) => Clicked?.Invoke(t, c);

        if (url is not null) await tab.NavigateAsync(url, CancellationToken.None);
        return tab;
    }

    /// <summary>Downloads uBlock Origin first if it is missing (not in an unattended run with the blocker off), then adds every extension in <see cref="ExtensionsFolder"/> to the profile, each switched on or off by <see cref="BlockAds"/>.</summary>
    async Task<List<CoreWebView2BrowserExtension>> InstallExtensionsAsync(CoreWebView2Profile profile)
    {
        if (BlockAds || !CancelExternalLaunches) await UBlockInstaller.EnsureInstalledAsync();
        var installed = new List<CoreWebView2BrowserExtension>();
        var folders = Directory.Exists(ExtensionsFolder)
            ? Directory.GetDirectories(ExtensionsFolder).Where(d => File.Exists(Path.Combine(d, "manifest.json")))
            : [];
        foreach (var folder in folders)
        {
            try
            {
                var extension = await profile.AddBrowserExtensionAsync(folder);
                await extension.EnableAsync(BlockAds);
                installed.Add(extension);
                AppLog.Write($"extension {extension.Name} ({extension.Id}) loaded from {folder} ({(BlockAds ? "on" : "off")})");
            }
            catch (Exception ex) when (ex is COMException or ArgumentException or IOException)
            {
                AppLog.Write($"extension in {folder} could not be loaded: 0x{ex.HResult:X8} {ex.Message}");
            }
        }
        return installed;
    }

    /// <summary>The id of the first loaded extension (uBlock Origin), the host name of its pages; null if none is installed.</summary>
    public async Task<string?> ExtensionIdAsync() => _extensions is null ? null : (await _extensions).FirstOrDefault()?.Id;

    /// <summary>Switches the loaded extensions on or off for all tabs; returns how many there are (0 = nothing to switch).</summary>
    public async Task<int> SetBlockAdsAsync(bool on)
    {
        BlockAds = on;
        if (_extensions is null) return 0;
        var extensions = await _extensions;
        foreach (var extension in extensions) await extension.EnableAsync(on);
        AppLog.Write($"ad blocker {(on ? "on" : "off")} ({extensions.Count} extension(s))");
        return extensions.Count;
    }

    /// <summary>Puts "add to link list" at the top of the context menu of a web link.</summary>
    void AddLinkMenuItem(BrowserTab tab, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        if (!e.ContextMenuTarget.HasLinkUri || !BrowserTab.IsWebUri(e.ContextMenuTarget.LinkUri)) return;
        var url = e.ContextMenuTarget.LinkUri;
        var item = tab.Core.Environment.CreateContextMenuItem(Loc.T("Menu.AddToLinkList"), null, CoreWebView2ContextMenuItemKind.Command);
        item.CustomItemSelected += (_, _) => LinkToList?.Invoke(url);
        e.MenuItems.Insert(0, item);
        e.MenuItems.Insert(1, tab.Core.Environment.CreateContextMenuItem("", null, CoreWebView2ContextMenuItemKind.Separator));
    }

    public void Select(BrowserTab tab)
    {
        if (_items.TryGetValue(tab, out var item)) tabs.SelectedItem = item;
    }

    /// <summary>
    /// True while the WebView2 control is closing the main window because a page called window.close(); the control does that
    /// synchronously in its own handler, so the window can only recognise it by the call stack. The page's tab is closed instead.
    /// </summary>
    public static bool IsPageCloseRequest() =>
        new StackTrace().GetFrames().Any(f => f.GetMethod()?.Name == "CoreWebView2_WindowCloseRequested");

    /// <summary>Closes a tab; the last tab is replaced by a blank one so there is always something to work on.</summary>
    public void Close(BrowserTab tab)
    {
        if (!_items.Remove(tab, out var item)) return;
        var wasSelected = tabs.SelectedItem == item;
        tab.IsClosed = true;
        tabs.Items.Remove(item);
        tab.View.Dispose();
        if (tabs.Items.Count == 0) _ = OpenTabAsync("about:blank");
        else if (wasSelected) tabs.SelectedIndex = tabs.Items.Count - 1;
    }

    /// <summary>
    /// Closes every tab except <paramref name="keep"/>; with nothing to keep it first opens a fresh blank tab (so the last tab never
    /// closes). Returns the tab to continue on (the last kept one or the fresh one) and how many were closed.
    /// </summary>
    public async Task<(BrowserTab Current, int Closed)> CloseAllAsync(IReadOnlyList<BrowserTab> keep)
    {
        var current = keep.Count > 0 ? keep[^1] : await OpenTabAsync("about:blank");
        var old = _items.Keys.Where(t => t != current && !keep.Contains(t)).ToList();
        foreach (var tab in old) Close(tab);
        Select(current);
        return (current, old.Count);
    }

    /// <summary>Link container files (DLC etc.): download silently, then ask whether to open them in JDownloader.</summary>
    void OnDownload(BrowserTab tab, CoreWebView2DownloadStartingEventArgs e)
    {
        var path = e.ResultFilePath;
        var download = e.DownloadOperation;
        AppLog.Write($"download started: {download.Uri} -> {path}");
        tab.RaiseHandedOver(Path.GetFileName(path));
        if (!ContainerExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) return;

        e.Handled = true;
        download.StateChanged += (_, _) =>
        {
            if (download.State == CoreWebView2DownloadState.Completed) OfferToOpen(path);
        };
    }

    void OfferToOpen(string path)
    {
        if (CancelExternalLaunches)
        {
            AppLog.Write($"container file saved, not opened (unattended run): {path}");
            return;
        }

        var question = Loc.T("Download.Ask").Replace("{0}", Path.GetFileName(path));
        if (!ConfirmDialog.Ask(Application.Current.MainWindow, question, Loc.T("Dlg.Open"), Loc.T("Dlg.NotNow"))) return;
        try
        {
            // JDownloader hands a file argument to the running instance; works even without a Windows file association
            var jd = FindJDownloader();
            Process.Start(jd is null
                ? new ProcessStartInfo(path) { UseShellExecute = true }
                : new ProcessStartInfo(jd, $"\"{path}\"") { UseShellExecute = false });
            AppLog.Write($"opened {path} with {jd ?? "the registered app"}");
        }
        catch (Exception ex) when (ex is Win32Exception or IOException) { AppLog.Write($"could not open {path}: {ex.Message}"); }
    }

    /// <summary>Path of JDownloader2.exe: from the running process, else the default per-user install.</summary>
    static string? FindJDownloader()
    {
        foreach (var process in Process.GetProcessesByName("JDownloader2"))
        {
            try { if (process.MainModule?.FileName is { } running) return running; }
            catch (Win32Exception) { }
        }
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JDownloader 2.0", "JDownloader2.exe");
        return File.Exists(installed) ? installed : null;
    }

    async void OnNewWindow(BrowserTab opener, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (!BrowserTab.IsWebUri(e.Uri))
        {
            // e.g. "Open with JDownloader": no tab to show, let the opener hand the link to the other app
            e.Handled = true;
            opener.Core.Navigate(e.Uri);
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            var popup = await OpenTabAsync();
            popup.IsPopup = true;
            e.NewWindow = popup.Core;
            e.Handled = true;
            opener.RaisePopupOpened(popup);
        }
        finally { deferral.Complete(); }
    }

    TabItem BuildItem(BrowserTab tab)
    {
        var title = new TextBlock { Text = Loc.T("Tab.New"), MaxWidth = 160, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var close = new Button { Content = "", FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10, Padding = new Thickness(5), Margin = new Thickness(8, 0, 0, 0), Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0) };
        close.Click += (_, _) => Close(tab);
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(title);
        header.Children.Add(close);
        return new TabItem { Header = header, Content = tab.View, Tag = tab };
    }

    void UpdateHeader(BrowserTab tab)
    {
        if (!_items.TryGetValue(tab, out var item)) return;
        var text = string.IsNullOrWhiteSpace(tab.Title) ? tab.Url : tab.Title;
        ((TextBlock)((StackPanel)item.Header).Children[0]).Text = string.IsNullOrEmpty(text) ? Loc.T("Tab.New") : text;
    }
}
