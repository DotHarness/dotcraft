using System.Text.Json.Nodes;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;

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
/// A plugin function descriptor paired with its invoker.
/// </summary>
public sealed record PluginFunctionRegistration(
    PluginFunctionDescriptor Descriptor,
    IPluginFunctionInvoker Invoker);

/// <summary>
/// Marker for AIFunction wrappers that represent a plugin function.
/// </summary>
public interface IPluginFunctionTool
{
    PluginFunctionDescriptor? PluginFunctionDescriptor { get; }
}

/// <summary>
/// Produces thread-scoped plugin functions.
/// </summary>
public interface IThreadPluginFunctionProvider
{
    int Priority => 100;

    IReadOnlyList<PluginFunctionRegistration> CreateFunctionsForThread(
        SessionThread thread,
        IReadOnlySet<string> reservedToolNames);
}

/// <summary>
/// Executes a plugin function invocation.
/// </summary>
public interface IPluginFunctionInvoker
{
    ValueTask<PluginFunctionInvocationResult> InvokeAsync(
        PluginFunctionInvocationContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// Runtime information passed to plugin function invokers.
/// </summary>
public sealed record PluginFunctionInvocationContext
{
    public required PluginFunctionDescriptor Descriptor { get; init; }

    public required PluginFunctionExecutionContext Execution { get; init; }

    public required string CallId { get; init; }

    public required JsonObject Arguments { get; init; }
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

    public string? MediaType { get; init; }
}
