using Microsoft.Extensions.AI;
using DotCraft.Sessions;

namespace DotCraft.Agents;

public sealed partial class StreamingFunctionInvokingChatClient
{
    private static Task NotifyAndWaitForStreamRetryAsync(
        ProviderFailure failure, Exception exception, int attempt, int budget, CancellationToken ct)
    {
        ModelStreamRetryRuntimeScope.Current?.NotifyRetry(new ModelStreamRetryNotification(attempt, budget, exception, failure));
        return ProviderFailureCapture.WaitForRetryAsync(failure, attempt, ct);
    }

    private readonly record struct StreamStep(bool HasNext, Exception? Failure);

    /// <summary>
    /// The yielding loop cannot host a catch block, so the capture has to live outside the
    /// iterator.
    /// </summary>
    private static async Task<StreamStep> MoveNextCapturingAsync(
        IAsyncEnumerator<ChatResponseUpdate> enumerator)
    {
        try
        {
            return new StreamStep(await enumerator.MoveNextAsync().ConfigureAwait(false), null);
        }
        catch (Exception ex)
        {
            return new StreamStep(false, ex);
        }
    }

    private static async ValueTask DisposeStreamEnumeratorAsync(
        IAsyncEnumerator<ChatResponseUpdate> enumerator,
        Exception? primaryFailure)
    {
        try
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
        catch when (primaryFailure != null)
        {
            // Preserve the stream failure that drives reissue and failure semantics.
        }
    }

    /// <summary>
    /// Argument previews are synthetic and must not reach history or the next provider request.
    /// </summary>
    private static void RemoveRemainingToolCallArgumentPreviews(
        List<ChatResponseUpdate> updates,
        Dictionary<ChatResponseUpdate, IReadOnlyList<ToolCallArgumentsDeltaContent>>? previewContentsByUpdate)
    {
        if (previewContentsByUpdate is not { Count: > 0 })
            return;

        foreach (var update in updates)
        {
            if (previewContentsByUpdate.TryGetValue(update, out var addedContents))
                RemoveToolCallArgumentPreviews(update, addedContents);
        }
    }
}
