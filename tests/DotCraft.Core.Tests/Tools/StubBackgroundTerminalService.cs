using DotCraft.Tools.BackgroundTerminals;

namespace DotCraft.Tests.Tools;

internal sealed class StubBackgroundTerminalService : IBackgroundTerminalService
{
    public event Action<BackgroundTerminalEvent>? TerminalEvent;

    public Func<BackgroundTerminalStartRequest, CancellationToken, Task<BackgroundTerminalSnapshot>>? StartHandler { get; init; }

    public Task<BackgroundTerminalSnapshot> StartAsync(
        BackgroundTerminalStartRequest request,
        CancellationToken ct = default) =>
        StartHandler?.Invoke(request, ct)
        ?? Task.FromResult(new BackgroundTerminalSnapshot
        {
            SessionId = "term_stub",
            ThreadId = request.ThreadId,
            TurnId = request.TurnId,
            CallId = request.CallId,
            Command = request.Command,
            WorkingDirectory = request.WorkingDirectory,
            Source = request.Source,
            Status = BackgroundTerminalStatus.Completed,
            Output = "ok",
            OutputPath = Path.Combine(request.WorkingDirectory, "term_stub.log"),
            ExitCode = 0,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            OriginalOutputChars = 2
        });

    public Task<BackgroundTerminalSnapshot> ReadAsync(string sessionId, int waitMs = 0, int? maxOutputChars = null, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<BackgroundTerminalSnapshot> WriteStdinAsync(string sessionId, string input, int yieldTimeMs = 1000, int? maxOutputChars = null, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<BackgroundTerminalSnapshot>> ListAsync(string? threadId = null, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<BackgroundTerminalSnapshot> StopAsync(string sessionId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<BackgroundTerminalSnapshot>> CleanThreadAsync(string threadId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<string>> DeleteThreadArtifactsAsync(string threadId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<int> CleanupExpiredArtifactsAsync(CancellationToken ct = default) =>
        throw new NotSupportedException();

    public void Raise(BackgroundTerminalEvent terminalEvent) => TerminalEvent?.Invoke(terminalEvent);
}
