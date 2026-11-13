using DotCraft.Oratorio.Api;

namespace DotCraft.Oratorio.Tests;

internal sealed partial class FakeAppServerClient
{
    private string? _activeTurnId;

    public Action<string, string?> CaptureModelSelection { get; init; } = (_, _) => { };

    public Task<string?> StartTurnAsync(string threadId, IReadOnlyList<TurnInputPartDto> input, string? modelId, CancellationToken ct)
    {
        CaptureModelSelection(threadId, modelId);
        return StartTurnAsync(threadId, input.FirstOrDefault()?.Text ?? "", ct);
    }

    public Task<string?> EnqueueTurnAsync(string threadId, IReadOnlyList<TurnInputPartDto> input, CancellationToken ct) =>
        Task.FromResult<string?>("queued-test-1");

    public Task SteerTurnAsync(string threadId, string expectedTurnId, IReadOnlyList<TurnInputPartDto> input, CancellationToken ct)
    {
        AcceptGuidance(threadId, expectedTurnId, input.FirstOrDefault()?.Text ?? "");
        return Task.CompletedTask;
    }

    private void AcceptGuidance(string threadId, string turnId, string prompt)
    {
        captureSteer(new FakeAppServerSteer(threadId, turnId, prompt));
        _steered.TrySetResult(prompt);
    }
}
