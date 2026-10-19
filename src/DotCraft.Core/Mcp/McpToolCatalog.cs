using System.Text.Json;
using DotCraft.Tools;

namespace DotCraft.Mcp;

public interface IMcpToolCatalogContributor
{
    ValueTask<IReadOnlyList<McpToolCatalog>> GetCatalogsAsync(
        ToolPlanningContext context,
        McpClientManager? manager,
        CancellationToken cancellationToken = default);
}

public sealed record McpCatalogTool(
    string SourceToolName,
    string Description,
    JsonElement InputSchema,
    JsonElement? ReturnSchema,
    McpAppToolMetadata AppMetadata,
    ToolPolicyHints PolicyHints,
    IReadOnlyDictionary<string, JsonElement> Annotations,
    string? NamespaceDescription);

public sealed record McpToolCatalog(
    string SourceIdentity,
    string RuntimeName,
    string? DeclaredName,
    string Origin,
    IReadOnlyList<McpCatalogTool> Tools,
    Func<ToolDefinitionId, string, ToolRuntimeBinding> BindRuntime);
