using System.Globalization;
using System.Windows.Markup;

namespace Clickpath.Localization;

/// <summary>Which language the UI uses; <see cref="Auto"/> follows the Windows display language.</summary>
public enum Language { Auto, English, German }

/// <summary>UI strings in English and German. English is the fallback for everything that is not German.</summary>
public static class Loc
{
    static bool _german = IsGerman(Language.Auto, CultureInfo.CurrentUICulture);

    public static Language Current { get; private set; } = Language.Auto;

    /// <summary>Switches the language of all strings requested from now on (windows already built keep their texts).</summary>
    public static void Use(Language language)
    {
        Current = language;
        _german = IsGerman(language, CultureInfo.CurrentUICulture);
    }

    public static bool IsGerman(Language choice, CultureInfo windowsCulture) => choice switch
    {
        Language.German => true,
        Language.English => false,
        _ => windowsCulture.TwoLetterISOLanguageName == "de",
    };

    public static string T(string key) =>
        _german && De.TryGetValue(key, out var de) ? de : En.GetValueOrDefault(key, key);

    /// <summary>Keys that exist in only one of the two dictionaries; both lists should be empty.</summary>
    public static (IReadOnlyList<string> OnlyEnglish, IReadOnlyList<string> OnlyGerman) Mismatches() =>
        (En.Keys.Except(De.Keys).Order().ToList(), De.Keys.Except(En.Keys).Order().ToList());

    /// <summary>Keys whose German text is word for word the English sentence (four or more words), i.e. probably forgotten.</summary>
    public static IReadOnlyList<string> UntranslatedSentences() =>
        En.Where(e => De.TryGetValue(e.Key, out var de) && de == e.Value && e.Value.Split(' ').Length >= 4).Select(e => e.Key).Order().ToList();

    static readonly Dictionary<string, string> En = new()
    {
        ["Tab.New"] = "New tab",
        ["Download.Ask"] = "“{0}” was downloaded.\n\nOpen it now with JDownloader 2 (or the app Windows uses for this file type)?",
        ["Tip.Back"] = "Back", ["Tip.Forward"] = "Forward", ["Tip.Reload"] = "Reload", ["Tip.NewTab"] = "New tab",
        ["Tip.Language"] = "Language", ["Lang.Auto"] = "Automatic (Windows language)", ["Lang.Restart"] = "The language changes after a restart.\n\nRestart Clickpath now? Your steps are kept.",
        ["Tip.History"] = "Recently visited pages (Ctrl+click opens in a new tab)", ["History.Filter"] = "Filter…", ["History.Empty"] = "No pages yet",
        ["History.Clear"] = "Clear history", ["History.Keep"] = "Keep the last", ["History.Runs"] = "Also remember pages visited during runs",
        ["Tip.Open"] = "Open plan…", ["Tip.Save"] = "Save plan…", ["Tip.Clear"] = "Delete all steps and start over", ["Clear.Ask"] = "Delete all {0} steps and start with an empty plan?",
        ["Tip.PlanMenu"] = "The whole plan: open, save, delete all steps", ["Btn.DeleteStep"] = "Delete",
        ["Add.Before"] = "Insert before the selected step", ["Add.Inside"] = "Insert inside the selected group", ["Add.After"] = "Insert after the selected step", ["Tip.Delete"] = "Delete step",

        ["Run.Changed"] = "page changed: {0} words differ (ends at {1})", ["Run.SourceChanged"] = ", source {0} characters{1}", ["Run.SourceLimit"] = " (ends at {0})",
        ["Field.SourceChars"] = "... or when the page source changes by this many characters (0 = off)", ["Run.Elapsed"] = "running for {0}", ["Opt.MoveForever"] = "No time limit - keep moving until an end condition below is met",
        ["Hint.MoveForever"] = "Needs \u201cend as soon as the page changes\u201d or a text below, otherwise the step fails (it would never end). Stop ends it too.", ["Sub.NoLimit"] = "no time limit",
        ["Run.Left"] = "{0} left", ["Run.TimeoutLeft"] = "{0} until timeout", ["Run.Title"] = "Playback", ["Run.All"] = "Run all", ["Run.Step"] = "Run step", ["Run.Stop"] = "Stop",
        ["Run.StepTip"] = "Runs only the selected step on the current tab; the browser stays where it ends up",
        ["Run.ShowClicks"] = "Show where it clicks", ["Run.Pause"] = "Pause between steps (ms)",

        ["Rec.Start"] = "Record clicks", ["Tip.Record"] = "Every click you make in the browser is added as a step",
        ["Rec.Hint"] = "Recording: click through the pages – every click becomes a step. Select a step first to record into it.",
        ["Steps.Title"] = "Steps", ["Steps.Add"] = "Add step",
        ["Steps.AddTip"] = "Adds into the selected “Repeat for each link” step, otherwise after the selected step",

        ["Step.Navigate"] = "Open a page", ["Desc.Navigate"] = "Load a web address in the current tab",
        ["Step.Click"] = "Click something", ["Desc.Click"] = "Click a button or link found by its text",
        ["Step.ClickPage"] = "Click anywhere in the window", ["Desc.ClickPage"] = "Click into the page without aiming at an element, for example when any click opens a new tab",
        ["Sub.PageCenter"] = "middle of the page",
        ["Step.OpenLink"] = "Open a link", ["Desc.OpenLink"] = "Open a link found by its text, here or in a new tab",
        ["Step.ForEachLink"] = "Repeat for each link", ["Desc.ForEachLink"] = "Open every matching link and run the steps below it for each",
        ["Step.WaitFor"] = "Wait until it appears", ["Desc.WaitFor"] = "Wait until something shows up on the page",
        ["Step.IfFound"] = "If it is on the page", ["Desc.IfFound"] = "Run the steps below only if something is (or is not) on the page",
        ["Opt.Negate"] = "Run the steps only if it is NOT there", ["Field.TimeoutIf"] = "Look for up to (ms)", ["Sub.Not"] = "not:",
        ["Hint.Vars"] = "Inside “Repeat for each link” you can use {text} and {href} of the current link here.",
        ["Step.Delay"] = "Pause", ["Desc.Delay"] = "Wait a fixed time",
        ["Step.MoveMouse"] = "Move the mouse around", ["Desc.MoveMouse"] = "Move the pointer randomly over the page for a while",
        ["Field.Move"] = "Duration (ms)",
        ["Step.SendToJDownloader"] = "Send to JDownloader", ["Desc.SendToJDownloader"] = "Put the matching links into the JDownloader link grabber",
        ["Field.Package"] = "Package name in JDownloader (you can use {text} and {href})", ["Opt.Autostart"] = "Start downloading right away",
        ["Results.SendJd"] = "Send to JDownloader",
        ["Step.Extract"] = "Collect links", ["Desc.Extract"] = "Save text and address of the matches to the results list",
        ["Step.RunScript"] = "Run JavaScript", ["Desc.RunScript"] = "Run your own script on the page",
        ["Step.CloseTab"] = "Close this tab", ["Desc.CloseTab"] = "Close the current tab and go back to the previous one",
        ["Step.ForEachListedLink"] = "Repeat for each link in my list", ["Desc.ForEachListedLink"] = "Open every link of a list you type or paste yourself and run the steps below it for each",
        ["Step.ForEachElement"] = "Repeat for each match", ["Desc.ForEachElement"] = "Go through every matching element in turn (for example each mirror button): click it, then run the steps below it",
        ["Step.StopRepeating"] = "Stop repeating", ["Desc.StopRepeating"] = "Leave the repeat step around this one, for example once the right link has been clicked; the page you are on stays open and the run continues there",
        ["Opt.ClickEachMatch"] = "Click each match first", ["Sub.Near"] = "in the row with", ["Sub.Until"] = "until",
        ["Field.Until"] = "Stop as soon as this text appears (optional)", ["Field.UntilWait"] = "Wait after each round up to (ms)",
        ["Hint.Until"] = "Checked after every round, also for text that script inserts a moment after the click. When it shows, the loop ends and the run continues below it. 0 ms = look once. Empty = no condition.",
        ["Hint.ForEachElement"] = "Each round clicks one match (1st, 2nd, ...) and then runs the steps below. {index} is its number, {text} its text. Combine with “If it is on the page” (it waits for content that script shows after the click) and “Stop repeating”.",
        ["Field.Near"] = "Only the one in the same row as (optional)",
        ["Hint.Near"] = "A text, the alt text or file name of an image (e.g. a hoster logo “alphahost”), a class or the host of a link in the same row. Empty = no restriction.",
        ["Field.LinkList"] = "Links (one per line)", ["Btn.PasteLinks"] = "Paste", ["Btn.LinksFromResults"] = "From results", ["Btn.TidyLinks"] = "Tidy up", ["Btn.ClearLinks"] = "Clear",
        ["Tip.PasteLinks"] = "Add the web addresses from the clipboard", ["Tip.LinksFromResults"] = "Add the addresses collected with “Collect links”", ["Menu.AddToLinkList"] = "Add to link list",
        ["Tip.AdBlock"] = "Ad blocker on/off (uBlock Origin from %LocalAppData%\\Clickpath\\extensions). Right click: uBlock settings",
        ["Tip.Zoom"] = "Page zoom (Ctrl+plus / Ctrl+minus / Ctrl+0 in the page)",
        ["Tip.AppMenu"] = "Menu", ["Menu.AdBlock"] = "Ad blocker", ["Menu.History"] = "History", ["Menu.DataFolder"] = "Open data folder",
        ["Menu.UbSettings"] = "uBlock settings", ["Menu.UbLists"] = "Filter lists", ["Menu.UbMyFilters"] = "My filters", ["Menu.UbTrusted"] = "Trusted sites", ["Menu.UbLogger"] = "Logger",
        ["Menu.UbOff"] = "Switch the ad blocker on to open its settings", ["Menu.UbMissing"] = "uBlock Origin is not installed (it is downloaded at the next start)",
        ["Tip.TidyLinks"] = "Remove blank lines and repeated addresses",
        ["Hint.LinkList"] = "One link per line. Blank lines and lines starting with # are skipped. In “Name https://…” the address is used; a bare example.com/page gets https://.",
        ["Summary.Links"] = "{0} link(s)", ["Summary.Ignored"] = "{0} line(s) ignored", ["Links.ClearAsk"] = "Remove all {0} links from the list?",
        ["Step.CloseAllTabs"] = "Close all windows", ["Desc.CloseAllTabs"] = "Close every open tab and window and continue on one blank tab - a clean start for the next run",

        ["Field.Url"] = "Web address",
        ["Field.Target"] = "Look for", ["Target.Link"] = "A link", ["Target.Clickable"] = "A button or link",
        ["Target.Anything"] = "Any text on the page", ["Target.Css"] = "A CSS selector (advanced)",
        ["Field.Css"] = "CSS selector", ["Field.Text"] = "The text of the element…",
        ["Match.Any"] = "any text", ["Match.Contains"] = "contains", ["Match.StartsWith"] = "starts with",
        ["Match.EndsWith"] = "ends with", ["Match.Exactly"] = "is exactly", ["Match.Wildcard"] = "fits the pattern (* = anything)",
        ["Match.Regex"] = "is a regular expression",
        ["Field.MatchCase"] = "Respect upper/lower case", ["Hint.TextEmpty"] = "Leave the text empty to match anything.",
        ["Btn.Pick"] = "Pick from page", ["Tip.Pick"] = "Click an element in the browser to fill in the fields (Esc cancels)",
        ["Btn.Show"] = "Show matches",
        ["Tip.Show"] = "Highlights what this step finds on the current page; the first match (orange) is the one used",
        ["Opt.NewTabOpen"] = "Open in a new tab", ["Opt.HumanMouse"] = "Move the mouse like a person (curved path, random spot inside)",
        ["Opt.EndOnNewPage"] = "End as soon as the page changes (new page, new window, or content replaced by script) and continue with the next step", ["Sub.EndOnNewPage"] = "→ ends when the page changes",
        ["Field.ChangedWords"] = "End when this many words differ from the start (new or gone)",
        ["Hint.ChangedWords"] = "Every 300 ms the visible words (frames included, numbers not) are compared with those at the start of the step; the step ends when that many words are new or gone - 20 catches a swapped block, a whole new page differs by far more. The length of the generated page source can end it too (0 = off; scripts and ads change it all the time, so look at the live value first). Smaller values react sooner but may end on a cookie banner or a rotating ad. Any one of the conditions is enough.",
        ["Hint.EndText"] = "Checked every 300 ms while the step waits - also for text that script puts into the page without loading a new one (a captcha that swaps its content). When it shows, the step ends and the run continues with the next one. Empty = no condition.",
        ["Tip.GroupType"] = "Change the kind of this step - the steps inside it stay",
        ["Detail.Multi"] = "{0} steps selected. Drag them together, move them with Alt+arrow keys, run them with “Run step” or press Delete.",
        ["Steps.DeleteAsk"] = "Delete the selected steps? That removes {0} steps in total, including the steps inside them.",
        ["Dlg.Delete"] = "Delete", ["Dlg.Clear"] = "Clear", ["Dlg.Cancel"] = "Cancel", ["Dlg.RestartNow"] = "Restart now", ["Dlg.Later"] = "Later",
        ["Dlg.Open"] = "Open", ["Dlg.NotNow"] = "Not now",
        ["Opt.KeepPages"] = "Keep the pages the run still needs", ["Sub.KeepsPages"] = "keeps the list page of a loop",
        ["Hint.KeepPages"] = "Inside “Repeat for each link” the list page (and what it needs for the next page) stays open; everything else is closed. Outside a loop all windows are closed and one blank tab is left.",
        ["Sub.AllPages"] = "all pages", ["Sub.Range"] = "limited range",
        ["Field.Range"] = "Which links (optional)", ["Field.StartAt"] = "Start at the link matching", ["Field.StopAfter"] = "Stop after the link matching",
        ["Hint.Range"] = "Compared with the link text and its address; the start and end links are included. Empty = no limit.",
        ["Opt.Paginate"] = "Go through all pages (switches to the next page when the links are done)",
        ["Field.NextText"] = "Text of the “next page” button (empty = detect it automatically)", ["Field.MaxPages"] = "Visit at most this many pages",
        ["Opt.RandomClicks"] = "Now and then click into an empty spot", ["Field.ClickEvery"] = "Average time between clicks (ms)", ["Sub.RandomClicks"] = "random clicks",
        ["Field.NewWindow"] = "If the click opens a new window",
        ["NewWindow.Follow"] = "Continue in the new window", ["NewWindow.Stay"] = "Stay here (leave the new window open)",
        ["NewWindow.StayAndClose"] = "Stay here and close the new window", ["Sub.CloseNew"] = "→ closes the new window",
        ["NewWindow.CloseAndRepeat"] = "Close it and click again until no window opens", ["Sub.Repeat"] = "→ repeats until no window opens",
        ["Opt.Continue"] = "If something fails, continue with the next link",
        ["Field.Timeout"] = "Wait up to (ms) for it to appear", ["Field.Delay"] = "Pause (ms)",
        ["Field.Script"] = "JavaScript (the result is logged)",
        ["Sub.NewTab"] = "→ new tab",

        ["Tab.Log"] = "Log", ["Tab.Results"] = "Results", ["Results.Save"] = "Save as CSV…", ["Results.Clear"] = "Clear",
        ["Col.Text"] = "Text", ["Col.Href"] = "Address", ["Col.Page"] = "Page",
        ["Filter.Plan"] = "Clickpath plan", ["Filter.Csv"] = "CSV",
    };

    static readonly Dictionary<string, string> De = new()
    {
        ["Tab.New"] = "Neuer Tab",
        ["Download.Ask"] = "„{0}“ wurde heruntergeladen.\n\nJetzt mit JDownloader 2 öffnen (oder mit der App, die Windows für diesen Dateityp verwendet)?",
        ["Tip.Back"] = "Zurück", ["Tip.Forward"] = "Vor", ["Tip.Reload"] = "Neu laden", ["Tip.NewTab"] = "Neuer Tab",
        ["Tip.Language"] = "Sprache", ["Lang.Auto"] = "Automatisch (Windows-Sprache)", ["Lang.Restart"] = "Die Sprache ändert sich nach einem Neustart.\n\nClickpath jetzt neu starten? Deine Schritte bleiben erhalten.",
        ["Tip.History"] = "Zuletzt besuchte Seiten (Strg+Klick öffnet in neuem Tab)", ["History.Filter"] = "Filtern…", ["History.Empty"] = "Noch keine Seiten",
        ["History.Clear"] = "Verlauf löschen", ["History.Keep"] = "Die letzten", ["History.Runs"] = "Auch Seiten aus Abläufen merken",
        ["Tip.Open"] = "Plan öffnen…", ["Tip.Save"] = "Plan speichern…", ["Tip.Clear"] = "Alle Schritte löschen und neu beginnen", ["Clear.Ask"] = "Alle {0} Schritte löschen und mit einem leeren Plan neu beginnen?",
        ["Tip.PlanMenu"] = "Der ganze Plan: öffnen, speichern, alle Schritte löschen", ["Btn.DeleteStep"] = "Löschen",
        ["Add.Before"] = "Vor dem markierten Schritt einfügen", ["Add.Inside"] = "In die markierte Gruppe einfügen", ["Add.After"] = "Hinter dem markierten Schritt einfügen", ["Tip.Delete"] = "Schritt löschen",

        ["Run.Changed"] = "Seite verändert: {0} Wörter weichen ab (endet ab {1})", ["Run.SourceChanged"] = ", Quelltext {0} Zeichen{1}", ["Run.SourceLimit"] = " (endet ab {0})",
        ["Field.SourceChars"] = "... oder wenn sich der Quelltext der Seite um so viele Zeichen ändert (0 = aus)", ["Run.Elapsed"] = "läuft seit {0}", ["Opt.MoveForever"] = "Kein Zeitlimit - weiterbewegen, bis eine der Bedingungen unten eintritt",
        ["Hint.MoveForever"] = "Braucht \u201eBeenden, sobald sich die Seite ändert\u201c oder einen Text unten, sonst schlägt der Schritt fehl (er würde nie enden). Auch „Stopp“ beendet ihn.", ["Sub.NoLimit"] = "ohne Zeitlimit",
        ["Run.Left"] = "noch {0}", ["Run.TimeoutLeft"] = "noch {0} bis Timeout", ["Run.Title"] = "Wiedergabe", ["Run.All"] = "Alle ausführen", ["Run.Step"] = "Schritt testen", ["Run.Stop"] = "Stopp",
        ["Run.StepTip"] = "Führt nur den markierten Schritt im aktuellen Tab aus; der Browser bleibt danach dort stehen",
        ["Run.ShowClicks"] = "Zeigen, wohin geklickt wird", ["Run.Pause"] = "Pause zwischen Schritten (ms)",

        ["Rec.Start"] = "Klicks aufnehmen", ["Tip.Record"] = "Jeder Klick im Browser wird als Schritt hinzugefügt",
        ["Rec.Hint"] = "Aufnahme läuft: klick dich durch die Seiten – jeder Klick wird ein Schritt. Markiere vorher einen Schritt, um dort hinein aufzunehmen.",
        ["Steps.Title"] = "Schritte", ["Steps.Add"] = "Schritt hinzufügen",
        ["Steps.AddTip"] = "Fügt in den markierten Schritt „Für jeden Link wiederholen“ ein, sonst hinter dem markierten Schritt",

        ["Step.Navigate"] = "Seite öffnen", ["Desc.Navigate"] = "Eine Webadresse im aktuellen Tab laden",
        ["Step.Click"] = "Etwas anklicken", ["Desc.Click"] = "Einen Button oder Link anhand seines Textes anklicken",
        ["Step.ClickPage"] = "Irgendwo ins Fenster klicken", ["Desc.ClickPage"] = "In die Seite klicken, ohne ein Element anzuvisieren, etwa wenn jeder Klick einen neuen Tab öffnet",
        ["Sub.PageCenter"] = "Mitte der Seite",
        ["Step.OpenLink"] = "Link öffnen", ["Desc.OpenLink"] = "Einen Link anhand seines Textes öffnen, hier oder in neuem Tab",
        ["Step.ForEachLink"] = "Für jeden Link wiederholen", ["Desc.ForEachLink"] = "Jeden passenden Link öffnen und die Schritte darunter jeweils ausführen",
        ["Step.WaitFor"] = "Warten, bis es erscheint", ["Desc.WaitFor"] = "Warten, bis etwas auf der Seite sichtbar wird",
        ["Step.IfFound"] = "Wenn es auf der Seite ist", ["Desc.IfFound"] = "Die Schritte darunter nur ausführen, wenn etwas auf der Seite ist (oder nicht ist)",
        ["Opt.Negate"] = "Schritte nur ausführen, wenn es NICHT vorhanden ist", ["Field.TimeoutIf"] = "Bis zu (ms) danach suchen", ["Sub.Not"] = "nicht:",
        ["Hint.Vars"] = "In „Für jeden Link wiederholen“ kannst du hier {text} und {href} des aktuellen Links verwenden.",
        ["Step.Delay"] = "Pause", ["Desc.Delay"] = "Eine feste Zeit warten",
        ["Step.MoveMouse"] = "Maus bewegen", ["Desc.MoveMouse"] = "Den Mauszeiger eine Weile zufällig über die Seite bewegen",
        ["Field.Move"] = "Dauer (ms)",
        ["Step.SendToJDownloader"] = "An JDownloader senden", ["Desc.SendToJDownloader"] = "Die passenden Links in den Linksammler von JDownloader legen",
        ["Field.Package"] = "Paketname in JDownloader ({text} und {href} sind möglich)", ["Opt.Autostart"] = "Download sofort starten",
        ["Results.SendJd"] = "An JDownloader senden",
        ["Step.Extract"] = "Links sammeln", ["Desc.Extract"] = "Text und Adresse der Treffer in die Ergebnisliste übernehmen",
        ["Step.RunScript"] = "JavaScript ausführen", ["Desc.RunScript"] = "Ein eigenes Skript auf der Seite ausführen",
        ["Step.CloseTab"] = "Diesen Tab schließen", ["Desc.CloseTab"] = "Den aktuellen Tab schließen und zum vorherigen zurückkehren",
        ["Step.ForEachListedLink"] = "Für jeden Link meiner Liste wiederholen", ["Desc.ForEachListedLink"] = "Jeden Link einer Liste öffnen, die du selbst eintippst oder einfügst, und die Schritte darunter jeweils ausführen",
        ["Step.ForEachElement"] = "Für jeden Treffer wiederholen", ["Desc.ForEachElement"] = "Jedes passende Element der Reihe nach durchgehen (zum Beispiel jeden Mirror-Knopf): anklicken, dann die Schritte darunter ausführen",
        ["Step.StopRepeating"] = "Wiederholung beenden", ["Desc.StopRepeating"] = "Den umgebenden Wiederholungsschritt verlassen, zum Beispiel sobald der richtige Link geklickt wurde; die Seite, auf der du bist, bleibt offen und es geht dort weiter",
        ["Opt.ClickEachMatch"] = "Jeden Treffer zuerst anklicken", ["Sub.Near"] = "in der Zeile mit", ["Sub.Until"] = "bis",
        ["Field.Until"] = "Beenden, sobald dieser Text erscheint (optional)", ["Field.UntilWait"] = "Nach jeder Runde bis zu (ms) warten",
        ["Hint.Until"] = "Wird nach jeder Runde geprüft, auch für Text, den ein Skript einen Moment nach dem Klick einfügt. Sobald er erscheint, endet die Schleife und es geht darunter weiter. 0 ms = einmal nachsehen. Leer = keine Bedingung.",
        ["Hint.ForEachElement"] = "Jede Runde klickt einen Treffer (1., 2., ...) und führt dann die Schritte darunter aus. {index} ist seine Nummer, {text} sein Text. Zusammen mit „Wenn es auf der Seite ist“ (wartet auf Inhalt, den ein Skript nach dem Klick einblendet) und „Wiederholung beenden“ verwenden.",
        ["Field.Near"] = "Nur der in derselben Zeile wie (optional)",
        ["Hint.Near"] = "Ein Text, der Alt-Text oder Dateiname eines Bildes (z. B. ein Hoster-Logo „alphahost“), eine Klasse oder der Host eines Links in derselben Zeile. Leer = keine Einschränkung.",
        ["Field.LinkList"] = "Links (einer pro Zeile)", ["Btn.PasteLinks"] = "Einfügen", ["Btn.LinksFromResults"] = "Aus Ergebnissen", ["Btn.TidyLinks"] = "Aufräumen", ["Btn.ClearLinks"] = "Leeren",
        ["Tip.PasteLinks"] = "Die Webadressen aus der Zwischenablage hinzufügen", ["Tip.LinksFromResults"] = "Die mit „Links sammeln“ gesammelten Adressen hinzufügen", ["Menu.AddToLinkList"] = "Zur Linkliste hinzufügen",
        ["Tip.AdBlock"] = "Werbeblocker an/aus (uBlock Origin aus %LocalAppData%\\Clickpath\\extensions). Rechtsklick: uBlock-Einstellungen",
        ["Tip.Zoom"] = "Seitenzoom (Strg+Plus / Strg+Minus / Strg+0 in der Seite)",
        ["Tip.AppMenu"] = "Menü", ["Menu.AdBlock"] = "Werbeblocker", ["Menu.History"] = "Verlauf", ["Menu.DataFolder"] = "Datenordner öffnen",
        ["Menu.UbSettings"] = "uBlock-Einstellungen", ["Menu.UbLists"] = "Filterlisten", ["Menu.UbMyFilters"] = "Meine Filter", ["Menu.UbTrusted"] = "Ausnahmeregeln", ["Menu.UbLogger"] = "Logger",
        ["Menu.UbOff"] = "Werbeblocker einschalten, um die Einstellungen zu öffnen", ["Menu.UbMissing"] = "uBlock Origin ist nicht installiert (wird beim nächsten Start heruntergeladen)",
        ["Tip.TidyLinks"] = "Leerzeilen und doppelte Adressen entfernen",
        ["Hint.LinkList"] = "Ein Link pro Zeile. Leere Zeilen und Zeilen mit # am Anfang werden übersprungen. Bei „Name https://…“ gilt die Adresse darin, bei example.com/seite wird https:// ergänzt.",
        ["Summary.Links"] = "{0} Link(s)", ["Summary.Ignored"] = "{0} Zeile(n) ignoriert", ["Links.ClearAsk"] = "Alle {0} Links aus der Liste entfernen?",
        ["Step.CloseAllTabs"] = "Alle Fenster schließen", ["Desc.CloseAllTabs"] = "Alle offenen Tabs und Fenster schließen und auf einem leeren Tab weitermachen - ein sauberer Start für den nächsten Lauf",

        ["Field.Url"] = "Webadresse",
        ["Field.Target"] = "Suchen nach", ["Target.Link"] = "Einem Link", ["Target.Clickable"] = "Einem Button oder Link",
        ["Target.Anything"] = "Beliebigem Text auf der Seite", ["Target.Css"] = "Einem CSS-Selektor (erweitert)",
        ["Field.Css"] = "CSS-Selektor", ["Field.Text"] = "Der Text des Elements…",
        ["Match.Any"] = "beliebiger Text", ["Match.Contains"] = "enthält", ["Match.StartsWith"] = "beginnt mit",
        ["Match.EndsWith"] = "endet mit", ["Match.Exactly"] = "ist genau", ["Match.Wildcard"] = "passt auf das Muster (* = beliebig)",
        ["Match.Regex"] = "ist ein regulärer Ausdruck",
        ["Field.MatchCase"] = "Groß-/Kleinschreibung beachten", ["Hint.TextEmpty"] = "Text leer lassen, um alles zu treffen.",
        ["Btn.Pick"] = "Aus Seite auswählen", ["Tip.Pick"] = "Ein Element im Browser anklicken, um die Felder auszufüllen (Esc bricht ab)",
        ["Btn.Show"] = "Treffer anzeigen",
        ["Tip.Show"] = "Hebt auf der aktuellen Seite hervor, was dieser Schritt findet; der erste Treffer (orange) wird verwendet",
        ["Opt.NewTabOpen"] = "In neuem Tab öffnen", ["Opt.HumanMouse"] = "Maus wie ein Mensch bewegen (gebogener Weg, zufällige Stelle im Ziel)",
        ["Opt.EndOnNewPage"] = "Beenden, sobald sich die Seite ändert (neue Seite, neues Fenster oder per Skript ersetzter Inhalt), und mit dem nächsten Schritt weitermachen", ["Sub.EndOnNewPage"] = "→ endet, wenn sich die Seite ändert",
        ["Field.ChangedWords"] = "Beenden, wenn so viele Wörter vom Start abweichen (neue oder weggefallene)",
        ["Hint.ChangedWords"] = "Alle 300 ms werden die sichtbaren Wörter (Frames eingeschlossen, Zahlen nicht) mit denen vom Start des Schritts verglichen; der Schritt endet, wenn so viele Wörter neu oder weggefallen sind - 20 erfasst einen ausgetauschten Block, eine ganz neue Seite weicht weit mehr ab. Auch die Länge des erzeugten Quelltexts kann ihn beenden (0 = aus; Skripte und Werbung verändern ihn ständig, schau dir zuerst den Live-Wert an). Kleinere Werte reagieren früher, können aber bei einem Cookie-Banner oder wechselnder Werbung zu früh enden. Eine einzige der Bedingungen genügt.",
        ["Hint.EndText"] = "Wird alle 300 ms geprüft, solange der Schritt wartet - auch für Text, den ein Skript in die Seite einfügt, ohne eine neue zu laden (ein Captcha, das seinen Inhalt austauscht). Sobald er erscheint, endet der Schritt und es geht mit dem nächsten weiter. Leer = keine Bedingung.",
        ["Tip.GroupType"] = "Art dieses Schritts ändern – die Schritte darin bleiben erhalten",
        ["Detail.Multi"] = "{0} Schritte markiert. Zusammen ziehen, mit Alt+Pfeiltasten verschieben, mit „Schritt testen“ ausführen oder Entf drücken.",
        ["Steps.DeleteAsk"] = "Die markierten Schritte löschen? Das entfernt insgesamt {0} Schritte, einschließlich der Schritte darin.",
        ["Dlg.Delete"] = "Löschen", ["Dlg.Clear"] = "Leeren", ["Dlg.Cancel"] = "Abbrechen", ["Dlg.RestartNow"] = "Jetzt neu starten", ["Dlg.Later"] = "Später",
        ["Dlg.Open"] = "Öffnen", ["Dlg.NotNow"] = "Nicht jetzt",
        ["Opt.KeepPages"] = "Seiten behalten, die der Ablauf noch braucht", ["Sub.KeepsPages"] = "behält die Listenseite einer Schleife",
        ["Hint.KeepPages"] = "In „Für jeden Link wiederholen“ bleibt die Listenseite (samt dem, was sie für die nächste Seite braucht) offen; alles andere wird geschlossen. Außerhalb einer Schleife werden alle Fenster geschlossen und ein leerer Tab bleibt übrig.",
        ["Sub.AllPages"] = "alle Seiten", ["Sub.Range"] = "begrenzter Bereich",
        ["Field.Range"] = "Welche Links (optional)", ["Field.StartAt"] = "Beginnen bei dem Link, der passt zu", ["Field.StopAfter"] = "Aufhören nach dem Link, der passt zu",
        ["Hint.Range"] = "Verglichen mit dem Linktext und der Adresse; Start- und Endlink werden mit bearbeitet. Leer = keine Grenze.",
        ["Opt.Paginate"] = "Alle Seiten durchgehen (wechselt zur nächsten Seite, wenn die Links abgearbeitet sind)",
        ["Field.NextText"] = "Text des „Nächste Seite“-Knopfs (leer = automatisch erkennen)", ["Field.MaxPages"] = "Höchstens so viele Seiten besuchen",
        ["Opt.RandomClicks"] = "Ab und zu an eine leere Stelle klicken", ["Field.ClickEvery"] = "Zeit zwischen den Klicks im Schnitt (ms)", ["Sub.RandomClicks"] = "zufällige Klicks",
        ["Field.NewWindow"] = "Wenn der Klick ein neues Fenster öffnet",
        ["NewWindow.Follow"] = "Im neuen Fenster weitermachen", ["NewWindow.Stay"] = "Hier bleiben (neues Fenster offen lassen)",
        ["NewWindow.StayAndClose"] = "Hier bleiben und das neue Fenster schließen", ["Sub.CloseNew"] = "→ schließt das neue Fenster",
        ["NewWindow.CloseAndRepeat"] = "Schließen und erneut klicken, bis sich kein Fenster mehr öffnet", ["Sub.Repeat"] = "→ wiederholt, bis sich kein Fenster mehr öffnet",
        ["Opt.Continue"] = "Bei einem Fehler mit dem nächsten Link weitermachen",
        ["Field.Timeout"] = "Bis zu (ms) warten, bis es erscheint", ["Field.Delay"] = "Pause (ms)",
        ["Field.Script"] = "JavaScript (das Ergebnis wird protokolliert)",
        ["Sub.NewTab"] = "→ neuer Tab",

        ["Tab.Log"] = "Protokoll", ["Tab.Results"] = "Ergebnisse", ["Results.Save"] = "Als CSV speichern…", ["Results.Clear"] = "Leeren",
        ["Col.Text"] = "Text", ["Col.Href"] = "Adresse", ["Col.Page"] = "Seite",
        ["Filter.Plan"] = "Clickpath-Plan", ["Filter.Csv"] = "CSV",
    };
}

/// <summary>XAML: <c>{loc:T Run.All}</c>.</summary>
public sealed class TExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(key);
}
