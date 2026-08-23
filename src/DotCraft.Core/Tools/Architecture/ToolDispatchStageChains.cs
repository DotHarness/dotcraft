using System.Text.Json.Nodes;
using DotCraft.Contributions;
using Microsoft.Extensions.Logging;

namespace DotCraft.Tools;

/// <summary>Composes the ordered contributions of a dispatch stage contribution point into one stage instance: policy and approval are first-refusal-wins, recorder is a fan-out, normalizer is a fold.</summary>
internal static class ToolDispatchStageChains
{
    /// <summary>Composes a policy chain, or returns the fallback when nothing is contributed.</summary>
    internal static IToolPolicyEvaluator Policy(
        IReadOnlyList<IToolPolicyEvaluator>? chain,
        IToolPolicyEvaluator fallback) => chain switch
        {
            null or { Count: 0 } => fallback,
            { Count: 1 } => chain[0],
            _ => new PolicyChain(chain)
        };

    /// <summary>Composes an approval chain, or returns the fallback when nothing is contributed.</summary>
    internal static IToolApprovalEvaluator Approval(
        IReadOnlyList<IToolApprovalEvaluator>? chain,
        IToolApprovalEvaluator fallback) => chain switch
        {
            null or { Count: 0 } => fallback,
            { Count: 1 } => chain[0],
            _ => new ApprovalChain(chain)
        };

    /// <summary>Composes a recorder fan-out, or returns the fallback when nothing is contributed. A lone recorder is wrapped too, so containment of a throwing recorder does not depend on chain length.</summary>
    internal static IToolInvocationRecorder Recorder(
        IReadOnlyList<IToolInvocationRecorder>? chain,
        IToolInvocationRecorder fallback,
        ILogger? logger) => chain switch
        {
            null or { Count: 0 } => fallback,
            _ => new RecorderChain(chain, logger)
        };

    /// <summary>Composes a normalizer fold, or returns the fallback when nothing is contributed.</summary>
    internal static IToolResultNormalizer Normalizer(
        IReadOnlyList<IToolResultNormalizer>? chain,
        IToolResultNormalizer fallback) => chain switch
        {
            null or { Count: 0 } => fallback,
            { Count: 1 } => chain[0],
            _ => new NormalizerChain(chain)
        };

    private sealed class PolicyChain(IReadOnlyList<IToolPolicyEvaluator> stages) : IToolPolicyEvaluator
    {
        public async ValueTask<ToolDispatchDecision> EvaluateAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            await ContributionRead.FirstOpinionAsync(
                stages,
                async (stage, token) =>
                {
                    var decision = await stage
                        .EvaluateAsync(context, registration, arguments, token)
                        .ConfigureAwait(false);
                    return decision.Allowed ? null : decision;
                },
                cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? ToolDispatchDecision.Allow;
    }

    private sealed class ApprovalChain(IReadOnlyList<IToolApprovalEvaluator> stages) : IToolApprovalEvaluator
    {
        public async ValueTask<ToolDispatchDecision> RequestAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            await ContributionRead.FirstOpinionAsync(
                stages,
                async (stage, token) =>
                {
                    var decision = await stage
                        .RequestAsync(context, registration, arguments, token)
                        .ConfigureAwait(false);
                    return decision.Allowed ? null : decision;
                },
                cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? ToolDispatchDecision.Allow;
    }

    private sealed class RecorderChain(IReadOnlyList<IToolInvocationRecorder> stages, ILogger? logger)
        : IToolInvocationRecorder
    {
        public ValueTask RecordStartedAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            ContributionRead.FanoutAsync(
                stages,
                (stage, token) => stage.RecordStartedAsync(context, registration, arguments, token),
                (stage, exception) => Report(exception, stage, context, "started"),
                cancellationToken);

        public ValueTask RecordTerminalAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            ToolExecutionResult result,
            TimeSpan duration,
            CancellationToken cancellationToken = default) =>
            ContributionRead.FanoutAsync(
                stages,
                (stage, token) => stage.RecordTerminalAsync(context, registration, result, duration, token),
                (stage, exception) => Report(exception, stage, context, "terminal"),
                cancellationToken);

        private void Report(
            Exception exception,
            IToolInvocationRecorder recorder,
            ToolInvocationContext context,
            string phase) =>
            logger?.LogError(
                exception,
                "Tool invocation recorder {RecorderType} threw while recording the {RecordPhase} projection of {ToolName} and was skipped.",
                recorder.GetType().FullName,
                phase,
                context.ToolName);
    }

    private sealed class NormalizerChain(IReadOnlyList<IToolResultNormalizer> stages) : IToolResultNormalizer
    {
        public ValueTask<ToolExecutionResult> NormalizeAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            ToolExecutionResult result,
            CancellationToken cancellationToken = default) =>
            ContributionRead.FoldAsync(
                stages,
                result,
                (current, stage, token) => stage.NormalizeAsync(context, registration, current, token),
                cancellationToken: cancellationToken);
    }
}
