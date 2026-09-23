using System.Security.Cryptography;
using System.Text;
using DotCraft.Sessions;
using ImportSessionsCompletedNotification = DotCraft.Protocol.AppServer.ImportSessionsCompletedNotification;

namespace DotCraft.SessionImport.Tests;

public sealed class SessionImportServiceTests : IDisposable
{
    private static readonly DateTimeOffset FirstWrite = DateTimeOffset.UtcNow.AddHours(-2);
    private readonly TempDirectory _temp = new();
    private readonly FakeSessionService _sessions = new();
    private readonly FakeImportSource _source = new(SessionImportSources.ClaudeCode);
    private readonly string _workspace;
    private readonly string _craft;

    public SessionImportServiceTests()
    {
        _workspace = _temp.CreateDirectory("repo");
        _craft = _temp.CreateDirectory("repo", ".craft");
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task ImportsNewSessionAndSkipsItUntilTheSourceChanges()
    {
        _source.Put("s1", turnCount: 2, hash: "h1", _workspace, FirstWrite);
        var service = CreateService();

        var before = Assert.Single(await service.DetectAsync(null));
        var completed = await RunToCompletionAsync(service);
        var after = Assert.Single(await service.DetectAsync(null));

        Assert.Equal(1, before.ImportableCount);
        Assert.Equal("new", Assert.Single(before.Sessions).State);
        var outcome = Assert.Single(completed.Outcomes);
        Assert.Equal("imported", outcome.Status);
        Assert.Equal("manual", completed.Trigger);
        var request = Assert.Single(_sessions.ImportRequests);
        Assert.Equal(ExpectedThreadId("claude-code", "s1"), request.ThreadId);
        Assert.Equal(request.ThreadId, outcome.ThreadId.Value);
        Assert.Equal(ThreadImportConstants.ChannelName, request.Identity.ChannelName);
        Assert.Equal("local", request.Identity.UserId);
        Assert.Equal($"workspace:{_workspace}", request.Identity.ChannelContext);
        Assert.Equal(_workspace, request.Identity.WorkspacePath);
        Assert.Null(request.Cwd);
        Assert.Equal("Title s1", request.DisplayName);
        Assert.Equal(new[] { "question 1", "question 2" }, request.Turns.Select(static turn => turn.UserText));
        Assert.Equal(new[] { "answer 1", "answer 2" }, request.Turns.SelectMany(static turn => turn.AgentTexts));
        Assert.Equal(9, request.EstimatedTokens);
        Assert.Equal("claude-code", request.Metadata["dotcraft.import.source"]);
        Assert.Equal("s1", request.Metadata["dotcraft.import.sessionId"]);
        Assert.True(DateTimeOffset.TryParse(request.Metadata["dotcraft.import.importedAt"], out _));
        var record = ReadLedger().Find(SessionImportSources.ClaudeCode, "s1")!;
        Assert.Equal(("h1", 2, request.ThreadId), (record.ContentSha256, record.TurnCount, record.ThreadId));
        Assert.Empty(after.Sessions);
        Assert.Equal(0, after.ImportableCount);
    }

    [Fact]
    public async Task TouchedButUnchangedSessionRefreshesItsModificationTimeOnce()
    {
        _source.Put("s1", turnCount: 1, hash: "h1", _workspace, FirstWrite);
        var service = CreateService();
        await RunToCompletionAsync(service);
        var touchedAt = FirstWrite.AddMinutes(5);
        _source.Put("s1", turnCount: 1, hash: "h1", _workspace, touchedAt);

        var first = Assert.Single(await service.DetectAsync(null));
        var second = Assert.Single(await service.DetectAsync(null));

        Assert.Equal("current", Assert.Single(first.Sessions).State);
        Assert.Empty(second.Sessions);
        var record = ReadLedger().Find(SessionImportSources.ClaudeCode, "s1")!;
        Assert.True(SessionImportLedger.SameInstant(touchedAt, record.SourceModifiedAt!.Value));
        Assert.Equal(("h1", 1), (record.ContentSha256, record.TurnCount));
    }

    [Fact]
    public async Task ImportsSubfolderSessionsWithTheirWorkingDirectory()
    {
        var subfolder = _temp.CreateDirectory("repo", "web");
        _source.Put("s1", turnCount: 1, hash: "h1", subfolder, FirstWrite);

        await RunToCompletionAsync(CreateService());

        Assert.Equal(subfolder, Assert.Single(_sessions.ImportRequests).Cwd);
    }

    [Fact]
    public async Task GrownSessionIsAppendedWithTheImportedPrefix()
    {
        _source.Put("s1", turnCount: 2, hash: "h1", _workspace, FirstWrite);
        var service = CreateService();
        await RunToCompletionAsync(service);
        _source.Put("s1", turnCount: 3, hash: "h2", _workspace, FirstWrite.AddMinutes(5));

        var detection = Assert.Single(await service.DetectAsync(null));
        var completed = await RunToCompletionAsync(service);

        Assert.Equal("changed", Assert.Single(detection.Sessions).State);
        Assert.Equal("appended", Assert.Single(completed.Outcomes).Status);
        var append = Assert.Single(_sessions.AppendRequests);
        Assert.Equal(ExpectedThreadId("claude-code", "s1"), append.ThreadId);
        Assert.Equal(new[] { "question 1", "question 2" }, append.ExistingTurns.Select(static turn => turn.UserText));
        Assert.Equal("question 3", Assert.Single(append.NewTurns).UserText);
        var record = ReadLedger().Find(SessionImportSources.ClaudeCode, "s1")!;
        Assert.Equal(("h2", 3), (record.ContentSha256, record.TurnCount));
    }

    [Fact]
    public async Task ContinuedArchivedAndDeletedThreadsAreDeferred()
    {
        foreach (var id in new[] { "continued", "archived", "deleted" })
            _source.Put(id, turnCount: 1, hash: "h1", _workspace, FirstWrite);
        var service = CreateService();
        await RunToCompletionAsync(service);
        var continued = _sessions.Threads[ExpectedThreadId("claude-code", "continued")];
        _sessions.AddTurns(continued, [FakeImportSource.Turn(9)], "dotcraft-desktop");
        _sessions.Threads[ExpectedThreadId("claude-code", "archived")].Status = ThreadStatus.Archived;
        _sessions.Threads.Remove(ExpectedThreadId("claude-code", "deleted"));
        foreach (var id in new[] { "continued", "archived", "deleted" })
            _source.Put(id, turnCount: 2, hash: "h2", _workspace, FirstWrite.AddMinutes(5));

        var detection = Assert.Single(await service.DetectAsync(null));
        var completed = await RunToCompletionAsync(service);

        Assert.All(detection.Sessions, candidate => Assert.Equal("deferred", candidate.State));
        Assert.Equal(3, detection.Sessions.Count);
        Assert.Equal(0, detection.ImportableCount);
        Assert.Empty(completed.Outcomes);
        Assert.Empty(_sessions.AppendRequests);
    }

    [Fact]
    public async Task RefusedAppendIsDeferredAndLeavesTheLedgerUntouched()
    {
        _source.Put("s1", turnCount: 2, hash: "h1", _workspace, FirstWrite);
        var service = CreateService();
        await RunToCompletionAsync(service);
        _source.Put("s1", turnCount: 3, hash: "h2", _workspace, FirstWrite.AddMinutes(5));
        _sessions.RefuseAppends = true;

        var completed = await RunToCompletionAsync(service);

        var outcome = Assert.Single(completed.Outcomes);
        Assert.Equal("deferred", outcome.Status);
        Assert.Equal(ThreadImportRefusedException.ErrorCode, outcome.ErrorCode.Value);
        var record = ReadLedger().Find(SessionImportSources.ClaudeCode, "s1")!;
        Assert.Equal(("h1", 2), (record.ContentSha256, record.TurnCount));
    }

    [Fact]
    public async Task SecondRunWhileAPassIsRunningIsBusy()
    {
        _source.Put("s1", turnCount: 1, hash: "h1", _workspace, FirstWrite);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sessions.ImportGate = gate.Task;
        var service = CreateService();
        var completed = WaitForCompletionAsync(service);

        service.Run([SessionImportSources.ClaudeCode], null);
        var busy = Assert.Throws<SessionImportException>(() => service.Run([SessionImportSources.ClaudeCode], null));
        gate.SetResult();

        Assert.Equal(SessionImportErrorCodes.Busy, busy.Code);
        Assert.Equal("imported", Assert.Single((await completed).Outcomes).Status);
    }

    [Fact]
    public async Task UnavailableSourceIsReportedByDetectionAndRefusedByRun()
    {
        _source.IsAvailable = false;
        var service = CreateService();

        var detection = Assert.Single(await service.DetectAsync(null));
        var refused = Assert.Throws<SessionImportException>(() => service.Run([SessionImportSources.ClaudeCode], null));

        Assert.False(detection.Available);
        Assert.Empty(detection.Sessions);
        Assert.Equal(SessionImportErrorCodes.SourceUnavailable, refused.Code);
        await Assert.ThrowsAsync<ArgumentException>(() => service.DetectAsync(["unknown-agent"]));
    }

    [Fact]
    public async Task CorruptLedgerIsRebuiltFromImportedThreadMetadata()
    {
        AddImportedThread("s1", turnCount: 2);
        AddImportedThread("s2", turnCount: 1);
        _source.Put("s1", turnCount: 2, hash: "h1", _workspace, FirstWrite);
        _source.Put("s2", turnCount: 3, hash: "h2", _workspace, FirstWrite);
        Directory.CreateDirectory(Path.Combine(_craft, "imports"));
        File.WriteAllText(Path.Combine(_craft, "imports", "sessions.json"), "{\"version\":1,\"records\":[{\"source\":\"codex\"}]}");
        var service = CreateService();

        var detection = Assert.Single(await service.DetectAsync(null));

        Assert.Equal("current", detection.Sessions.Single(static candidate => candidate.SourceId == "s1").State);
        Assert.Equal("changed", detection.Sessions.Single(static candidate => candidate.SourceId == "s2").State);
        var ledger = ReadLedger();
        Assert.Equal("h1", ledger.Find(SessionImportSources.ClaudeCode, "s1")!.ContentSha256);
        Assert.Null(ledger.Find(SessionImportSources.ClaudeCode, "s2")!.ContentSha256);
    }

    [Fact]
    public async Task ExternalCliSessionsFromThreadMetadataReachTheSources()
    {
        _sessions.Threads["thread_parent"] = new SessionThread
        {
            Id = "thread_parent",
            WorkspacePath = _workspace,
            Status = ThreadStatus.Active,
            Metadata = new Dictionary<string, string>
            {
                ["dotcraft.externalCliSessions"] =
                    "[{\"profileName\":\"codex\",\"workingDirectory\":\"x\",\"sessionId\":\"cli-session-1\",\"lastUpdatedAt\":\"2026-09-20T10:00:00Z\"}]"
            }
        };

        await CreateService().DetectAsync(null);

        Assert.Contains("cli-session-1", _source.LastScope!.ExternalCliSessionIds);
    }

    private SessionImportService CreateService()
    {
        var service = new SessionImportService(
            new SessionImportServiceOptions(_workspace, _craft, new SessionImportConfig()),
            new SessionImportSettingsStore(_temp.Combine("user-config.json"), Path.Combine(_craft, "config.json")),
            [_source]);
        service.SetSessionService(_sessions);
        return service;
    }

    private void AddImportedThread(string sessionId, int turnCount)
    {
        var thread = new SessionThread
        {
            Id = ExpectedThreadId("claude-code", sessionId),
            WorkspacePath = _workspace,
            OriginChannel = ThreadImportConstants.ChannelName,
            Status = ThreadStatus.Active,
            Metadata = new Dictionary<string, string>
            {
                ["dotcraft.import.source"] = "claude-code",
                ["dotcraft.import.sessionId"] = sessionId,
                ["dotcraft.import.importedAt"] = "2026-09-20T10:00:00.000Z"
            }
        };
        _sessions.AddTurns(thread, Enumerable.Range(1, turnCount).Select(FakeImportSource.Turn), ThreadImportConstants.ChannelName);
        _sessions.Threads[thread.Id] = thread;
    }

    private SessionImportLedgerDocument ReadLedger() =>
        new SessionImportLedger(_craft).TryRead() ?? throw new InvalidOperationException("The ledger was not written.");

    private static async Task<ImportSessionsCompletedNotification> RunToCompletionAsync(SessionImportService service)
    {
        var completed = WaitForCompletionAsync(service);
        var importId = service.Run([SessionImportSources.ClaudeCode], null);
        var result = await completed;
        Assert.Equal(importId, result.ImportId);
        return result;
    }

    private static Task<ImportSessionsCompletedNotification> WaitForCompletionAsync(SessionImportService service)
    {
        var completion = new TaskCompletionSource<ImportSessionsCompletedNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCompleted(ImportSessionsCompletedNotification notification)
        {
            service.Completed -= OnCompleted;
            completion.TrySetResult(notification);
        }

        service.Completed += OnCompleted;
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private static string ExpectedThreadId(string source, string sessionId) =>
        $"thread_import_{source}_{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{source}:{sessionId}")))[..16]}";
}
