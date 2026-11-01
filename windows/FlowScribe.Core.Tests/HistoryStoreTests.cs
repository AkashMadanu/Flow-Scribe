using System.Text.Json;
using FlowScribe.Core;

namespace FlowScribe.Core.Tests;

// The same cases as Tests/FlowScribeCoreTests/HistoryStoreTests.swift.
public sealed class HistoryStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    string FilePath => Path.Combine(_dir, "history.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void StartsEmptyWhenNoFile() => Assert.Empty(new HistoryStore(FilePath).Notes);

    [Fact]
    public void KeepsOnlyLastFiveNewestFirst()
    {
        var store = new HistoryStore(FilePath);
        for (var i = 1; i <= 7; i++) store.Add($"note {i}");
        Assert.Equal(["note 7", "note 6", "note 5", "note 4", "note 3"], store.Notes.Select(n => n.Text));
    }

    [Fact]
    public void OlderNotesAreGoneFromDiskToo()
    {
        var store = new HistoryStore(FilePath);
        for (var i = 1; i <= 6; i++) store.Add($"note {i}");
        Assert.DoesNotContain("note 1\"", File.ReadAllText(FilePath));
        Assert.Equal(5, new HistoryStore(FilePath).Notes.Count);
    }

    [Fact]
    public void PersistsAcrossLaunches()
    {
        new HistoryStore(FilePath).Add("hello");
        Assert.Equal(["hello"], new HistoryStore(FilePath).Notes.Select(n => n.Text));
    }

    [Fact]
    public void IgnoresEmptyText()
    {
        var store = new HistoryStore(FilePath);
        store.Add("   \n");
        Assert.Empty(store.Notes);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void DamagedFileStartsFresh()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "not json");
        var store = new HistoryStore(FilePath);
        Assert.Empty(store.Notes);
        store.Add("after damage");
        Assert.Equal(["after damage"], new HistoryStore(FilePath).Notes.Select(n => n.Text));
    }

    [Fact]
    public void OversizedFileFromOlderBuildIsTrimmedOnLoad()
    {
        Directory.CreateDirectory(_dir);
        var notes = Enumerable.Range(1, 9).Select(i => Note.Create($"n{i}")).ToList();
        File.WriteAllText(FilePath, JsonSerializer.Serialize(notes, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        Assert.Equal(5, new HistoryStore(FilePath).Notes.Count);
        Assert.Equal(["n1", "n2", "n3", "n4", "n5"], new HistoryStore(FilePath).Notes.Select(n => n.Text));
    }

    [Fact]
    public void FileIsPrivate()
    {
        new HistoryStore(FilePath).Add("secret");
        // On Windows privacy comes from the per-user %APPDATA% folder's ACL,
        // not from the file mode, so only the Unix mode can be checked here.
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(FilePath));
    }
}
