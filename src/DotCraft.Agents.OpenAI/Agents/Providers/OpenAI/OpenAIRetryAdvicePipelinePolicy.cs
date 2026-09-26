using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;

namespace DotCraft.Agents;

internal sealed class OpenAIRetryAdvicePipelinePolicy : PipelinePolicy
{
    private sealed record Advice(ProviderRetryDeadline? Deadline);
    private static readonly ConditionalWeakTable<PipelineResponse, Advice> Responses = new();

    internal static ProviderRetryDeadline? Capture(PipelineResponse? response, TimeProvider? clock = null)
    {
        if (response == null) return null;
        var time = clock ?? TimeProvider.System;
        return Responses.GetValue(response, value => new Advice(
            value.Headers.TryGetValue("retry-after", out var header)
                && ProviderFailureParsing.ParseRetryAfter(header, time.GetUtcNow()) is { } delay
                    ? ProviderRetryDeadline.FromDelay(delay, time) : null)).Deadline;
    }

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        try { ProcessNext(message, pipeline, currentIndex); }
        finally { Capture(message.Response); }
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        try { await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false); }
        finally { Capture(message.Response); }
    }
}
