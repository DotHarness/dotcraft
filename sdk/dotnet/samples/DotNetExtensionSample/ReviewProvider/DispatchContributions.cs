using System.Text.Json.Nodes;
using DotCraft.Tools;

namespace Acme.ReviewCore;

/// <summary>Policy is first-refusal-wins, so a denial returned here is the verbatim reason the caller sees.</summary>
internal sealed class ReviewInputLengthPolicy(ReviewSettings settings) : IToolPolicyEvaluator
{
    /// <summary>The error code an oversized review is denied with.</summary>
    internal const string InputTooLongCode = "ReviewInputTooLong";

    /// <inheritdoc />
    public ValueTask<ToolDispatchDecision> EvaluateAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        var tooLong = arguments.TryGetPropertyValue("text", out var node)
            && node is JsonValue value
            && value.TryGetValue<string>(out var text)
            && text.Length > settings.MaxInputLength;
        return ValueTask.FromResult(tooLong
            ? ToolDispatchDecision.Deny(
                InputTooLongCode,
                $"'{registration.Definition.Name}' exceeds the configured review input limit.")
            : ToolDispatchDecision.Allow);
    }
}

/// <summary>Refuses this plugin's one write Tool until the caller passes an explicit acknowledgement.</summary>
internal sealed class ReviewPublishApproval : IToolApprovalEvaluator
{
    /// <summary>The error code an unacknowledged publish is denied with.</summary>
    internal const string UnapprovedCode = "ReviewPublishUnapproved";

    /// <inheritdoc />
    public ValueTask<ToolDispatchDecision> RequestAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        JsonObject arguments,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            registration.Definition.Name.Name != ReviewToolNames.Publish || IsAcknowledged(arguments)
                ? ToolDispatchDecision.Allow
                : ToolDispatchDecision.Deny(
                    UnapprovedCode,
                    "Publishing a review needs an explicit 'approved' argument."));

    private static bool IsAcknowledged(JsonObject arguments) =>
        arguments.TryGetPropertyValue("approved", out var node)
        && node?.GetValueKind() == System.Text.Json.JsonValueKind.True;
}

/// <summary>Normalizers fold, each receiving the previous result, so this one stamps what the Host's own
/// normalizer already produced.</summary>
internal sealed class ReviewResultStamp : IToolResultNormalizer
{
    /// <summary>The line every successful review Tool result ends with.</summary>
    internal const string Stamp = "[reviewed by acme.review-core]";

    /// <inheritdoc />
    public ValueTask<ToolExecutionResult> NormalizeAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        ToolExecutionResult result,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            result.Success && registration.Definition.Name.Namespace == ReviewToolNames.Namespace
                ? new ToolExecutionResult(
                    result.Success,
                    $"{result.Content}{Environment.NewLine}{Stamp}",
                    result.StructuredContent,
                    result.Meta,
                    result.RawSourceResult,
                    result.Error,
                    result.ProviderResult,
                    result.ContentItems,
                    result.Directive)
                : result);
}

/// <summary>Joins the dispatch recorder chain so the plugin observes every Tool call, not only its own.</summary>
internal sealed class ReviewToolRecorder(ReviewJournal journal) : IToolInvocationRecorder
{
    /// <inheritdoc />
    public ValueTask RecordStartedAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        journal.Write("dispatch started");
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask RecordTerminalAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        ToolExecutionResult result,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        journal.Write($"dispatch finished success={result.Success} in {duration.TotalMilliseconds:F0}ms");
        return ValueTask.CompletedTask;
    }
}

/// <summary>Edits the tools a thread inherits without owning a source: masking removes a registration outright,
/// while a description rewrite leaves it dispatchable.</summary>
/// <remarks>Applied at snapshot assembly, so it reaches Tools the plugin did not contribute.</remarks>
internal sealed class ReviewToolRestriction(ReviewSettings settings) : IToolRestriction
{
    /// <inheritdoc />
    public string Name => "acme-review-surface";

    /// <inheritdoc />
    public ToolRestrictionEdit? Restrict(ToolRestrictionContext context) =>
        context.Definition.Name is { Namespace: ReviewToolNames.Namespace, Name: var local }
            ? local switch
            {
                // The write Tool never reaches the model; the approval stage still guards Host dispatch.
                ReviewToolNames.Publish => new ToolRestrictionEdit { Mask = true },
                ReviewToolNames.Summary => new ToolRestrictionEdit
                {
                    Description = $"Normalizes review text and appends the checklist, in a {settings.Tone} tone."
                },
                _ => null
            }
            : null;
}
