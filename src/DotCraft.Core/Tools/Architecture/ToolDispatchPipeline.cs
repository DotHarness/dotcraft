using System.Text.Json.Nodes;
using DotCraft.Contributions;
using Microsoft.Extensions.AI;

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
public interface IToolPolicyEvaluator : IContributionContract
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

/// <summary>Resolves the common approval stage for a tool invocation. Contributed evaluators chain first-refusal-wins.</summary>
public interface IToolApprovalEvaluator : IContributionContract
{
    /// <summary>Returns whether execution may proceed.</summary>
    ValueTask<ToolDispatchDecision> RequestAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        JsonObject arguments,
        CancellationToken cancellationToken = default);
}

/// <summary>Projects started and terminal Session lifecycle records.</summary>
public interface IToolInvocationRecorder : IContributionContract
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
public interface IToolResultNormalizer : IContributionContract
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

public sealed class DefaultToolResultNormalizer(
    int maxModelContentCharacters = 100_000,
    string? defaultWorkspacePath = null,
    string? dataPath = null,
    int spillPreviewLines = 20) : IToolResultNormalizer
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

        var limit = ResolveResultLimit(registration, maxModelContentCharacters);
        if (result.Content is { Length: > 0 } content && limit > 0 && content.Length > limit)
        {
            var workspacePath = string.IsNullOrWhiteSpace(context.WorkspacePath)
                ? defaultWorkspacePath
                : context.WorkspacePath;
            var normalizedContent = string.IsNullOrWhiteSpace(workspacePath) || string.IsNullOrWhiteSpace(dataPath)
                ? BuildBoundedPreview(content, limit)
                : (string)ToolResultProcessor.Process(
                    registration.Definition.Name.Name,
                    content,
                    limit,
                    workspacePath,
                    dataPath,
                    context.ThreadId,
                    spillPreviewLines,
                    context.CallId)!;
            result = new ToolExecutionResult(
                result.Success,
                normalizedContent,
                result.StructuredContent,
                result.Meta,
                result.RawSourceResult,
                result.Error,
                result.ProviderResult,
                LimitRichText(result.ContentItems, normalizedContent),
                result.Directive);
        }

        if (result.Success
            && context.Audience.HasFlag(ToolInvocationAudience.Model)
            && string.IsNullOrWhiteSpace(result.Content))
        {
            result = new ToolExecutionResult(
                true,
                ToolResultProcessor.EmptyResultMessage(registration.Definition.Name.Name),
                result.StructuredContent,
                result.Meta,
                result.RawSourceResult,
                providerResult: result.ProviderResult,
                contentItems: result.ContentItems,
                directive: result.Directive);
        }

        return ValueTask.FromResult(new ToolExecutionResult(
            result.Success,
            result.Content,
            result.StructuredContent,
            result.Meta,
            result.RawSourceResult,
            result.Error,
            result.ProviderResult,
            result.ContentItems,
            result.Directive));
    }

    private static int ResolveResultLimit(ToolRegistration registration, int globalLimit) =>
        registration.Definition.Annotations.TryGetValue("dotcraft/maxResultChars", out var value)
        && value.TryGetInt32(out var perToolLimit)
            ? Math.Max(0, perToolLimit)
            : Math.Max(0, globalLimit);

    private static IReadOnlyList<AIContent>? LimitRichText(
        IReadOnlyList<AIContent>? contentItems,
        string normalizedContent)
    {
        if (contentItems is not { Count: > 0 })
            return null;

        var result = new List<AIContent>(contentItems.Count);
        var insertedText = false;
        foreach (var item in contentItems)
        {
            if (item is TextContent)
            {
                if (!insertedText)
                {
                    result.Add(new TextContent(normalizedContent));
                    insertedText = true;
                }
                continue;
            }
            result.Add(item);
        }
        if (!insertedText)
            result.Insert(0, new TextContent(normalizedContent));
        return result;
    }

    private static string BuildBoundedPreview(string content, int maximumCharacters)
    {
        if (maximumCharacters <= 0)
            return string.Empty;

        var marker = $"\n\n[Tool result truncated from {content.Length} characters.]\n\n";
        if (marker.Length >= maximumCharacters)
            return marker[..maximumCharacters];

        var available = maximumCharacters - marker.Length;
        var headLength = (available + 1) / 2;
        var tailLength = available - headLength;
        return string.Concat(
            content.AsSpan(0, headLength),
            marker,
            content.AsSpan(content.Length - tailLength, tailLength));
    }
}
