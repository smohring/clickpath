using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clickpath.Localization;

namespace Clickpath;

/// <summary>Settings that outlive a session, kept in %LocalAppData%\Clickpath\settings.json. A missing or damaged file means defaults.</summary>
public sealed class AppSettings
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clickpath", "settings.json");

    public Language Language { get; set; } = Language.Auto;

    /// <summary>Whether the browser extensions in the extensions folder (an ad blocker) are switched on.</summary>
    public bool BlockAds { get; set; }

    /// <summary>The browser-wide page zoom in percent.</summary>
    public int ZoomPercent { get; set; } = 100;

    public static AppSettings Load(string path)
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new AppSettings(); }
    }

    public static void Save(string path, AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
