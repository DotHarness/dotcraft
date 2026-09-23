using System.Text.Json;

namespace DotCraft.SessionImport.Tests;

public sealed class SessionImportLedgerTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void WriteReplacesTheFileAtomicallyAndRoundTripsRecords()
    {
        var craft = _temp.CreateDirectory(".craft");
        var ledger = new SessionImportLedger(craft);
        var document = new SessionImportLedgerDocument();
        document.Upsert(Record("a", turnCount: 1));
        document.Upsert(Record("b", turnCount: 2));
        document.Upsert(Record("a", turnCount: 5));

        ledger.Write(document);
        ledger.Write(document);

        var imports = Path.Combine(craft, "imports");
        Assert.Equal(new[] { "sessions.json" }, Directory.GetFiles(imports).Select(Path.GetFileName));
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(imports, "sessions.json")));
        Assert.Equal(1, json.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(
            "2026-09-23T06:00:00.123Z",
            json.RootElement.GetProperty("records")[0].GetProperty("sourceModifiedAt").GetString());
        var read = Assert.IsType<SessionImportLedgerDocument>(ledger.TryRead());
        Assert.Equal(2, read.Records.Count);
        Assert.Equal(5, read.Find(SessionImportSources.ClaudeCode, "A")!.TurnCount);
        Assert.Null(read.Find(SessionImportSources.Codex, "a"));
    }

    private static SessionImportLedgerRecord Record(string sourceId, int turnCount) => new()
    {
        Source = SessionImportSources.ClaudeCode,
        SourceId = sourceId,
        SourcePath = $"/sessions/{sourceId}.jsonl",
        ThreadId = $"thread_import_claude-code_{sourceId}",
        ContentSha256 = "abc",
        SourceModifiedAt = new DateTimeOffset(2026, 9, 23, 6, 0, 0, 123, TimeSpan.Zero),
        ImportedAt = new DateTimeOffset(2026, 9, 23, 7, 0, 0, TimeSpan.Zero),
        TurnCount = turnCount,
        Title = sourceId
    };
}
