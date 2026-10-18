using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clickpath.Browser;

public sealed record HistoryEntry(string Url, string Title, DateTime VisitedAt)
{
    /// <summary>What the list shows: the title, or the address if the page has none.</summary>
    [JsonIgnore] public string Label => Title.Length > 0 ? Title : Url;

    /// <summary>The automation name of a list item is its ToString.</summary>
    public override string ToString() => Label;
}

/// <summary>The most recently visited pages, newest first, kept in a small JSON file together with its two settings.</summary>
public sealed class HistoryStore
{
    public const int DefaultCapacity = 40;
    const int MinCapacity = 5, MaxCapacity = 500;

    sealed class FileModel
    {
        public int Capacity { get; set; } = DefaultCapacity;
        public bool IncludeRuns { get; set; }
        public List<HistoryEntry> Entries { get; set; } = [];
    }

    readonly string _path;
    readonly List<HistoryEntry> _entries = [];
    int _capacity = DefaultCapacity;
    bool _includeRuns;

    public HistoryStore(string path)
    {
        _path = path;
        try
        {
            if (JsonSerializer.Deserialize<FileModel>(File.ReadAllText(_path)) is { } model)
            {
                _capacity = Math.Clamp(model.Capacity, MinCapacity, MaxCapacity);
                _includeRuns = model.IncludeRuns;
                _entries.AddRange(model.Entries.Where(e => e is not null && Normalize(e.Url) is not null).Take(_capacity));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
    }

    public IReadOnlyList<HistoryEntry> Entries => _entries;

    /// <summary>How many pages are kept.</summary>
    public int Capacity
    {
        get => _capacity;
        set
        {
            _capacity = Math.Clamp(value, MinCapacity, MaxCapacity);
            Trim();
            Save();
        }
    }

    /// <summary>Whether pages visited by an automatic run are remembered too.</summary>
    public bool IncludeRuns
    {
        get => _includeRuns;
        set
        {
            _includeRuns = value;
            Save();
        }
    }

    /// <summary>Adds the page at the top (replacing an earlier visit of the same address); false if it is not a web page.</summary>
    public bool Record(string url, string title)
    {
        if (Normalize(url) is not { } key) return false;
        _entries.RemoveAll(e => e.Url == key);
        _entries.Insert(0, new HistoryEntry(key, title.Trim(), DateTime.Now));
        Trim();
        Save();
        return true;
    }

    /// <summary>The title of a page often arrives after the page was recorded.</summary>
    public void UpdateTitle(string url, string title)
    {
        if (Normalize(url) is not { } key || _entries.FindIndex(e => e.Url == key) is not (>= 0 and var i)) return;
        if (_entries[i].Title == title.Trim()) return;
        _entries[i] = _entries[i] with { Title = title.Trim() };
        Save();
    }

    public IEnumerable<HistoryEntry> Search(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? _entries
            : _entries.Where(e => e.Title.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase) || e.Url.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase));

    public void Clear()
    {
        _entries.Clear();
        Save();
    }

    /// <summary>The address without fragment, or null if it is not an http, https or file page.</summary>
    static string? Normalize(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "file"
            ? uri.GetLeftPart(UriPartial.Query)
            : null;

    void Trim()
    {
        if (_entries.Count > _capacity) _entries.RemoveRange(_capacity, _entries.Count - _capacity);
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(new FileModel { Capacity = _capacity, IncludeRuns = _includeRuns, Entries = _entries }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
