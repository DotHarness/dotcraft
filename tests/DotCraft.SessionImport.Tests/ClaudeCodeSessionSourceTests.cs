namespace DotCraft.SessionImport.Tests;

public sealed class ClaudeCodeSessionSourceTests : IDisposable
{
    private const string SessionId = "5d1b7c2e-0000-4000-8000-000000000001";
    private readonly TempDirectory _temp = new();
    private readonly string _workspace;
    private readonly string _root;

    public ClaudeCodeSessionSourceTests()
    {
        _workspace = _temp.CreateDirectory("repo");
        _root = _temp.CreateDirectory("claude");
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task ConvertsTranscriptIntoTurnsWithMergedMessagesAndToolText()
    {
        var longResult = new string('x', 4100);
        var path = Transcript(
            SessionId,
            new { type = "queue-operation", operation = "enqueue", sessionId = SessionId },
            new { type = "custom-title", customTitle = "Copied from another session", sessionId = "other-session" },
            User("u1", "2026-09-20T10:00:00.000Z", "<user_query>Fix the build</user_query>"),
            new { type = "user", isMeta = true, uuid = "m1", cwd = _workspace, message = new { content = "Caveat injected by the tool" } },
            Assistant("a1", "2026-09-20T10:00:01.000Z", "msg_1", new object[] { new { type = "thinking", thinking = "", signature = "sig" } }),
            Assistant("a2", "2026-09-20T10:00:02.000Z", "msg_1", new object[] { new { type = "text", text = "Looking at the build." } }),
            Assistant("a3", "2026-09-20T10:00:03.000Z", "msg_1", new object[]
            {
                new { type = "tool_use", id = "toolu_1", name = "Bash", input = new { command = "dotnet build", description = "Build the solution" } }
            }),
            new
            {
                type = "user", uuid = "u2", timestamp = "2026-09-20T10:00:04.000Z", cwd = _workspace,
                message = new { content = new object[] { new { type = "tool_result", tool_use_id = "toolu_1", content = longResult, is_error = true } } }
            },
            Assistant("a2", "2026-09-20T10:00:05.000Z", "msg_1", new object[] { new { type = "text", text = "Repeated uuid" } }),
            new { type = "assistant", isSidechain = true, uuid = "s1", cwd = _workspace, message = new { id = "msg_s", content = new object[] { new { type = "text", text = "Sub-agent" } } } },
            new
            {
                type = "user", isCompactSummary = true, uuid = "c1", timestamp = "2026-09-20T10:00:05.500Z", cwd = _workspace,
                message = new { content = "This session is being continued from a previous conversation." }
            },
            Assistant("a4", "2026-09-20T10:00:06.000Z", "msg_2", new object[]
            {
                new { type = "text", text = "Done." },
                new { type = "image", source = new { type = "base64", media_type = "image/png", data = "AAAA" } }
            }),
            User("u3", "2026-09-20T10:00:07.000Z", "Thanks"),
            new { type = "custom-title", customTitle = "Build fix", sessionId = SessionId },
            new { type = "ai-title", aiTitle = "Generated title", sessionId = SessionId });
        File.AppendAllText(path, "{\"type\":\"user\",\"broken\n{\"type\":\"user\",\"uuid\":\"u4\",\"message\":{\"content\":\"partial");

        var session = await LoadSingleAsync();

        Assert.Equal("Build fix", session.Title);
        Assert.Equal(_workspace, session.Cwd);
        Assert.Collection(
            session.Turns,
            first =>
            {
                Assert.Equal("Fix the build", first.UserText);
                Assert.Equal(
                    new[]
                    {
                        "Looking at the build.\n\n[external_agent_tool_call: Bash]\ndescription: Build the solution\ncommand: dotnet build\n[/external_agent_tool_call]",
                        $"[external_agent_tool_result: error]\n{new string('x', 3997)}...\n[/external_agent_tool_result]",
                        "Done.\n\n[external unsupported block: image]"
                    },
                    first.AgentTexts);
                Assert.Equal(DateTimeOffset.Parse("2026-09-20T10:00:00Z"), first.StartedAt);
                Assert.Equal(DateTimeOffset.Parse("2026-09-20T10:00:06Z"), first.CompletedAt);
            },
            second =>
            {
                Assert.Equal("Thanks", second.UserText);
                Assert.Empty(second.AgentTexts);
            });
    }

    [Fact]
    public async Task TitleFallsBackToAiTitleThenFirstUserLine()
    {
        Transcript(
            SessionId,
            User("u1", "2026-09-20T10:00:00.000Z", "<system-reminder>context</system-reminder>\n\n  Rename the parser\nwith details"),
            new { type = "ai-title", aiTitle = "  ", sessionId = SessionId });

        var session = await LoadSingleAsync();

        Assert.Equal("Rename the parser", session.Title);
    }

    [Fact]
    public async Task TruncatesUnknownToolInputAndLongTitles()
    {
        var longLine = new string('t', 130);
        Transcript(
            SessionId,
            User("u1", "2026-09-20T10:00:00.000Z", longLine),
            Assistant("a1", "2026-09-20T10:00:01.000Z", "msg_1", new object[]
            {
                new { type = "tool_use", id = "toolu_1", name = "Grep", input = new { pattern = new string('p', 2100) } }
            }));

        var session = await LoadSingleAsync();

        Assert.Equal(new string('t', 117) + "...", session.Title);
        var lines = Assert.Single(Assert.Single(session.Turns).AgentTexts).Split('\n');
        Assert.Equal("[external_agent_tool_call: Grep]", lines[0]);
        Assert.Equal("input: {\"pattern\":\"" + new string('p', 1997 - "{\"pattern\":\"".Length) + "...", lines[1]);
        Assert.Equal("[/external_agent_tool_call]", lines[2]);
    }

    [Fact]
    public async Task MembershipUsesFirstMessageCwdAndKeepsExistingSubfolders()
    {
        var subfolder = Directory.CreateDirectory(Path.Combine(_workspace, "src")).FullName;
        Transcript("in-subfolder", User("u1", "2026-09-20T10:00:00.000Z", "hello", subfolder));
        Transcript("outside", User("u1", "2026-09-20T10:00:00.000Z", "hello", _temp.CreateDirectory("elsewhere")));
        Transcript("missing-folder", User("u1", "2026-09-20T10:00:00.000Z", "hello", Path.Combine(_workspace, "gone")));
        Transcript("no-cwd", new { type = "user", uuid = "u1", message = new { content = "hello" } }, User("u2", "2026-09-20T10:00:00.000Z", "again"));
        Jsonl.Write(
            Path.Combine(_root, "projects", "repo", "in-subfolder", "subagents", "agent-a1.jsonl"),
            User("u1", "2026-09-20T10:00:00.000Z", "sub-agent", _workspace));

        var candidates = await new ClaudeCodeSessionSource(_root).EnumerateCandidatesAsync(Scopes.For(_workspace));

        var candidate = Assert.Single(candidates);
        Assert.Equal("in-subfolder", candidate.SourceId);
        Assert.Equal(subfolder, candidate.Cwd);
    }

    [Fact]
    public async Task WindowKeepsNewestSessionsModifiedWithinTheAge()
    {
        var now = DateTimeOffset.UtcNow;
        Jsonl.Touch(Transcript("newest", User("u1", "2026-09-20T10:00:00.000Z", "a")), now.AddDays(-1));
        Jsonl.Touch(Transcript("older", User("u1", "2026-09-20T10:00:00.000Z", "b")), now.AddDays(-2));
        Jsonl.Touch(Transcript("expired", User("u1", "2026-09-20T10:00:00.000Z", "c")), now.AddDays(-40));
        var source = new ClaudeCodeSessionSource(_root);

        var limited = await source.EnumerateCandidatesAsync(Scopes.For(_workspace, maxSessions: 1));
        var windowed = await source.EnumerateCandidatesAsync(Scopes.For(_workspace));

        Assert.Equal(new[] { "newest" }, limited.Select(static candidate => candidate.SourceId));
        Assert.Equal(new[] { "newest", "older" }, windowed.Select(static candidate => candidate.SourceId));
    }

    private async Task<ImportedSession> LoadSingleAsync()
    {
        var source = new ClaudeCodeSessionSource(_root);
        var candidate = Assert.Single(await source.EnumerateCandidatesAsync(Scopes.For(_workspace)));
        return Assert.IsType<ImportedSession>(await source.LoadAsync(candidate));
    }

    private string Transcript(string sessionId, params object[] records) =>
        Jsonl.Write(Path.Combine(_root, "projects", "repo", $"{sessionId}.jsonl"), records);

    private object User(string uuid, string timestamp, string content, string? cwd = null) =>
        new { type = "user", uuid, timestamp, cwd = cwd ?? _workspace, sessionId = SessionId, message = new { role = "user", content } };

    private object Assistant(string uuid, string timestamp, string messageId, object[] content) =>
        new { type = "assistant", uuid, timestamp, cwd = _workspace, sessionId = SessionId, message = new { id = messageId, role = "assistant", content } };
}
