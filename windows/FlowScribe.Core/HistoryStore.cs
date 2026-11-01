using System.Text.Json;

namespace FlowScribe.Core;

public sealed record Note(Guid Id, string Text, DateTimeOffset Date)
{
    // A factory, not a second constructor: System.Text.Json cannot choose
    // between two public constructors and would fail to load the history.
    public static Note Create(string text) => new(Guid.NewGuid(), text, DateTimeOffset.Now);
}

/// <summary>
/// Keeps only the most recent transcriptions on disk. Older ones are removed
/// from the file on every save, so nothing beyond the limit is ever kept.
/// </summary>
public sealed class HistoryStore
{
    public const int Limit = 5;

    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    readonly string _path;
    readonly object _lock = new();
    List<Note> _notes;

    public IReadOnlyList<Note> Notes { get { lock (_lock) return _notes.ToList(); } }

    public HistoryStore(string path)
    {
        _path = path;
        _notes = LoadFrom(path);
        // Enforce the limit on files written by older builds as well.
        if (_notes.Count > Limit)
        {
            _notes = _notes.Take(Limit).ToList();
            try { Save(); } catch (Exception) { }
        }
    }

    public static string DefaultPath() => Path.Combine(AppPaths.DataDirectory, "history.json");

    /// Adds a note at the top and drops everything past the limit. Empty text is ignored.
    public IReadOnlyList<Note> Add(string text)
    {
        var trimmed = text.Trim();
        lock (_lock)
        {
            if (trimmed.Length == 0) return _notes.ToList();
            _notes.Insert(0, Note.Create(trimmed));
            if (_notes.Count > Limit) _notes.RemoveRange(Limit, _notes.Count - Limit);
            Save();
            return _notes.ToList();
        }
    }

    void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        // Write then rename: a crash mid-write never leaves a half-written file.
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_notes, Json));
        // Transcripts are private. On Windows the per-user data folder is already
        // readable only by this user; elsewhere restrict the file itself.
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temp, _path, overwrite: true);
    }

    static List<Note> LoadFrom(string path)
    {
        try
        {
            // A damaged file starts a fresh history instead of crashing the app.
            return JsonSerializer.Deserialize<List<Note>>(File.ReadAllText(path), Json) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }
}
