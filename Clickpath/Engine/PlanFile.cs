using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clickpath.Models;

namespace Clickpath.Engine;

/// <summary>Plans are stored as a JSON array of steps; results as CSV.</summary>
public static class PlanFile
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToJson(IEnumerable<Step> steps) => JsonSerializer.Serialize(steps, Options);

    public static void Save(string path, IEnumerable<Step> steps) =>
        File.WriteAllText(path, ToJson(steps), new UTF8Encoding(false));

    public static List<Step> FromJson(string json) => JsonSerializer.Deserialize<List<Step>>(json, Options) ?? [];

    public static List<Step> Load(string path) => FromJson(File.ReadAllText(path, Encoding.UTF8));

    public static void SaveResults(string path, IEnumerable<ExtractedItem> items)
    {
        static string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
        var lines = items.Select(i => $"{Q(i.Text)},{Q(i.Href)},{Q(i.Page)}").Prepend("Text,Href,Page");
        File.WriteAllLines(path, lines, new UTF8Encoding(true));
    }
}
