using System.Security.Cryptography;
using System.Text;
using DotCraft.Sessions;

namespace DotCraft.SessionImport.Tests;

public sealed class CodexSessionSourceTests : IDisposable
{
    private const string ThreadId = "01990000-0000-7000-8000-000000000001";
    private const string SegmentRolloutId = "01990000-0000-7000-8000-0000000000ff";
    private readonly TempDirectory _temp = new();
    private readonly string _workspace;
    private readonly string _root;

    public CodexSessionSourceTests()
    {
        _workspace = _temp.CreateDirectory("repo");
        _root = _temp.CreateDirectory("codex");
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task ConvertsPaginatedItemsAndUsesTheSessionIndexTitle()
    {
        Rollout(
            $"rollout-2026-09-20T10-00-00-{ThreadId}.jsonl",
            Meta(ThreadId),
            Event("2026-09-20T10:00:01.000Z", new { type = "task_started", turn_id = "t1" }),
            Item("2026-09-20T10:00:02.000Z", "t1", new
            {
                type = "UserMessage", id = "i1",
                content = new object[] { new { type = "text", text = "List files" }, new { type = "local_image", path = "shot.png" } }
            }),
            Response("2026-09-20T10:00:03.000Z", new { type = "custom_tool_call", call_id = "c1", name = "exec", input = "ls" }),
            Item("2026-09-20T10:00:04.000Z", "t1", new { type = "Reasoning", id = "i2", summary_text = new[] { "thinking" } }),
            Item("2026-09-20T10:00:05.000Z", "t1", new
            {
                type = "CommandExecution", id = "i3", command = new[] { "bash", "-lc", "ls" },
                status = "completed", aggregated_output = "a.txt", exit_code = 0
            }),
            Item("2026-09-20T10:00:06.000Z", "t1", new
            {
                type = "FileChange", id = "i4", status = "completed", stdout = "Success",
                changes = new Dictionary<string, object> { ["src/a.cs"] = new { type = "update", unified_diff = "@@" } }
            }),
            Item("2026-09-20T10:00:07.000Z", "t1", new
            {
                type = "McpToolCall", id = "i5", server = "github", tool = "search",
                arguments = new { query = "x" }, status = "failed", error = new { message = "denied" }
            }),
            Item("2026-09-20T10:00:08.000Z", "t1", new { type = "AgentMessage", id = "i6", content = new[] { new { type = "Text", text = "Here are the files." } } }),
            Item("2026-09-20T10:00:09.000Z", "t1", new { type = "AgentMessage", id = "i6", content = new[] { new { type = "Text", text = "Here are the files." } } }),
            Event("2026-09-20T10:00:10.000Z", new { type = "task_complete", turn_id = "t1" }));
        Jsonl.Write(
            Path.Combine(_root, "session_index.jsonl"),
            new { id = ThreadId, thread_name = "Old name" },
            new { id = "other", thread_name = "Other" },
            new { id = ThreadId, thread_name = "Listing files" });

        var session = await LoadSingleAsync();

        Assert.Equal("Listing files", session.Title);
        var turn = Assert.Single(session.Turns);
        Assert.Equal("List files\n\n[external unsupported block: local_image]", turn.UserText);
        Assert.Equal(
            new[]
            {
                "[external_agent_tool_call: shell]\ncommand: bash -lc ls\n[/external_agent_tool_call]\n\n[external_agent_tool_result]\na.txt\n[/external_agent_tool_result]",
                "[external_agent_tool_call: apply_patch]\nfile: src/a.cs\n[/external_agent_tool_call]\n\n[external_agent_tool_result]\nSuccess\n[/external_agent_tool_result]",
                "[external_agent_tool_call: mcp__github__search]\ninput: {\"query\":\"x\"}\n[/external_agent_tool_call]\n\n[external_agent_tool_result: error]\ndenied\n[/external_agent_tool_result]",
                "Here are the files."
            },
            turn.AgentTexts);
        Assert.Equal(DateTimeOffset.Parse("2026-09-20T10:00:02Z"), turn.StartedAt);
        Assert.Equal(DateTimeOffset.Parse("2026-09-20T10:00:08Z"), turn.CompletedAt);
    }

    [Fact]
    public async Task ConvertsLegacyEventsAndResponseItemToolCalls()
    {
        Rollout(
            $"rollout-2026-09-20T10-00-00-{ThreadId}.jsonl",
            Meta(ThreadId, source: "cli", threadSource: null, historyMode: null),
            Response("2026-09-20T10:00:01.000Z", new
            {
                type = "message", role = "user",
                content = new[] { new { type = "input_text", text = "<environment_context>injected</environment_context>" } }
            }),
            Event("2026-09-20T10:00:02.000Z", new { type = "user_message", message = "Run tests" }),
            Response("2026-09-20T10:00:03.000Z", new { type = "function_call", name = "shell", arguments = "{\"command\":[\"dotnet\",\"test\"]}", call_id = "c1" }),
            Response("2026-09-20T10:00:04.000Z", new { type = "function_call_output", call_id = "c1", output = "Passed!" }),
            Event("2026-09-20T10:00:05.000Z", new { type = "agent_message", message = "All tests pass." }),
            Response("2026-09-20T10:00:05.000Z", new
            {
                type = "message", role = "assistant", content = new[] { new { type = "output_text", text = "All tests pass." } }
            }),
            Item("2026-09-20T10:00:06.000Z", "t1", new { type = "AgentMessage", id = "x", content = new[] { new { type = "Text", text = "Paginated only" } } }));

        var session = await LoadSingleAsync();

        Assert.Equal("Run tests", session.Title);
        var turn = Assert.Single(session.Turns);
        Assert.Equal("Run tests", turn.UserText);
        Assert.Equal(
            new[]
            {
                "[external_agent_tool_call: shell]\ncommand: dotnet test\n[/external_agent_tool_call]",
                "[external_agent_tool_result]\nPassed!\n[/external_agent_tool_result]",
                "All tests pass."
            },
            turn.AgentTexts);
    }

    [Fact]
    public async Task RevertedThreadReadsTheAncestorPrefixBeforeItsOwnRecords()
    {
        var prefix = Jsonl.Lines(
            Meta(ThreadId),
            UserItem("2026-09-19T09:00:01.000Z", "t1", "u1", "First question"),
            AgentItem("2026-09-19T09:00:02.000Z", "t1", "a1", "First answer"));
        var basePath = Jsonl.WriteText(
            RolloutPath($"rollout-2026-09-19T09-00-00-{ThreadId}.jsonl"),
            prefix + Jsonl.Lines(
                UserItem("2026-09-19T09:00:03.000Z", "t2", "u2", "Abandoned question"),
                AgentItem("2026-09-19T09:00:04.000Z", "t2", "a2", "Abandoned answer")));
        var endByteOffset = Encoding.UTF8.GetByteCount(prefix);
        var segmentPath = Rollout(
            $"rollout-2026-09-20T10-00-00-{ThreadId}_{SegmentRolloutId}.jsonl",
            Meta(ThreadId, historyBase: new { thread_id = ThreadId, end_ordinal_exclusive = 3, end_byte_offset = endByteOffset }),
            UserItem("2026-09-20T10:00:01.000Z", "t3", "u3", "Second question"),
            AgentItem("2026-09-20T10:00:02.000Z", "t3", "a3", "Second answer"));

        var session = await LoadSingleAsync();

        Assert.Equal(segmentPath, session.SourcePath);
        Assert.Equal(new[] { "First question", "Second question" }, session.Turns.Select(static turn => turn.UserText));
        Assert.Equal(new[] { "First answer", "Second answer" }, session.Turns.SelectMany(static turn => turn.AgentTexts));
        var lineageBytes = File.ReadAllBytes(basePath).Take(endByteOffset).Concat(File.ReadAllBytes(segmentPath)).ToArray();
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(lineageBytes)), session.ContentSha256);
    }

    [Fact]
    public async Task MissingAncestorSkipsTheThread()
    {
        Rollout(
            $"rollout-2026-09-20T10-00-00-{ThreadId}.jsonl",
            Meta(ThreadId, historyBase: new { thread_id = SegmentRolloutId, end_ordinal_exclusive = 2, end_byte_offset = 10 }),
            UserItem("2026-09-20T10:00:01.000Z", "t1", "u1", "Question"));
        var source = new CodexSessionSource(_root);

        var candidate = Assert.Single(await source.EnumerateCandidatesAsync(Scopes.For(_workspace)));

        Assert.Null(await source.LoadAsync(candidate));
    }

    [Fact]
    public async Task ExcludesNonUserSourcesDotCraftSubAgentsAndCodexImports()
    {
        Session("01990000-0000-7000-8000-000000000002", Meta("01990000-0000-7000-8000-000000000002", source: "exec"));
        Session("01990000-0000-7000-8000-000000000003", Meta("01990000-0000-7000-8000-000000000003", threadSource: "subagent"));
        Session("01990000-0000-7000-8000-000000000004", Meta(
            "01990000-0000-7000-8000-000000000004",
            source: new { subagent = new { thread_spawn = new { parent_thread_id = ThreadId } } }));
        Session("01990000-0000-7000-8000-000000000005", Meta("01990000-0000-7000-8000-000000000005", source: new { custom = "ide-extension" }));
        Session("01990000-0000-7000-8000-000000000006", Meta("01990000-0000-7000-8000-000000000006"));
        Session(
            "01990000-0000-7000-8000-000000000007",
            Meta("01990000-0000-7000-8000-000000000007"),
            AgentItem("2026-09-20T10:00:03.000Z", "t1", "a9", ThreadImportConstants.Marker));
        var source = new CodexSessionSource(_root);

        var candidates = await source.EnumerateCandidatesAsync(
            Scopes.For(_workspace, externalCliSessionIds: "01990000-0000-7000-8000-000000000006"));

        Assert.Equal(
            new[] { "01990000-0000-7000-8000-000000000005", "01990000-0000-7000-8000-000000000007" },
            candidates.Select(static candidate => candidate.SourceId).Order(StringComparer.Ordinal));
        var imported = candidates.Single(static candidate => candidate.SourceId.EndsWith('7'));
        Assert.Null(await source.LoadAsync(imported));
    }

    private void Session(string id, object meta, params object[] extra) =>
        Rollout(
            $"rollout-2026-09-20T10-00-00-{id}.jsonl",
            [meta, UserItem("2026-09-20T10:00:01.000Z", "t1", "u1", "Question"), .. extra]);

    private async Task<ImportedSession> LoadSingleAsync()
    {
        var source = new CodexSessionSource(_root);
        var candidate = Assert.Single(await source.EnumerateCandidatesAsync(Scopes.For(_workspace)));
        return Assert.IsType<ImportedSession>(await source.LoadAsync(candidate));
    }

    private string RolloutPath(string name) => Path.Combine(_root, "sessions", "2026", "09", "20", name);

    private string Rollout(string name, params object[] records) => Jsonl.Write(RolloutPath(name), records);

    private object Meta(
        string id,
        object? source = null,
        string? threadSource = "user",
        string? historyMode = "paginated",
        object? historyBase = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["cwd"] = _workspace,
            ["source"] = source ?? "vscode"
        };
        if (threadSource is not null)
            payload["thread_source"] = threadSource;
        if (historyMode is not null)
            payload["history_mode"] = historyMode;
        if (historyBase is not null)
            payload["history_base"] = historyBase;
        return new { timestamp = "2026-09-20T10:00:00.000Z", type = "session_meta", payload };
    }

    private static object UserItem(string timestamp, string turnId, string itemId, string text) =>
        Item(timestamp, turnId, new { type = "UserMessage", id = itemId, content = new[] { new { type = "text", text } } });

    private static object AgentItem(string timestamp, string turnId, string itemId, string text) =>
        Item(timestamp, turnId, new { type = "AgentMessage", id = itemId, content = new[] { new { type = "Text", text } } });

    private static object Item(string timestamp, string turnId, object item) =>
        new { timestamp, type = "event_msg", payload = new { type = "item_completed", thread_id = ThreadId, turn_id = turnId, item } };

    private static object Event(string timestamp, object payload) => new { timestamp, type = "event_msg", payload };

    private static object Response(string timestamp, object payload) => new { timestamp, type = "response_item", payload };
}
