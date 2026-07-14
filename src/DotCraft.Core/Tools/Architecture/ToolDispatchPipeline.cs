using System.Text.Json.Nodes;

namespace DotCraft.Tools;

/// <summary>A source-neutral allow or deny decision used by dispatcher policy stages.</summary>
public sealed record ToolDispatchDecision(bool Allowed, ToolError? Error = null)
{
    /// <summary>Reusable allow decision.</summary>
    public static ToolDispatchDecision Allow { get; } = new(true);

    /// <summary>Creates a deny decision.</summary>
    public static ToolDispatchDecision Deny(string code, string message) =>
        new(false, new ToolError(code, message));
}

/// <summary>Checks live authority references after the binding lease check.</summary>
public interface IToolAuthorityEvaluator
{
    /// <summary>Evaluates the current authority for the resolved invocation.</summary>
    ValueTask<ToolDispatchDecision> CheckAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        CancellationToken cancellationToken = default);
}

/// <summary>Applies server-authoritative mode, thread, native guard, and annotation policy.</summary>
public interface IToolPolicyEvaluator
{
    /// <summary>Evaluates policy without trusting source hints to expand authority.</summary>
    ValueTask<ToolDispatchDecision> EvaluateAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        JsonObject arguments,
        CancellationToken cancellationToken = default);
}

/// <summary>Runs common PreToolUse and terminal tool hooks.</summary>
public interface IToolDispatchHookRunner
{
    /// <summary>Runs the pre-use hook.</summary>
    ValueTask<ToolDispatchDecision> RunPreToolUseAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        JsonObject arguments,
        CancellationToken cancellationToken = default);

    /// <summary>Runs PostToolUse or PostToolUseFailure.</summary>
    ValueTask RunTerminalAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        ToolExecutionResult result,
        CancellationToken cancellationToken = default);
}

/// <summary>Resolves the single common approval stage for a tool invocation.</summary>
public interface IToolApprovalEvaluator
{
    /// <summary>Returns whether execution may proceed.</summary>
    ValueTask<ToolDispatchDecision> RequestAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        JsonObject arguments,
        CancellationToken cancellationToken = default);
}

/// <summary>Projects started and terminal Session lifecycle records.</summary>
public interface IToolInvocationRecorder
{
    /// <summary>Records the started lifecycle projection.</summary>
    ValueTask RecordStartedAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        JsonObject arguments,
        CancellationToken cancellationToken = default);

    /// <summary>Records exactly one terminal lifecycle projection.</summary>
    ValueTask RecordTerminalAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        ToolExecutionResult result,
        TimeSpan duration,
        CancellationToken cancellationToken = default);
}

/// <summary>Normalizes model/client/host audiences and enforces common result limits.</summary>
public interface IToolResultNormalizer
{
    /// <summary>Normalizes and validates the source result.</summary>
    ValueTask<ToolExecutionResult> NormalizeAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        ToolExecutionResult result,
        CancellationToken cancellationToken = default);
}

internal sealed class AllowAllToolAuthorityEvaluator : IToolAuthorityEvaluator
{
    public ValueTask<ToolDispatchDecision> CheckAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(ToolDispatchDecision.Allow);
}

internal sealed class AllowAllToolPolicyEvaluator : IToolPolicyEvaluator
{
    public ValueTask<ToolDispatchDecision> EvaluateAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        JsonObject arguments,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(ToolDispatchDecision.Allow);
}

internal sealed class NoopToolDispatchHookRunner : IToolDispatchHookRunner
{
    public ValueTask<ToolDispatchDecision> RunPreToolUseAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        JsonObject arguments,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(ToolDispatchDecision.Allow);

    public ValueTask RunTerminalAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        ToolExecutionResult result,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

internal sealed class PolicyHintApprovalEvaluator : IToolApprovalEvaluator
{
    public ValueTask<ToolDispatchDecision> RequestAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        JsonObject arguments,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(registration.Definition.PolicyHints.RequiresApproval
            ? ToolDispatchDecision.Deny(
                ToolErrorCodes.ApprovalRejected,
                $"Tool '{registration.Definition.Name}' requires an approval service.")
            : ToolDispatchDecision.Allow);
}

internal sealed class NoopToolInvocationRecorder : IToolInvocationRecorder
{
    public ValueTask RecordStartedAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        JsonObject arguments,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask RecordTerminalAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        ToolExecutionResult result,
        TimeSpan duration,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

internal sealed class DefaultToolResultNormalizer(int maxModelContentCharacters = 100_000) : IToolResultNormalizer
{
    public ValueTask<ToolExecutionResult> NormalizeAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        ToolExecutionResult result,
        CancellationToken cancellationToken = default)
    {
        if ((!result.Success && result.Error is null) || (result.Success && result.Error is not null))
        {
            return ValueTask.FromResult(ToolExecutionResult.Failed(new ToolError(
                ToolErrorCodes.ResultInvalid,
                $"Tool '{registration.Definition.Name}' returned inconsistent success and error state.")));
        }

        if (result.Content is { Length: > 0 } content && content.Length > maxModelContentCharacters)
        {
            return ValueTask.FromResult(ToolExecutionResult.Failed(new ToolError(
                ToolErrorCodes.ResultInvalid,
                $"Tool '{registration.Definition.Name}' exceeded the model result size limit.")));
        }

        if (result.Success && context.Audience.HasFlag(ToolInvocationAudience.Model) &&
            string.IsNullOrWhiteSpace(result.Content))
        {
            return ValueTask.FromResult(ToolExecutionResult.Failed(new ToolError(
                ToolErrorCodes.ResultInvalid,
                $"Tool '{registration.Definition.Name}' returned no model-visible content.")));
        }

        return ValueTask.FromResult(new ToolExecutionResult(
            result.Success,
            result.Content,
            result.StructuredContent,
            result.Meta,
            result.RawSourceResult,
            result.Error));
    }
}
