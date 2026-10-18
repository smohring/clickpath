using System.Net.Http;
using Clickpath.Browser;

namespace Clickpath.Engine;

/// <summary>Hands links to a running JDownloader through its local Click'n'Load interface (the one browser extensions use).</summary>
public static class JDownloader
{
    const string Endpoint = "http://127.0.0.1:9666";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>Adds the links to the link grabber; they are only downloaded right away if <paramref name="autostart"/> is set.</summary>
    public static async Task SendAsync(IReadOnlyCollection<string> urls, string package, bool autostart, CancellationToken ct)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["urls"] = string.Join("\n", urls),
            ["package"] = package,
            ["source"] = "Clickpath",
            ["autostart"] = autostart ? "1" : "0",
        });

        try
        {
            // JDownloader answers 204 (no content) when it took the links
            using var response = await Http.PostAsync($"{Endpoint}/flash/add", form, ct);
            if (!response.IsSuccessStatusCode)
                throw new StepFailedException($"JDownloader answered {(int)response.StatusCode}: {(await response.Content.ReadAsStringAsync(ct)).Trim()}");
        }
        catch (HttpRequestException ex)
        {
            throw new StepFailedException($"JDownloader is not reachable on 127.0.0.1:9666 ({ex.Message}). Is it running with Click'n'Load enabled?");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new StepFailedException("JDownloader did not answer within 8 seconds");
        }
    }
}
