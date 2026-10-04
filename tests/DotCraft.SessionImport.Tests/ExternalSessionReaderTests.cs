namespace DotCraft.SessionImport.Tests;

public sealed class ExternalSessionReaderTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _workspace;
    private readonly string _root;

    public ExternalSessionReaderTests()
    {
        _workspace = _temp.CreateDirectory("repo");
        _root = _temp.CreateDirectory("claude");
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task ListsWorkspaceSessionsNewestFirstAndRefreshesAChangedOne()
    {
        var now = DateTimeOffset.UtcNow;
        Jsonl.Touch(Transcript("older", User("older", "Explain the parser"), Assistant("older", "It reads JSONL.")), now.AddHours(-2));
        var newer = Transcript("newer", User("newer", "Fix the build"));
        Jsonl.Touch(newer, now.AddHours(-1));
        Transcript("elsewhere", User("elsewhere", "Not this project", _temp.CreateDirectory("other")));
        var reader = new ExternalSessionReader([new ClaudeCodeSessionSource(_root)]);

        var listed = await reader.ListAsync(Scopes.For(_workspace));

        Assert.Equal(["newer", "older"], listed.Select(static summary => summary.SourceId));
        var older = listed[1];
        Assert.Equal(SessionImportSources.ClaudeCode, older.Source);
        Assert.Equal("Explain the parser", older.Title);
        Assert.Equal("Explain the parser", older.FirstRequest);
        Assert.Equal(1, older.TurnCount);
        Assert.Equal(_workspace, older.Cwd);

        File.AppendAllText(newer, Jsonl.Line(User("newer", "And the tests")) + "\n");
        Jsonl.Touch(newer, now);

        Assert.Equal(2, (await reader.ListAsync(Scopes.For(_workspace)))[0].TurnCount);
    }

    [Fact]
    public async Task ReadsOneCandidateAndNothingOutsideTheScope()
    {
        Transcript("mine", User("mine", "Fix the build"), Assistant("mine", "Built."));
        Transcript("elsewhere", User("elsewhere", "Not this project", _temp.CreateDirectory("other")));
        var reader = new ExternalSessionReader([new ClaudeCodeSessionSource(_root)]);

        var session = await reader.ReadAsync(Scopes.For(_workspace), SessionImportSources.ClaudeCode, "mine");

        var turn = Assert.Single(Assert.IsType<ImportedSession>(session).Turns);
        Assert.Equal("Fix the build", turn.UserText);
        Assert.Equal(["Built."], turn.AgentTexts);
        Assert.Null(await reader.ReadAsync(Scopes.For(_workspace), SessionImportSources.ClaudeCode, "elsewhere"));
        Assert.Null(await reader.ReadAsync(Scopes.For(_workspace), SessionImportSources.Codex, "mine"));
    }

    private string Transcript(string sessionId, params object[] records) =>
        Jsonl.Write(Path.Combine(_root, "projects", "repo", $"{sessionId}.jsonl"), records);

    private object User(string sessionId, string content, string? cwd = null) =>
        new { type = "user", uuid = Guid.NewGuid().ToString(), timestamp = "2026-09-20T10:00:00.000Z", cwd = cwd ?? _workspace, sessionId, message = new { role = "user", content } };

    private object Assistant(string sessionId, string text) =>
        new { type = "assistant", uuid = Guid.NewGuid().ToString(), timestamp = "2026-09-20T10:00:01.000Z", cwd = _workspace, sessionId, message = new { id = Guid.NewGuid().ToString(), role = "assistant", content = new object[] { new { type = "text", text } } } };
}
