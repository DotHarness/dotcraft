using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    internal IThreadTitleGenerator? ThreadTitleGenerator { get; set; }

    private void ScheduleGeneratedThreadTitle(
        SessionThread thread,
        string provisionalTitle,
        string userMessage)
    {
        var generator = ThreadTitleGenerator;
        if (generator == null
            || thread.Turns.Count != 1
            || thread.ForkedFromId != null
            || !string.Equals(thread.Source.Kind, ThreadSourceKinds.User, StringComparison.Ordinal)
            || ThreadVisibility.IsInternal(thread))
        {
            return;
        }

        var request = new ThreadTitleGenerationRequest(
            thread.Id,
            provisionalTitle,
            userMessage,
            thread.Configuration?.ProviderId,
            thread.Configuration?.Model);
        _ = Task.Run(
            () => GenerateAndApplyThreadTitleAsync(generator, request),
            CancellationToken.None);
    }

    private async Task GenerateAndApplyThreadTitleAsync(
        IThreadTitleGenerator generator,
        ThreadTitleGenerationRequest request)
    {
        try
        {
            var generatedTitle = await generator.GenerateAsync(request, CancellationToken.None)
                .ConfigureAwait(false);
            if (generatedTitle == null)
            {
                Logger?.LogDebug(
                    "Thread title generation returned no valid title for thread {ThreadId}.",
                    request.ThreadId);
                return;
            }

            await TryApplyGeneratedThreadTitleAsync(
                    request.ThreadId,
                    request.ProvisionalTitle,
                    generatedTitle,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Logger?.LogDebug(
                "Thread title generation timed out for thread {ThreadId}.",
                request.ThreadId);
        }
        catch (Exception ex)
        {
            Logger?.LogDebug(
                ex,
                "Thread title generation failed for thread {ThreadId}.",
                request.ThreadId);
        }
    }

    internal async Task<bool> TryApplyGeneratedThreadTitleAsync(
        string threadId,
        string expectedTitle,
        string generatedTitle,
        CancellationToken cancellationToken)
    {
        return await InvokeThreadCommandAsync(
            threadId,
            async commandCt =>
            {
                var thread = await GetOrLoadThreadAsync(threadId, commandCt).ConfigureAwait(false);
                if (!string.Equals(thread.DisplayName, expectedTitle, StringComparison.Ordinal)
                    || string.Equals(thread.DisplayName, generatedTitle, StringComparison.Ordinal))
                {
                    return false;
                }

                thread.DisplayName = generatedTitle;
                await PersistThreadWithMaterializationAsync(thread, commandCt).ConfigureAwait(false);
                ThreadRenamedForBroadcast?.Invoke(thread);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }
}
