using System.Text.Json;
using DotCraft.Sessions.Wire;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotCraft.Mcp;

internal static class McpLiveToolCatalogReader
{
    public static async ValueTask<IReadOnlyList<McpToolCatalog>> ReadAsync(
        McpClientManager manager,
        CancellationToken cancellationToken)
    {
        var catalogs = new List<McpToolCatalog>();
        foreach (var status in (await manager.ListStatusesAsync(cancellationToken))
                     .Where(static status => status.StartupState == "ready" && !status.Origin.IsBinding)
                     .OrderBy(static status => status.Name, StringComparer.Ordinal))
        {
            var inventory = await manager.GetInventoryAsync(status.Name, cancellationToken);
            if (inventory is null) continue;
            var config = await manager.GetConfigAsync(status.Name, cancellationToken);
            var approvalMode = config is { Origin.IsThread: false } ? config.ToolApprovalMode : McpToolApprovalMode.Auto;
            var protocol = inventory.Tools.OfType<McpClientTool>()
                .GroupBy(static tool => tool.Name, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.First().ProtocolTool, StringComparer.Ordinal);
            var tools = new List<McpCatalogTool>();
            foreach (var function in inventory.Tools.OfType<AIFunction>().OrderBy(static tool => tool.Name, StringComparer.Ordinal))
            {
                protocol.TryGetValue(function.Name, out var raw);
                if (!McpAppMetadataParser.TryParseToolMetadata(raw?.Meta, out var metadata))
                    metadata = new McpAppToolMetadata(null, McpAppVisibility.None);
                var hints = raw?.Annotations;
                var annotations = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                if (hints is not null)
                    annotations["mcp/toolAnnotations"] = JsonSerializer.SerializeToElement(hints, SessionWireJsonOptions.Default);
                tools.Add(new McpCatalogTool(
                    function.Name,
                    string.IsNullOrWhiteSpace(function.Description) ? function.Name : function.Description,
                    function.JsonSchema,
                    function.ReturnJsonSchema,
                    metadata,
                    new ToolPolicyHints(
                        RequiresApproval: RequiresApproval(approvalMode, hints),
                        ReadOnly: hints?.ReadOnlyHint == true,
                        Destructive: hints?.DestructiveHint != false,
                        OpenWorld: hints?.OpenWorldHint != false),
                    annotations,
                    inventory.ServerInstructions));
            }
            var generation = inventory.Generation;
            catalogs.Add(new McpToolCatalog($"server:{status.Name}", status.Name, config?.Origin.DeclaredName,
                config?.Origin.Kind ?? "workspace", tools, (id, rawName) => new ToolRuntimeBinding(
                    new RuntimeBindingId($"mcp:{status.Name}:{rawName}:{generation}"), id,
                    new McpToolRuntime(manager, status.Name, rawName, generation),
                    new McpGenerationLease(manager, status.Name, generation),
                    $"mcp:{status.Name}:{generation}", generation,
                    timeout: config?.ToolTimeoutSec is > 0 ? TimeSpan.FromSeconds(config.ToolTimeoutSec.Value) : null)));
        }
        return catalogs;
    }

    internal static bool RequiresApproval(McpToolApprovalMode mode, ToolAnnotations? hints) => mode switch
    {
        McpToolApprovalMode.Approve => false,
        McpToolApprovalMode.Prompt => true,
        _ => hints?.DestructiveHint == true
             || (hints?.ReadOnlyHint != true && (hints?.DestructiveHint != false || hints?.OpenWorldHint != false))
    };
}
