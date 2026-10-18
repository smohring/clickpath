using System.IO;

namespace Clickpath;

/// <summary>Diagnostics in %LocalAppData%\Clickpath\app.log: start, orderly exit, unhandled errors, browser process failures.</summary>
public static class AppLog
{
    static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clickpath", "app.log");

    public static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, $"{DateTime.Now:s} [{Environment.ProcessId}] {line}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
    }
}
