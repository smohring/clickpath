using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Clickpath.Browser;
using Clickpath.Engine;
using Clickpath.Localization;
using Clickpath.Models;
using Microsoft.Win32;

namespace Clickpath;

public partial class MainWindow : Window
{
    static readonly string AutosavePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clickpath", "autosave.json");

    static readonly string HistoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clickpath", "history.json");

    readonly StartupOptions _options;
    readonly HistoryStore _history = new(HistoryPath);
    string _lastAutosave = "";
    readonly BrowserHost _host;
    readonly StepRunner _runner;
    readonly ObservableCollection<Step> _steps = new();
    readonly ObservableCollection<ExtractedItem> _results = new();
    readonly List<string> _logLines = new();
    CancellationTokenSource? _cts;
    readonly PlanEditor _editor;
    Step? _lastRecorded;
    Step? _lastLinkList;
    Place? _addPlace;
    bool _running, _recording;

    public MainWindow(StartupOptions options)
    {
        _options = options;
        InitializeComponent();
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);   // a small screen gets the window as wide as it can be
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        _host = new BrowserHost(BrowserTabs)
        {
            CancelExternalLaunches = options.Run,
            BlockAds = options.BlockAds ?? AppSettings.Load(AppSettings.DefaultPath).BlockAds,
        };
        _host.SetZoom(options.Run ? 1 : AppSettings.Load(AppSettings.DefaultPath).ZoomPercent / 100.0);   // unattended runs always see the page at 100 %
        _host.ZoomChanged += OnZoomChanged;
        ZoomText.Text = $"{Math.Round(_host.Zoom * 100)}%";
        AdBlockButton.IsChecked = _host.BlockAds;
        _runner = new StepRunner(_host, Log, item => _results.Add(item));
        _runner.StepStarted += OnStepStarted;
        _runner.StepFinished += OnStepFinished;
        _editor = new PlanEditor(_steps);
        StepTree.ItemsSource = _steps;
        ResultsList.ItemsSource = _results;
        _results.CollectionChanged += (_, _) => ResultsTab.Header = $"{Loc.T("Tab.Results")} ({_results.Count})";

        _host.TabChanged += tab =>
        {
            if (tab == _host.Selected) ShowAddress(tab);
            if (!tab.IsPopup && tab.Title.Length > 0) _history.UpdateTitle(tab.Url, tab.Title);
        };
        _host.PageLoaded += OnPageLoaded;
        _host.Picked += OnPicked;
        _host.Clicked += OnRecordedClick;
        _host.LinkToList += OnLinkToList;
        BrowserTabs.SelectionChanged += (_, e) =>
        {
            if (e.OriginalSource == BrowserTabs && _host.Selected is { } tab) ShowAddress(tab);
        };
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _host.OpenTabAsync("about:blank");
            if (_options.PlanPath is not null) LoadPlan(_options.PlanPath);
            else if (File.Exists(AutosavePath)) LoadPlan(AutosavePath);
            if (_options.Run) await RunHeadlessAsync();
            else StartAutosave();
        }
        catch (Exception ex)
        {
            Log($"Startup failed: {ex.Message}");
            if (!_options.Run) return;
            if (_options.LogPath is not null) File.WriteAllLines(_options.LogPath, _logLines);
            Application.Current.Shutdown(2);
        }
    }

    void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (BrowserHost.IsPageCloseRequest())
        {
            e.Cancel = true;
            AppLog.Write("a page called window.close(); closing its tab, not the app");
            return;
        }
        _cts?.Cancel();
        if (!_options.Run) Autosave();
    }

    /// <summary>Keeps the current plan in %LocalAppData% so it is there again at the next start.</summary>
    void StartAutosave()
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        timer.Tick += (_, _) => Autosave();
        timer.Start();
    }

    void Autosave()
    {
        try
        {
            var json = PlanFile.ToJson(_steps);
            if (json == _lastAutosave) return;
            Directory.CreateDirectory(Path.GetDirectoryName(AutosavePath)!);
            File.WriteAllText(AutosavePath, json);
            _lastAutosave = json;
        }
        catch (IOException) { }
    }

    /// <summary>Called for exceptions nobody handled; the app keeps running and the details go to the crash log.</summary>
    public void ReportError(Exception ex) => Log($"Unexpected error: {ex.Message} (details in {AppLog.FilePath})");

    // ---- Browser controls ----

    void ShowAddress(BrowserTab tab)
    {
        if (!AddressBox.IsKeyboardFocusWithin) AddressBox.Text = tab.Url;
        BackButton.IsEnabled = tab.CanGoBack;
        ForwardButton.IsEnabled = tab.CanGoForward;
    }

    /// <summary>The zoom percentage next to the address field follows the browser-wide zoom; a change made by the user is remembered for the next start.</summary>
    void OnZoomChanged()
    {
        ZoomText.Text = $"{Math.Round(_host.Zoom * 100)}%";
        if (_options.Run) return;
        var settings = AppSettings.Load(AppSettings.DefaultPath);
        settings.ZoomPercent = (int)Math.Round(_host.Zoom * 100);
        AppSettings.Save(AppSettings.DefaultPath, settings);
    }

    static readonly int[] ZoomSteps = [50, 67, 75, 80, 90, 100, 110, 125, 150, 175, 200, 250, 300];

    /// <summary>The zoom percentage next to the address field: a click lists the levels for the shown tab (Ctrl+plus/minus/wheel work in the page itself).</summary>
    void OnZoomMenu(object sender, RoutedEventArgs e)
    {
        var current = (int)Math.Round(_host.Zoom * 100);
        var menu = new ContextMenu();
        foreach (var percent in ZoomSteps.Union([current]).Order())
        {
            var item = new MenuItem { Header = $"{percent}%", IsCheckable = true, IsChecked = percent == current };
            item.Click += (_, _) => _host.SetZoom(percent / 100.0);
            menu.Items.Add(item);
        }
        OpenUnder(menu, ZoomButton);
    }

    void OnBack(object sender, RoutedEventArgs e) => _host.Selected?.Core.GoBack();
    void OnForward(object sender, RoutedEventArgs e) => _host.Selected?.Core.GoForward();
    void OnReload(object sender, RoutedEventArgs e) => _host.Selected?.Core.Reload();
    void OnGo(object sender, RoutedEventArgs e) => _host.Selected?.Core.Navigate(BrowserTab.NormalizeUrl(AddressBox.Text));
    async void OnNewTab(object sender, RoutedEventArgs e)
    {
        await _host.OpenTabAsync("about:blank");
        AddressBox.Focus();
        AddressBox.SelectAll();
    }

    void OnAddressKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnGo(sender, e);
    }

    // ---- Address field: like a browser's - a click selects everything, typing completes from the history

    bool _completing, _deleting;

    /// <summary>A click into the field selects its whole text, so typing replaces it; once it has focus, clicks place the caret as usual.</summary>
    void OnAddressMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (AddressBox.IsKeyboardFocusWithin) return;
        AddressBox.Focus();
        AddressBox.SelectAll();
        e.Handled = true;
    }

    void OnAddressFocus(object sender, KeyboardFocusChangedEventArgs e) => AddressBox.SelectAll();

    /// <summary>Remembers whether the key deletes (then nothing is completed) and lets Esc put the page's address back.</summary>
    void OnAddressPreviewKeyDown(object sender, KeyEventArgs e)
    {
        _deleting = e.Key is Key.Back or Key.Delete;
        if (e.Key != Key.Escape || _host.Selected is not { } tab) return;
        AddressBox.Text = tab.Url;
        AddressBox.SelectAll();
        e.Handled = true;
    }

    /// <summary>While typing at the end of the text, the rest of the first matching visited address is filled in and selected (typing on replaces it).</summary>
    void OnAddressTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_completing || _deleting || !AddressBox.IsKeyboardFocusWithin || AddressBox.SelectionLength > 0 || AddressBox.CaretIndex != AddressBox.Text.Length) return;
        var typed = AddressBox.Text;
        if (AddressCompletion.Complete(typed, _history.Entries.Select(h => h.Url)) is not { } completed) return;
        _completing = true;
        AddressBox.Text = completed;
        AddressBox.Select(typed.Length, completed.Length - typed.Length);
        _completing = false;
    }

    // ---- Language ----

    /// <summary>Saves the choice; the texts of the open window are built once, so the app offers to restart (the plan is autosaved).</summary>
    void ChangeLanguage(Localization.Language language)
    {
        if (language == Loc.Current) return;
        var settings = AppSettings.Load(AppSettings.DefaultPath);
        settings.Language = language;
        AppSettings.Save(AppSettings.DefaultPath, settings);
        if (!ConfirmDialog.Ask(this, Loc.T("Lang.Restart"), Loc.T("Dlg.RestartNow"), Loc.T("Dlg.Later"))) return;
        Autosave();
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false });
        Application.Current.Shutdown();
    }

    /// <summary>Switches the ad blocker (uBlock Origin in the extensions folder) on or off and remembers the choice.</summary>
    async void OnAdBlockToggle(object sender, RoutedEventArgs e)
    {
        var on = AdBlockButton.IsChecked == true;
        var settings = AppSettings.Load(AppSettings.DefaultPath);
        settings.BlockAds = on;
        AppSettings.Save(AppSettings.DefaultPath, settings);
        var count = await _host.SetBlockAdsAsync(on);
        Log(count == 0
            ? $"Ad blocker {(on ? "on" : "off")}, but uBlock Origin is not installed - it is downloaded at the next start (needs internet access; see app.log), or unpack it into {BrowserHost.ExtensionsFolder}"
            : $"Ad blocker {(on ? "on" : "off")} ({count} extension(s))");
    }

    /// <summary>Right click on the shield: uBlock's own pages (settings, filter lists, ...) in a new tab; they only load while the blocker is on.</summary>
    async void OnAdBlockMenu(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var menu = new ContextMenu();
        foreach (var item in await AdBlockItemsAsync()) menu.Items.Add(item);
        OpenUnder(menu, AdBlockButton);
    }

    /// <summary>The entries for uBlock's pages, or one disabled entry saying why there are none.</summary>
    async Task<List<MenuItem>> AdBlockItemsAsync()
    {
        var id = await _host.ExtensionIdAsync();
        if (id is null || !_host.BlockAds) return [new MenuItem { Header = Loc.T(id is null ? "Menu.UbMissing" : "Menu.UbOff"), IsEnabled = false }];
        var items = new List<MenuItem>();
        foreach (var (key, page) in new[] { ("Menu.UbSettings", "dashboard.html#settings.html"), ("Menu.UbLists", "dashboard.html#3p-filters.html"),
                     ("Menu.UbMyFilters", "dashboard.html#1p-filters.html"), ("Menu.UbTrusted", "dashboard.html#whitelist.html"), ("Menu.UbLogger", "logger-ui.html") })
        {
            var item = new MenuItem { Header = Loc.T(key) };
            item.Click += async (_, _) => await _host.OpenTabAsync($"chrome-extension://{id}/{page}");
            items.Add(item);
        }
        return items;
    }

    /// <summary>The application menu ("..." at the right end of the address bar): language, ad blocker pages, history settings, data folder.</summary>
    async void OnAppMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();

        var language = new MenuItem { Header = Loc.T("Tip.Language") };
        foreach (var (lang, label) in new[] { (Localization.Language.Auto, Loc.T("Lang.Auto")), (Localization.Language.English, "English"), (Localization.Language.German, "Deutsch") })
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = lang == Loc.Current };
            item.Click += (_, _) => ChangeLanguage(lang);
            language.Items.Add(item);
        }
        menu.Items.Add(language);

        var adBlock = new MenuItem { Header = Loc.T("Menu.AdBlock") };
        foreach (var item in await AdBlockItemsAsync()) adBlock.Items.Add(item);
        menu.Items.Add(adBlock);

        var history = new MenuItem { Header = Loc.T("Menu.History") };
        foreach (var size in HistorySizes.Union([_history.Capacity]).Order())
        {
            var item = new MenuItem { Header = $"{Loc.T("History.Keep")} {size}", IsCheckable = true, IsChecked = size == _history.Capacity };
            item.Click += (_, _) => _history.Capacity = size;
            history.Items.Add(item);
        }
        history.Items.Add(new Separator());
        var runs = new MenuItem { Header = Loc.T("History.Runs"), IsCheckable = true, IsChecked = _history.IncludeRuns };
        runs.Click += (_, _) => _history.IncludeRuns = runs.IsChecked;
        history.Items.Add(runs);
        menu.Items.Add(history);

        menu.Items.Add(new Separator());
        var folder = new MenuItem { Header = Loc.T("Menu.DataFolder") };
        folder.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{Path.GetDirectoryName(AppSettings.DefaultPath)}\"") { UseShellExecute = true });
        menu.Items.Add(folder);

        OpenUnder(menu, AppMenuButton);
    }

    /// <summary>Opens a menu under a button at the right end of the address bar, with both right edges lined up so it stays inside the window.</summary>
    static void OpenUnder(ContextMenu menu, FrameworkElement button)
    {
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.Opened += (_, _) => menu.HorizontalOffset = button.ActualWidth - menu.ActualWidth;
        menu.IsOpen = true;
    }

    // ---- History ----

    static readonly int[] HistorySizes = [20, 40, 100, 200];

    void OnPageLoaded(BrowserTab tab)
    {
        if (tab.IsPopup || _options.Run || (_running && !_history.IncludeRuns)) return;
        _history.Record(tab.Url, tab.Title);
    }

    long _historyClosedAt;

    /// <summary>The popup closes itself on the mouse-down of the history button; remember when, so the click that follows does not open it again.</summary>
    void OnHistoryClosed(object? sender, EventArgs e) => _historyClosedAt = Environment.TickCount64;

    /// <summary>Opens the history flush under the address field like an unfolded address bar: as wide as the field, 40 % of the window high.</summary>
    void OnHistory(object sender, RoutedEventArgs e)
    {
        if (Environment.TickCount64 - _historyClosedAt < 300) return;
        HistoryFilter.Text = "";
        RefreshHistory();
        HistoryPanel.Width = AddressBox.ActualWidth;
        HistoryPanel.Height = Math.Max(220, ActualHeight * 0.4);
        HistoryPopup.IsOpen = true;
        HistoryFilter.Focus();
    }

    void OnHistoryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) HistoryPopup.IsOpen = false;
    }

    void RefreshHistory()
    {
        var shown = _history.Search(HistoryFilter.Text).ToList();
        HistoryList.ItemsSource = shown;
        HistoryEmpty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnHistoryFilter(object sender, TextChangedEventArgs e) => RefreshHistory();

    /// <summary>A click opens the page in the current tab, Ctrl+click in a new one.</summary>
    async void OnHistoryPick(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryList.SelectedItem is not HistoryEntry entry) return;
        var newTab = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        HistoryList.SelectedItem = null;
        HistoryPopup.IsOpen = false;
        if (newTab || _host.Selected is null) await _host.OpenTabAsync(entry.Url);
        else _host.Selected.Core.Navigate(entry.Url);
    }

    void OnClearHistory(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        RefreshHistory();
    }

    // ---- Step editing ----

    Step? SelectedStep => StepTree.SelectedItem as Step;

    void OnStepSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _addPlace = null;
        DetailCard.DataContext = e.NewValue;
        if (_syncingSelection)
        {
            RefreshDetail();
            return;
        }
        var step = e.NewValue as Step;
        SetSelection(step is null ? [] : [step]);
        if (step is not null) _anchor = step;
    }

    /// <summary>Where "add step" puts the new step: the user's choice from the menu, else the default for the selected step.</summary>
    Place EffectivePlace(Step? selected) =>
        _addPlace is { } chosen && (chosen != Place.Inside || selected is { CanHaveChildren: true }) ? chosen : PlanEditor.DefaultPlace(selected);

    void OnAddStepMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AddStepButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        if (SelectedStep is { } anchor)
        {
            var radios = new List<MenuItem>();
            foreach (var (place, key) in new[] { (Place.Before, "Add.Before"), (Place.Inside, "Add.Inside"), (Place.After, "Add.After") })
            {
                if (place == Place.Inside && !anchor.CanHaveChildren) continue;
                var radio = new MenuItem { Header = Loc.T(key), IsCheckable = true, IsChecked = place == EffectivePlace(anchor), StaysOpenOnClick = true };
                radio.Click += (_, _) =>
                {
                    _addPlace = place;
                    radios.ForEach(r => r.IsChecked = r == radio);
                };
                radios.Add(radio);
                menu.Items.Add(radio);
            }
            menu.Items.Add(new Separator());
        }

        foreach (var type in Enum.GetValues<StepType>())
        {
            var step = Step.Create(type);
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = step.Title, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = Loc.T("Desc." + type), FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 });
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(new TextBlock { Text = step.Icon, FontFamily = (System.Windows.Media.FontFamily)FindResource("IconFont"), FontSize = 18, Width = 34, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(text);
            var item = new MenuItem { Header = row };
            item.Click += (_, _) => AddStep(Step.Create(type), SelectedStep, EffectivePlace(SelectedStep));
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    void AddStep(Step step, Step? anchor, Place place)
    {
        _editor.Add(step, anchor, place);
        SelectInTree(step);
    }

    // ---- Recording ----

    void OnRecordToggle(object sender, RoutedEventArgs e)
    {
        _recording = RecordButton.IsChecked == true;
        RecordHint.Visibility = _recording ? Visibility.Visible : Visibility.Collapsed;
        if (!_recording) return;
        _lastRecorded = null;
        if (_steps.Count == 0 && _host.Selected is { } tab && tab.Url is not ("" or "about:blank"))
            _lastRecorded = AddRecorded(new Step { Type = StepType.Navigate, Url = tab.Url });
    }

    Step AddRecorded(Step step)
    {
        AddStep(step, _lastRecorded ?? SelectedStep, Place.Inside);
        return step;
    }

    void OnRecordedClick(BrowserTab tab, RecordedClick click)
    {
        if (!_recording || _running) return;
        var step = Step.Create(StepType.Click);
        var text = click.Text.Length > 80 ? click.Text[..80] : click.Text;
        if (text.Length == 0)
        {
            step.Target = StepTarget.Css;
            step.Selector = click.Selector;
        }
        else
        {
            step.Target = click.IsLink ? StepTarget.Link : StepTarget.Clickable;
            step.Text = text;
        }
        _lastRecorded = AddRecorded(step);
        FlagNewTab(tab, step);
    }

    /// <summary>A click that opens a tab/window within a moment is recorded as "follow the new tab".</summary>
    static async void FlagNewTab(BrowserTab tab, Step step)
    {
        void OnPopup(BrowserTab _, BrowserTab __) => step.NewTab = true;
        tab.PopupOpened += OnPopup;
        await Task.Delay(1500);
        tab.PopupOpened -= OnPopup;
    }

    void OnDeleteStep(object sender, RoutedEventArgs e)
    {
        var roots = SelectedRoots;
        if (roots.Count == 0) return;
        if (roots.Count > 1 && !ConfirmDialog.Ask(this, string.Format(Loc.T("Steps.DeleteAsk"), CountSteps(roots)), Loc.T("Dlg.Delete"), Loc.T("Dlg.Cancel"), danger: true)) return;
        _editor.RemoveMany(roots);
        SetSelection([]);
    }

    void OnClearPlan(object sender, RoutedEventArgs e)
    {
        if (_steps.Count == 0) return;
        var question = Loc.T("Clear.Ask").Replace("{0}", CountSteps(_steps).ToString());
        if (!ConfirmDialog.Ask(this, question, Loc.T("Dlg.Delete"), Loc.T("Dlg.Cancel"), danger: true)) return;
        _steps.Clear();
        _lastRecorded = null;
    }

    // ---- Link list editor ("repeat for each link in my list") ----

    /// <summary>A double click selects the whole line (one link) instead of a single word of the address.</summary>
    void OnLinkListMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not TextBox box) return;
        var index = box.GetCharacterIndexFromPoint(e.GetPosition(box), snapToText: true);
        if (index < 0) return;
        var (start, length) = LinkList.LineAt(box.Text, index);
        box.Select(start, length);
        e.Handled = true;
    }

    void OnPasteLinks(object sender, RoutedEventArgs e)
    {
        if (SelectedStep is not { Type: StepType.ForEachListedLink } step) return;
        string text;
        try { text = Clipboard.ContainsText() ? Clipboard.GetText() : ""; }
        catch (System.Runtime.InteropServices.ExternalException) { text = ""; }   // another program holds the clipboard
        var found = LinkList.Parse(text).Urls;
        if (found.Count == 0) Log("The clipboard holds no web address");
        else step.Links = LinkList.Append(step.Links, found);
    }

    /// <summary>A link chosen in the browser's context menu goes into the selected link list (else the last used, else the first).</summary>
    void OnLinkToList(string url)
    {
        if (_editor.FindLinkList(SelectedStep, _lastLinkList) is not { } step)
        {
            Log("No link list in the plan - add a \"repeat for each link in my list\" step first");
            return;
        }
        _lastLinkList = step;
        var before = LinkList.Count(step.Links).Links;
        step.Links = LinkList.Append(step.Links, [url]);
        Log(LinkList.Count(step.Links).Links > before ? $"Added to the link list: {url}" : $"Already in the link list: {url}");
    }

    void OnLinksFromResults(object sender, RoutedEventArgs e)
    {
        if (SelectedStep is not { Type: StepType.ForEachListedLink } step) return;
        var found = _results.Select(r => r.Href).Where(h => h.Length > 0).ToList();
        if (found.Count == 0) Log("No links collected yet - use the \"Collect links\" step first");
        else step.Links = LinkList.Append(step.Links, found);
    }

    void OnTidyLinks(object sender, RoutedEventArgs e)
    {
        if (SelectedStep is { Type: StepType.ForEachListedLink } step) step.Links = LinkList.Tidy(step.Links);
    }

    void OnClearLinks(object sender, RoutedEventArgs e)
    {
        if (SelectedStep is not { Type: StepType.ForEachListedLink } step) return;
        var count = LinkList.Count(step.Links).Links;
        if (count > 0 && !ConfirmDialog.Ask(this, string.Format(Loc.T("Links.ClearAsk"), count), Loc.T("Dlg.Clear"), Loc.T("Dlg.Cancel"), danger: true)) return;
        step.Links = "";
    }

    static int CountSteps(IEnumerable<Step> steps) => steps.Sum(s => 1 + CountSteps(s.Children));

    void OnMoveUp(object sender, RoutedEventArgs e) => Restructure(steps => _editor.MoveMany(steps, -1));
    void OnMoveDown(object sender, RoutedEventArgs e) => Restructure(steps => _editor.MoveMany(steps, 1));
    void OnOutdent(object sender, RoutedEventArgs e) => Restructure(_editor.OutdentMany);
    void OnIndent(object sender, RoutedEventArgs e) => Restructure(_editor.IndentMany);

    /// <summary>Applies a structural edit to the selection (one or several steps) and keeps it selected.</summary>
    void Restructure(Func<IReadOnlyList<Step>, bool> edit)
    {
        var roots = SelectedRoots;
        if (roots.Count == 0 || !edit(roots)) return;
        if (roots.Count == 1) SelectInTree(roots[0]);
        else ReselectMany(roots);
    }

    void SelectInTree(Step step) => Dispatcher.BeginInvoke(() => SelectContainer(StepTree, step), System.Windows.Threading.DispatcherPriority.Loaded);

    static bool SelectContainer(ItemsControl parent, Step step)
    {
        foreach (var item in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem container) continue;
            if (item == step) { container.IsSelected = true; return true; }
            if (SelectContainer(container, step)) return true;
        }
        return false;
    }

    // ---- Picking and previewing ----

    async void OnPick(object sender, RoutedEventArgs e)
    {
        if (_host.Selected is not { } tab) return;
        try { await tab.StartPickAsync(); Log("Pick mode: click an element in the page (Esc cancels)."); }
        catch (Exception ex) { Log($"Pick failed: {ex.Message}"); }
    }

    void OnPicked(PickedElement picked)
    {
        if (SelectedStep is not { UsesMatch: true } step) return;
        var text = picked.Text.Length > 80 ? picked.Text[..80] : picked.Text;
        step.Selector = picked.Selector;
        if (text.Length == 0) step.Target = StepTarget.Css;
        else
        {
            step.Text = text;
            step.MatchMode = TextMatch.Contains;
        }
        Log($"Picked \"{text}\" ({picked.Selector})");
    }

    void OnShowMatches(object sender, RoutedEventArgs e) => ShowMatches(SelectedStep);

    /// <summary>Clicking the step that is already selected outlines its matches on the open page.</summary>
    void OnStepMouseDown(object sender, MouseButtonEventArgs e)
    {
        RememberDragStart(e);
        if (HandleSelectionClick(e)) return;
        if (_running || e.OriginalSource is not DependencyObject source) return;
        for (DependencyObject? node = source; node is not null; node = ParentOf(node))
        {
            if (node is System.Windows.Controls.Primitives.ToggleButton) return;
            if (node is TreeViewItem { DataContext: Step step })
            {
                if (step == SelectedStep) ShowMatches(step);
                return;
            }
        }
    }

    // ---- Log and results panel: folded to its tab headers until one is clicked ----

    const double LogFoldedHeight = 46;
    GridLength _logHeight = new(220);
    bool _logOpen;

    /// <summary>A click on a header opens the folded panel on that tab; a click on the header of the open tab folds it again.</summary>
    void OnLogTabClick(object sender, MouseButtonEventArgs e)
    {
        for (var node = e.OriginalSource as DependencyObject; node is not null; node = ParentOf(node))
        {
            if (node is not TabItem tab) continue;
            ShowLogPanel(!_logOpen || !tab.IsSelected);
            return;
        }
    }

    void ShowLogPanel(bool open)
    {
        if (open == _logOpen) return;
        if (!open) _logHeight = LogRow.Height;
        _logOpen = open;
        LogRow.MinHeight = open ? 90 : 0;
        LogRow.Height = open ? _logHeight : new GridLength(LogFoldedHeight);
        LogSplitter.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        LogChevron.Text = open ? "" : "";
    }

    static DependencyObject? ParentOf(DependencyObject node) =>
        node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);

    async void ShowMatches(Step? selected)
    {
        if (selected is not { UsesMatch: true } step || _host.Selected is not { } tab) return;
        try
        {
            var matches = await tab.HighlightAsync(step.ToQuery(), step.Type is StepType.Click or StepType.WaitFor, 5000);
            Log(matches.Count == 0
                ? "No match on this page (yet)."
                : $"{matches.Count} match(es); the first one (orange) is used: \"{matches[0].Text}\"");
            if (matches.Count == 0 && step.MatchMode == TextMatch.Wildcard && !(step.Text.Trim().StartsWith('*') && step.Text.Trim().EndsWith('*')))
                Log("  A wildcard pattern has to cover the whole text: start and end it with * (for example *Download*AlphaHost*) to match just a part of it.");
        }
        catch (Exception ex) { Log($"Could not check this page: {ex.Message}"); }
    }

    // ---- Running ----

    void SetRunning(bool running)
    {
        _running = running;
        RunAllButton.IsEnabled = RunSelectedButton.IsEnabled = RecordButton.IsEnabled = PlanMenuButton.IsEnabled = DeleteStepButton.IsEnabled = !running;
        StopButton.IsEnabled = running;
        if (running) SaveAndClearSelection();
        else RestoreSelection();
        RunAllButton.Style = (Style)FindResource(running ? "LabelButton" : "AccentLabelButton");   // the blue button is the one that can be pressed
        StopButton.Style = (Style)FindResource(running ? "AccentLabelButton" : "LabelButton");
        Properties.IsEnabled = DetailHeader.IsEnabled = !running;
        RefreshDetail();
    }

    // ---- the step that is running now

    List<Step> _selectionBeforeRun = [];

    /// <summary>While a run goes, no step is marked as selected (the green one is the running one); the selection comes back afterwards.</summary>
    void SaveAndClearSelection()
    {
        _selectionBeforeRun = _selection.Count > 0 ? [.. _selection] : SelectedStep is { } one ? [one] : [];
        SetSelection([]);
        if (StepTree.SelectedItem is not Step selected || FindContainer(StepTree, selected) is not { } container) return;
        _syncingSelection = true;
        try { container.IsSelected = false; }
        finally { _syncingSelection = false; }
    }

    void RestoreSelection()
    {
        var inPlan = _editor.InOrder().ToHashSet();
        var steps = _selectionBeforeRun.Where(inPlan.Contains).ToList();
        _selectionBeforeRun = [];
        if (steps.Count > 0) ApplySelection(steps);
    }

    readonly List<Step> _activeSteps = [];

    void OnStepStarted(Step step)
    {
        _activeSteps.Add(step);
        step.PropertyChanged += OnActiveStepChanged;
        RefreshRunStatus();
        ScrollToStep(step);
    }

    void ScrollToStep(Step step) =>
        Dispatcher.BeginInvoke(() => FindContainer(StepTree, step)?.BringIntoView(), System.Windows.Threading.DispatcherPriority.Background);

    void OnStepFinished(Step step)
    {
        step.PropertyChanged -= OnActiveStepChanged;
        _activeSteps.Remove(step);
        RefreshRunStatus();
    }

    void OnActiveStepChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not Step step || step != _activeSteps.LastOrDefault()) return;
        if (e.PropertyName is nameof(Step.RemainingText) or nameof(Step.ChangeText)) RefreshRunStatus();
        if (e.PropertyName == nameof(Step.HasCountdown) && step.HasCountdown) ScrollToStep(step);   // the row just grew by the countdown bar
    }

    /// <summary>"▶ Move the mouse · 1:27 left" under the playback buttons: the innermost running step, also when the list is scrolled elsewhere.</summary>
    void RefreshRunStatus()
    {
        var current = _activeSteps.LastOrDefault();
        RunStatus.Visibility = current is null ? Visibility.Collapsed : Visibility.Visible;
        RunStatus.Text = current is null ? "" : $"▶ {current.Title}{(current.HasCountdown ? $" · {current.RemainingText}" : "")}{(current.HasChangeText ? $" · {current.ChangeText}" : "")}";
    }

    static TreeViewItem? FindContainer(ItemsControl parent, Step step)
    {
        foreach (var item in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem container) continue;
            if (item == step) return container;
            if (FindContainer(container, step) is { } inner) return inner;
        }
        return null;
    }

    async Task RunAsync(IReadOnlyList<Step> steps, string title)
    {
        if (_running || _host.Selected is not { } start) return;
        if (!_options.Run) Autosave();
        RecordButton.IsChecked = false;
        SetRunning(true);
        _cts = new CancellationTokenSource();
        _runner.PauseMs = int.TryParse(PauseBox.Text, out var pause) ? Math.Max(0, pause) : 0;
        _runner.ShowClicks = ShowClicksBox.IsChecked == true;
        Log($"=== {title} ===");
        try
        {
            await _runner.RunAsync(steps, start, _cts.Token);
            Log("=== Finished ===");
        }
        catch (OperationCanceledException) { Log("=== Stopped ==="); }
        catch (StepFailedException ex) { Log($"✗ {ex.Message}"); throw; }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetRunning(false);
        }
    }

    async void OnRunAll(object sender, RoutedEventArgs e)
    {
        try { await RunAsync(_steps.ToList(), "Run all"); } catch (StepFailedException) { }
    }

    async void OnRunSelected(object sender, RoutedEventArgs e)
    {
        var roots = SelectedRoots;
        if (roots.Count == 0) return;
        try { await RunAsync(roots, "Run selected"); } catch (StepFailedException) { }
    }

    void OnStop(object sender, RoutedEventArgs e) => _cts?.Cancel();

    async Task RunHeadlessAsync()
    {
        var exitCode = 0;
        try { await RunAsync(_steps.ToList(), "Run all"); }
        catch (StepFailedException) { exitCode = 1; }
        if (_options.LogPath is not null) File.WriteAllLines(_options.LogPath, _logLines);
        if (_options.ResultsPath is not null) PlanFile.SaveResults(_options.ResultsPath, _results);
        Application.Current.Shutdown(exitCode);
    }

    // ---- Plan and results files ----

    static string Filter(string nameKey, string pattern) => $"{Loc.T(nameKey)} ({pattern})|{pattern}";

    void LoadPlan(string path)
    {
        _steps.Clear();
        foreach (var step in PlanFile.Load(path)) _steps.Add(step);
        Log($"Loaded {path}");
    }

    /// <summary>The "..." menu: everything that concerns the whole plan, kept away from the buttons for single steps.</summary>
    void OnPlanMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = PlanMenuButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        MenuItem Item(string glyph, string key, RoutedEventHandler click)
        {
            var item = new MenuItem { Header = Loc.T(key), Icon = new TextBlock { Text = glyph, FontFamily = (System.Windows.Media.FontFamily)FindResource("IconFont") } };
            item.Click += click;
            return item;
        }
        menu.Items.Add(Item("", "Tip.Open", OnLoadPlan));
        menu.Items.Add(Item("", "Tip.Save", OnSavePlan));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("", "Tip.Clear", OnClearPlan));
        menu.IsOpen = true;
    }

    void OnLoadPlan(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = Filter("Filter.Plan", "*.json") };
        if (dialog.ShowDialog(this) != true) return;
        try { LoadPlan(dialog.FileName); } catch (Exception ex) { Log($"Could not load plan: {ex.Message}"); }
    }

    void OnSavePlan(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = Filter("Filter.Plan", "*.json"), FileName = "plan.json" };
        if (dialog.ShowDialog(this) != true) return;
        PlanFile.Save(dialog.FileName, _steps);
        Log($"Saved {dialog.FileName}");
    }

    void OnSaveResults(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = Filter("Filter.Csv", "*.csv"), FileName = "results.csv" };
        if (dialog.ShowDialog(this) != true) return;
        PlanFile.SaveResults(dialog.FileName, _results);
        Log($"Saved {_results.Count} result(s) to {dialog.FileName}");
    }

    void OnClearResults(object sender, RoutedEventArgs e) => _results.Clear();

    async void OnSendResultsToJDownloader(object sender, RoutedEventArgs e)
    {
        var urls = _results.Select(r => r.Href).Where(h => h.Length > 0).Distinct().ToList();
        if (urls.Count == 0) { Log("No links in the results to send."); return; }
        try
        {
            await JDownloader.SendAsync(urls, "Clickpath", false, CancellationToken.None);
            Log($"{urls.Count} link(s) sent to JDownloader (package \"Clickpath\")");
        }
        catch (StepFailedException ex) { Log($"✗ {ex.Message}"); }
    }

    void Log(string line)
    {
        _logLines.Add(line);
        LogBox.AppendText(line + Environment.NewLine);
        LogBox.ScrollToEnd();
    }
}
