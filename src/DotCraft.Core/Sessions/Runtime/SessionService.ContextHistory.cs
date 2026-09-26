using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private static bool TryAppendFailedTurnTailToSession(List<ChatMessage> session, SessionTurn turn)
    {
        var history = session;
        var turnTail = ThreadStore.BuildModelVisibleHistoryFromTurn(turn);
        if (turnTail.Count == 0)
            return true;

        var overlap = FindHistoryTailOverlap(history, turnTail);
        if (overlap >= turnTail.Count)
            return true;

        var merged = new List<ChatMessage>(history.Count + turnTail.Count - overlap);
        merged.AddRange(history);
        for (var i = overlap; i < turnTail.Count; i++)
            merged.Add(turnTail[i]);

        session.Clear();
        session.AddRange(merged);
        return true;
    }

    private async Task<List<ChatMessage>?> TryLoadSurvivingModelHistoryAsync(
        string threadId,
        CancellationToken ct)
    {
        try
        {
            return await persistence.LoadModelHistoryAsync(threadId, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to reload model history after rollback for thread {ThreadId}", threadId);
        }

        return null;
    }

    private async Task SaveContextUsageFromSessionAsync(
        SessionThread thread,
        List<ChatMessage>? session,
        CancellationToken ct)
    {
        try
        {
            var tokens = 0L;
            var source = "history_estimate";
            if (session is not null && TrySnapshotInMemoryHistory(session, out var history) && history.Count > 0)
            {
                var visibleHistory = PrepareProviderVisibleHistory(history);
                if (await TryEstimateNativeCompactedContextTokensAsync(thread, visibleHistory, ct) is { } nativeTokens)
                {
                    tokens = nativeTokens;
                    source = "provider_compacted_estimate";
                }
                else
                {
                    tokens = MessageTokenEstimator.Estimate(visibleHistory);
                }
            }

            await SaveReplacementContextUsageSnapshotAsync(
                thread.Id,
                tokens,
                source,
                ct: ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to update context usage after rollback for thread {ThreadId}", thread.Id);
        }
    }

    // An active provider-native replacement has no neutral expansion; estimating the full
    // transcript would report occupancy the provider does not hold.
    private async Task<long?> TryEstimateNativeCompactedContextTokensAsync(
        SessionThread thread,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct)
    {
        if (thread.ProviderHistorySchemaVersion != ProviderHistorySchema.CurrentSchemaVersion
            || thread.HistoryMode != HistoryMode.Server
            || thread.Ephemeral)
        {
            return null;
        }

        var currentConfig = _appConfigMonitor?.Current ?? agentFactory.RuntimeContext.Config;
        var runtime = agentFactory.RuntimeContext.ChatClientRegistry.ResolveMainRuntime(
            currentConfig,
            thread.Configuration?.ProviderId,
            thread.Configuration?.Model);
        if (!string.Equals(runtime.Protocol, ModelProviderProtocols.OpenAIResponses, StringComparison.Ordinal))
            return null;
        var factory = agentFactory.RuntimeContext.ChatClientRegistry
            .GetProviderService<IProviderHistorySessionFactory>(runtime);
        if (factory == null)
            return null;

        var identity = ThreadConversationIdentity.Create(
            thread,
            ResolveNewestTerminalTurn(thread),
            GetOrCreateResponsesContextWindow(thread.Id).CurrentWindowId,
            ProviderRequestKind.Compaction);
        var snapshot = ToOpaqueHistory(
            runtime,
            identity,
            await persistence.LoadProviderHistoryAsync(thread, identity.ContextWindowId, ct).ConfigureAwait(false));
        if (!snapshot.IsNativeCompacted)
            return null;

        var context = factory.CreateSession(identity, snapshot, history, sink: null);
        // The thread agent's options carry the instructions and tools every request pays for.
        var options = GetThreadAgentOrDefault(thread.Id).ChatOptions;
        return context.TryEstimateActiveContextTokens(history, options, out var tokens) ? tokens : null;
    }

    private static int FindHistoryTailOverlap(
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ChatMessage> tail)
    {
        var max = Math.Min(history.Count, tail.Count);
        for (var length = max; length > 0; length--)
        {
            var historyStart = history.Count - length;
            var matched = true;
            for (var i = 0; i < length; i++)
            {
                if (ChatMessagesEquivalent(history[historyStart + i], tail[i]))
                    continue;

                matched = false;
                break;
            }

            if (matched)
                return length;
        }

        return 0;
    }

    private static bool ChatMessagesEquivalent(ChatMessage left, ChatMessage right)
    {
        if (left.Role != right.Role)
            return false;

        var leftContents = BuildContentSignatures(left);
        var rightContents = BuildContentSignatures(right);
        return leftContents.SequenceEqual(rightContents, StringComparer.Ordinal);
    }

    private static List<string> BuildContentSignatures(ChatMessage message)
    {
        var signatures = new List<string>();
        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case TextContent text:
                {
                    var normalized = NormalizeTextForHistorySignature(text.Text);
                    if (!string.IsNullOrWhiteSpace(normalized))
                        signatures.Add("text:" + normalized);
                    break;
                }
                case TextReasoningContent reasoning:
                {
                    if (ReasoningContentHelper.TryGetText(reasoning, out var reasoningText) &&
                        !string.IsNullOrWhiteSpace(reasoningText))
                    {
                        signatures.Add("reasoning:" + reasoningText.Trim());
                    }

                    break;
                }
                case FunctionCallContent call:
                    signatures.Add($"call:{call.CallId}:{call.Name}");
                    break;
                case FunctionResultContent result:
                    signatures.Add(
                        $"result:{result.CallId}:{ImageContentSanitizingChatClient.DescribeResult(result.Result)}");
                    break;
                default:
                    signatures.Add($"{content.GetType().FullName}:{content}");
                    break;
            }
        }

        return signatures;
    }

    private static string NormalizeTextForHistorySignature(string? text)
    {
        var normalized = StripSystemReminderBlocks(text).Trim();
        var runtimeContextIndex = normalized.IndexOf("\n[Runtime Context]", StringComparison.Ordinal);
        if (runtimeContextIndex >= 0)
            normalized = normalized[..runtimeContextIndex].Trim();
        return normalized;
    }
}
