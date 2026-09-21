using DotCraft.Sessions;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

internal sealed partial class OpenAIResponsesProviderHistoryContext
{
    public async ValueTask AppendLocalInputAsync(
        IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AppendLocalInputCoreAsync(messages, null, cancellationToken).ConfigureAwait(false);
            _coveredSamplingMessageCount += GetSamplingProjection(messages).Count;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task AppendLocalInputCoreAsync(
        IReadOnlyList<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken)
    {
        var mapped = ResponsesToolSearchMapper.BuildInputItems(
            messages, options, BuildCallCorrelationIndex(), itemOrdinalOffset: _entries.Count);
        var entries = CreateEntries(mapped.Input, ProviderHistorySources.LocalInput, attemptId: null);
        if (entries.Count == 0)
            return;
        await PersistAppendAsync(entries, ProviderHistorySources.LocalInput, null, cancellationToken)
            .ConfigureAwait(false);
        _entries.AddRange(entries.Select(entry => new RuntimeEntry(entry, AttemptId: null)));
        _coveredThroughTurnId = _identity.TurnId;
    }
}
