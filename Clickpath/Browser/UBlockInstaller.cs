using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace Clickpath.Browser;

/// <summary>Downloads uBlock Origin (Chromium build, Manifest V2) from its GitHub release into the extensions folder when it is missing.</summary>
public static class UBlockInstaller
{
    const string LatestRelease = "https://api.github.com/repos/gorhill/uBlock/releases/latest";

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Clickpath");   // the GitHub API refuses requests without one
        return client;
    }

    public static string TargetFolder { get; } = Path.Combine(BrowserHost.ExtensionsFolder, "ublock-origin");

    public static bool IsInstalled => File.Exists(Path.Combine(TargetFolder, "manifest.json"));

    /// <summary>Installs uBlock Origin unless it is there; never throws, returns whether it is installed afterwards.</summary>
    public static async Task<bool> EnsureInstalledAsync()
    {
        if (IsInstalled) return true;
        var work = Path.Combine(Path.GetTempPath(), $"clickpath-ublock-{Guid.NewGuid():N}");
        try
        {
            AppLog.Write("uBlock Origin is not installed, downloading it");
            var release = await Http.GetStringAsync(LatestRelease);
            var (name, url) = PickAsset(release) ?? throw new InvalidDataException("no Chromium zip in the latest uBlock Origin release");
            Directory.CreateDirectory(work);
            var zip = Path.Combine(work, name);
            await File.WriteAllBytesAsync(zip, await Http.GetByteArrayAsync(url));
            Unpack(zip, TargetFolder);
            AppLog.Write($"uBlock Origin installed from {name} into {TargetFolder}");
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or JsonException)
        {
            AppLog.Write($"uBlock Origin could not be installed: {ex.Message}");
            return false;
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { }
        }
    }

    /// <summary>The Chromium zip (<c>uBlock0_*.chromium.zip</c>) of a GitHub release as name and download address, or null.</summary>
    public static (string Name, string Url)? PickAsset(string releaseJson)
    {
        using var doc = JsonDocument.Parse(releaseJson);
        if (!doc.RootElement.TryGetProperty("assets", out var assets)) return null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (name.StartsWith("uBlock0_", StringComparison.Ordinal) && name.EndsWith(".chromium.zip", StringComparison.Ordinal))
                return (name, asset.GetProperty("browser_download_url").GetString()!);
        }
        return null;
    }

    /// <summary>Unpacks the zip and puts the folder that holds <c>manifest.json</c> at <paramref name="target"/>, replacing what is there.</summary>
    public static void Unpack(string zipPath, string target)
    {
        var staging = Path.Combine(Path.GetDirectoryName(zipPath)!, "x");
        ZipFile.ExtractToDirectory(zipPath, staging);
        var manifest = Directory.EnumerateFiles(staging, "manifest.json", SearchOption.AllDirectories)
            .OrderBy(p => p.Length).FirstOrDefault() ?? throw new InvalidDataException("the archive holds no manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (Directory.Exists(target)) Directory.Delete(target, true);
        Directory.Move(Path.GetDirectoryName(manifest)!, target);
    }
}
