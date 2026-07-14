using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Protocol;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotCraft.Mcp;

/// <summary>Contributes connected MCP tools with exact server and raw source identities.</summary>
public sealed class McpToolSource(McpClientManager manager, AppConfig config) : IToolSource
{
    /// <inheritdoc />
    public string SourceId => "mcp";

    /// <inheritdoc />
    public int Priority => 80;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context,
        CancellationToken cancellationToken = default)
    {
        var statuses = await manager.ListStatusesAsync(cancellationToken);
        var deferredConfig = config.Tools.DeferredLoading;
        var registrations = new List<ToolRegistration>();

        foreach (var status in statuses
                     .Where(static status => string.Equals(status.StartupState, "ready", StringComparison.Ordinal))
                     .OrderBy(static status => status.Name, StringComparer.Ordinal))
        {
            var inventory = await manager.GetInventoryAsync(status.Name, cancellationToken);
            var serverConfig = await manager.GetConfigAsync(status.Name, cancellationToken);
            if (inventory == null)
                continue;
            var generation = inventory.Generation;

            var protocolTools = inventory.Tools
                .OfType<McpClientTool>()
                .GroupBy(static tool => tool.Name, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.First().ProtocolTool, StringComparer.Ordinal);
            var functions = ToolSchemaSanitizer.SanitizeTools(inventory.Tools)
                .OfType<AIFunction>()
                .OrderBy(static function => function.Name, StringComparer.Ordinal)
                .ToArray();
            var useDeferred = deferredConfig.Strategy != AppConfig.DeferredLoadingStrategy.Off
                              && functions.Length >= deferredConfig.DeferThreshold;
            var alwaysLoaded = deferredConfig.AlwaysLoadedTools.ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var function in functions)
            {
                var sourceToolId = new SourceToolId(function.Name);
                var definitionId = new ToolDefinitionId(ToolSourceKind.Mcp, status.Name, sourceToolId);
                protocolTools.TryGetValue(function.Name, out var protocolTool);
                var toolAnnotations = protocolTool?.Annotations;
                var appMetadataValid = McpAppMetadataParser.TryParseToolMetadata(
                    protocolTool?.Meta,
                    out var appMetadata);
                if (!appMetadataValid)
                    appMetadata = new McpAppToolMetadata(null, McpAppVisibility.None);
                var definitionAnnotations = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    [McpAppMetadataParser.ToolAnnotationKey] =
                        McpAppMetadataParser.ToDefinitionAnnotation(appMetadata)
                };
                if (toolAnnotations is not null)
                {
                    definitionAnnotations["mcp/toolAnnotations"] = JsonSerializer.SerializeToElement(
                        toolAnnotations,
                        SessionWireJsonOptions.Default);
                }
                var definition = new ToolDefinition(
                    definitionId,
                    McpToolNaming.CanonicalToolName(status.Name, function.Name),
                    string.IsNullOrWhiteSpace(function.Description) ? function.Name : function.Description,
                    function.JsonSchema,
                    function.ReturnJsonSchema,
                    annotations: definitionAnnotations,
                    policyHints: new ToolPolicyHints(
                        RequiresApproval: toolAnnotations?.DestructiveHint != false
                                          || toolAnnotations?.OpenWorldHint != false,
                        ReadOnly: toolAnnotations?.ReadOnlyHint == true,
                        Destructive: toolAnnotations?.DestructiveHint != false,
                        OpenWorld: toolAnnotations?.OpenWorldHint != false),
                    provenance: new ToolProvenance(
                        ToolSourceKind.Mcp,
                        status.Name,
                        serverConfig?.Origin.Kind ?? "workspace"));
                var binding = new ToolRuntimeBinding(
                    new RuntimeBindingId($"mcp:{status.Name}:{function.Name}:{generation}"),
                    definitionId,
                    new McpToolRuntime(manager, status.Name, function.Name, generation),
                    new McpGenerationLease(manager, status.Name, generation),
                    $"mcp:{status.Name}:{generation}",
                    generation,
                    timeout: serverConfig?.ToolTimeoutSec is > 0
                        ? TimeSpan.FromSeconds(serverConfig.ToolTimeoutSec.Value)
                        : null);
                var modelVisible = appMetadata.Visibility.HasFlag(McpAppVisibility.Model);
                var isDeferred = modelVisible && useDeferred && !alwaysLoaded.Contains(function.Name);
                var (exposure, audiences) = ResolvePublication(appMetadata, isDeferred);
                registrations.Add(new ToolRegistration(
                    definition,
                    binding,
                    exposure,
                    audiences,
                    deferred: isDeferred
                        ? new DeferredToolDescriptor(status.Name, $"{function.Name} {function.Description}")
                        : null));
            }
        }

        return registrations;
    }

    internal static (ToolExposure Exposure, ToolInvocationAudience Audiences) ResolvePublication(
        McpAppToolMetadata metadata,
        bool deferred)
    {
        var modelVisible = metadata.Visibility.HasFlag(McpAppVisibility.Model);
        var exposure = !modelVisible
            ? ToolExposure.Hidden
            : deferred
                ? ToolExposure.Deferred
                : ToolExposure.Direct;
        var audiences = ToolInvocationAudience.Host;
        if (modelVisible)
            audiences |= ToolInvocationAudience.Model;
        if (metadata.Visibility.HasFlag(McpAppVisibility.App))
            audiences |= ToolInvocationAudience.App;
        return (exposure, audiences);
    }
}

internal sealed class McpGenerationLease(
    McpClientManager manager,
    string serverName,
    long generation) : IToolBindingLease
{
    public async ValueTask<ToolBindingLeaseResult> CheckAsync(
        ToolInvocationContext context,
        CancellationToken cancellationToken = default)
    {
        var current = await manager.GetGenerationAsync(serverName, cancellationToken);
        return current == generation
            ? ToolBindingLeaseResult.Available
            : ToolBindingLeaseResult.Unavailable(
                $"MCP server '{serverName}' generation {generation} is no longer active.");
    }
}

internal sealed class McpToolRuntime(
    McpClientManager manager,
    string serverName,
    string rawToolName,
    long generation) : IToolRuntime
{
    public async ValueTask<ToolExecutionResult> InvokeAsync(
        ToolInvocationContext context,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sourceArguments = arguments.ToDictionary(
                static pair => pair.Key,
                static pair => (object?)pair.Value?.Deserialize<object>(SessionWireJsonOptions.Default),
                StringComparer.Ordinal);
            var result = await manager.CallToolAsync(
                serverName,
                rawToolName,
                sourceArguments,
                generation,
                cancellationToken);
            var raw = JsonSerializer.SerializeToElement(result, SessionWireJsonOptions.Default);
            var structured = result.StructuredContent == null
                ? (JsonElement?)null
                : JsonSerializer.SerializeToElement(result.StructuredContent, SessionWireJsonOptions.Default);
            var meta = result.Meta == null
                ? (JsonElement?)null
                : JsonSerializer.SerializeToElement(result.Meta, SessionWireJsonOptions.Default);
            var normalized = NormalizeModelContent(result);
            if (result.IsError == true)
            {
                return new ToolExecutionResult(
                    false,
                    normalized,
                    structured,
                    meta,
                    raw,
                    new ToolError(ToolErrorCodes.ExecutionFailed,
                        string.IsNullOrWhiteSpace(normalized) ? "The MCP tool returned an error." : normalized));
            }

            return ToolExecutionResult.Succeeded(normalized, structured, meta, raw);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
        {
            return ToolExecutionResult.Failed(new ToolError(
                ToolErrorCodes.McpReauthenticationRequired,
                ex.Message));
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not TimeoutException)
        {
            return ToolExecutionResult.Failed(new ToolError(ToolErrorCodes.McpProtocolError, ex.Message));
        }
    }

    private static string? NormalizeModelContent(CallToolResult result)
    {
        var element = JsonSerializer.SerializeToElement(result.Content, SessionWireJsonOptions.Default);
        if (element.ValueKind != JsonValueKind.Array)
            return null;
        var parts = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var type) && type.GetString() == "text" &&
                item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                parts.Add(text.GetString()!);
            }
            else if (item.TryGetProperty("type", out type) && type.GetString() is { } contentType)
            {
                parts.Add($"[{contentType} content]");
            }
        }

        return parts.Count == 0 ? null : string.Join("\n", parts);
    }
}
