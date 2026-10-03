using System.Runtime.CompilerServices;
using DotCraft.Sessions;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

internal sealed class PromptCacheWarmingChatClient(
    IChatClient innerClient,
    TimeSpan ttl,
    TimeProvider? timeProvider = null) : DelegatingChatClient(innerClient)
{
    internal const string DiagnosticName = "prompt_cache.warm";
    internal const long MinimumPromptTokens = 16_000;
    private static readonly TimeSpan MaximumWarmingDuration = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan MinimumMargin = TimeSpan.FromSeconds(10);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan _delay = TimeSpan.FromTicks(
        Math.Min((long)(ttl.Ticks * 0.9), (ttl - MinimumMargin).Ticks));

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var request = BeginRequest(messages, options);
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        if (request is not null)
            Arm(request, TokenUsageExtractor.FromResponse(response).InputTokens);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = BeginRequest(messages, options);
        long promptTokens = 0;
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken)
                           .ConfigureAwait(false))
        {
            if (request is not null)
            {
                foreach (var usage in update.Contents.OfType<UsageContent>())
                    promptTokens = Math.Max(promptTokens, TokenUsageExtractor.FromUsageContent(usage).InputTokens);
            }

            yield return update;
        }

        if (request is not null)
            Arm(request, promptTokens);
    }

    private WarmRequest? BeginRequest(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        if (PromptCacheWarmingScope.Current is not { } scope)
            return null;

        scope.Cancel();
        if (_delay <= TimeSpan.Zero
            || ProviderRequestContextScope.Current is not { } context
            || context.CurrentIdentity.RequestKind != ProviderRequestKind.Turn)
        {
            return null;
        }

        return new WarmRequest(
            scope,
            [.. messages],
            options?.Clone(),
            context.CurrentIdentity,
            context.Diagnostics,
            _time.GetUtcNow());
    }

    private void Arm(WarmRequest request, long promptTokens)
    {
        if (promptTokens < MinimumPromptTokens || !request.Scope.TryArm(out var cancellationToken))
            return;

        using (ExecutionContext.SuppressFlow())
            _ = Task.Run(() => WarmAsync(request, cancellationToken), CancellationToken.None);
    }

    private async Task WarmAsync(WarmRequest request, CancellationToken cancellationToken)
    {
        var dueAt = request.SentAt + _delay;
        var lateAfter = (ttl - _delay) / 2;
        while (true)
        {
            try
            {
                var wait = dueAt - _time.GetUtcNow();
                await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, _time, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var now = _time.GetUtcNow();
            if (now > dueAt + lateAfter)
            {
                Record(request, "late");
                return;
            }

            if (now - request.SentAt >= MaximumWarmingDuration)
                return;

            if (!await ReplayAsync(request, cancellationToken).ConfigureAwait(false))
                return;

            dueAt = now + _delay;
        }
    }

    private async Task<bool> ReplayAsync(WarmRequest request, CancellationToken cancellationToken)
    {
        var identity = request.Identity with { RequestKind = ProviderRequestKind.CacheWarm };
        using var auxiliaryScope = new AuxiliaryProviderRequestScope(identity, request.Diagnostics);
        using var retryScope = ModelStreamRetryRuntimeScope.Suppress();
        var options = request.Options?.Clone() ?? new ChatOptions();
        options.MaxOutputTokens = 1;
        try
        {
            var response = await base.GetResponseAsync(request.Messages, options, cancellationToken)
                .ConfigureAwait(false);
            var usage = TokenUsageExtractor.FromResponse(response);
            var succeeded = response.FinishReason is { } finishReason && finishReason != ChatFinishReason.ContentFilter;
            Record(
                request,
                succeeded ? "succeeded" : "incomplete",
                usage,
                succeeded ? null : response.FinishReason?.Value ?? "missing_finish_reason");
            return succeeded;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Record(request, "cancelled");
            return false;
        }
        catch (Exception ex)
        {
            Record(request, "failed", failureReason: ex.GetType().Name);
            return false;
        }
    }

    private static void Record(
        WarmRequest request,
        string outcome,
        TokenUsageSnapshot? usage = null,
        string? failureReason = null) =>
        request.Diagnostics?.Record(new ModelRuntimeDiagnostic(
            DiagnosticName,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["sessionKey"] = request.Identity.CurrentThreadId,
                ["turnId"] = request.Identity.TurnId,
                ["outcome"] = outcome,
                ["usage"] = usage,
                ["failureReason"] = failureReason
            }));

    private sealed record WarmRequest(
        PromptCacheWarmingScope Scope,
        IReadOnlyList<ChatMessage> Messages,
        ChatOptions? Options,
        ProviderConversationIdentity Identity,
        IModelRuntimeDiagnostics? Diagnostics,
        DateTimeOffset SentAt);
}
