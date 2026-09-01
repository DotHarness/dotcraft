using System.Text.Json.Nodes;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using SessionThread = DotCraft.Sessions.SessionThread;

namespace DotCraft.Plugins;

/// <summary>
/// Describes a plugin-provided function that can be exposed to the model as an <see cref="AIFunction"/>.
/// </summary>
public sealed record PluginFunctionDescriptor
{
    /// <summary>
    /// Stable plugin identifier. Built-in plugins use ids such as <c>browser</c>.
    /// </summary>
    public required string PluginId { get; init; }

    /// <summary>
    /// Stable function identifier within the plugin. When omitted, <see cref="Name"/>
    /// is used for compatibility with existing plugin manifests.
    /// </summary>
    public string? FunctionId { get; init; }

    /// <summary>
    /// Optional internal namespace. The MEAI-facing function name remains flat.
    /// </summary>
    public string? Namespace { get; init; }

    /// <summary>
    /// Flat function name exposed to Microsoft.Extensions.AI.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Description shown to the model.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// JSON Schema describing accepted function arguments.
    /// </summary>
    public JsonObject? InputSchema { get; init; }

    /// <summary>
    /// Optional JSON Schema describing structured function output.
    /// </summary>
    public JsonObject? OutputSchema { get; init; }

    /// <summary>
    /// Optional display metadata for clients.
    /// </summary>
    public PluginFunctionDisplay? Display { get; init; }

    /// <summary>
    /// Optional approval target metadata. Policy remains server-owned.
    /// </summary>
    public PluginFunctionApprovalDescriptor? Approval { get; init; }

    /// <summary>
    /// Whether the function needs the originating channel chat context to execute.
    /// </summary>
    public bool RequiresChatContext { get; init; }

    /// <summary>
    /// Reserved for future lazy-loading support. The current runtime records the value but does not apply lazy loading.
    /// </summary>
    public bool? DeferLoading { get; init; }
}

/// <summary>
/// Display metadata attached to a plugin function.
/// </summary>
public sealed record PluginFunctionDisplay
{
    public string? Title { get; init; }

    public string? Subtitle { get; init; }

    public string? Icon { get; init; }
}

/// <summary>
/// Describes which runtime argument should be guarded before dispatching a plugin function.
/// </summary>
public sealed record PluginFunctionApprovalDescriptor
{
    public string Kind { get; init; } = string.Empty;

    public string TargetArgument { get; init; } = string.Empty;

    public string? Operation { get; init; }

    public string? OperationArgument { get; init; }
}

/// <summary>
/// Result returned by a plugin function invoker.
/// </summary>
public sealed record PluginFunctionInvocationResult
{
    public bool Success { get; init; } = true;

    public IReadOnlyList<PluginFunctionContentItem>? ContentItems { get; init; }

    public JsonNode? StructuredResult { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public static PluginFunctionInvocationResult Failed(string errorCode, string errorMessage) =>
        new()
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            ContentItems = [new PluginFunctionContentItem { Type = "text", Text = $"{errorCode}: {errorMessage}" }]
        };
}

/// <summary>
/// Content item returned by a plugin function.
/// </summary>
public sealed record PluginFunctionContentItem
{
    public string Type { get; init; } = "text";

    public string? Text { get; init; }

    public string? DataBase64 { get; init; }

    /// <summary>Optional non-data URL for image content.</summary>
    public string? Url { get; init; }

    public string? MediaType { get; init; }
}

/// <summary>
/// A plugin function descriptor paired with its source-specific runtime invoker.
/// It does not depend on an ambient Session execution scope.
/// </summary>
public sealed record PluginToolRegistration(
    PluginFunctionDescriptor Descriptor,
    IPluginToolInvoker Invoker);

/// <summary>Executes one plugin-owned operation after common dispatch checks have completed.</summary>
public interface IPluginToolInvoker
{
    /// <summary>Invokes the plugin operation using the original provider call identifier.</summary>
    ValueTask<PluginFunctionInvocationResult> InvokeAsync(
        PluginToolInvocationContext context,
        CancellationToken cancellationToken);
}

/// <summary>Source-specific context supplied to a plugin runtime.</summary>
public sealed record PluginToolInvocationContext
{
    /// <summary>Gets the declared plugin function.</summary>
    public required PluginFunctionDescriptor Descriptor { get; init; }

    /// <summary>Gets the common immutable invocation context.</summary>
    public required ToolInvocationContext Invocation { get; init; }

    /// <summary>Gets a cloned arguments object.</summary>
    public required JsonObject Arguments { get; init; }

    /// <summary>Gets the originating channel when this is a channel plugin.</summary>
    public string? OriginChannel { get; init; }

    /// <summary>Gets the opaque originating channel conversation context.</summary>
    public string? ChannelContext { get; init; }

    /// <summary>Gets the channel sender identifier when available.</summary>
    public string? SenderId { get; init; }

    /// <summary>Gets the channel group identifier when available.</summary>
    public string? GroupId { get; init; }
}

/// <summary>Creates source-neutral tool sources that are scoped to one Session thread.</summary>
public interface IThreadPluginToolSourceProvider
{
    /// <summary>Gets deterministic provider ordering priority.</summary>
    int Priority => 100;

    /// <summary>Creates the plugin sources currently authorized for a thread.</summary>
    IReadOnlyList<IToolSource> CreateToolSourcesForThread(SessionThread thread);
}
