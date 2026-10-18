using System.Windows;

namespace Clickpath;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLog.Write("start");
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            AppLog.Write($"UI error (app keeps running): {args.Exception}");
            (MainWindow as MainWindow)?.ReportError(args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Write($"FATAL unhandled error (terminating={args.IsTerminating}): {args.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, args) => AppLog.Write($"unobserved task error: {args.Exception}");
        Exit += (_, args) => AppLog.Write($"exit code {args.ApplicationExitCode}");
        var options = StartupOptions.Parse(e.Args);
        // unattended runs always log in English, whatever the user picked
        Localization.Loc.Use(options.Run ? Localization.Language.English : AppSettings.Load(AppSettings.DefaultPath).Language);
        var window = new MainWindow(options);
        CloseTrace.Attach(window);
        window.Show();
    }
}

/// <summary>Command line: <c>Clickpath.exe [plan.json] [--run] [--log file] [--results file] [--adblock on|off]</c>.</summary>
public sealed record StartupOptions(string? PlanPath, bool Run, string? LogPath, string? ResultsPath, bool? BlockAds = null)
{
    public static StartupOptions Parse(string[] args)
    {
        string? plan = null, log = null, results = null;
        bool? blockAds = null;
        var run = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--run": run = true; break;
                case "--log" when i + 1 < args.Length: log = args[++i]; break;
                case "--results" when i + 1 < args.Length: results = args[++i]; break;
                case "--adblock" when i + 1 < args.Length && args[i + 1] is "on" or "off": blockAds = args[++i] == "on"; break;
                case "--adblock": break;   // no valid value: ignore the switch, never mistake it for the plan path
                default: plan ??= args[i]; break;
            }
        }
        return new StartupOptions(plan, run, log, results, blockAds);
    }
}
