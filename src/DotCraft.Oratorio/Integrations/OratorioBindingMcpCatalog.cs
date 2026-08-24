using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace DotCraft.Oratorio.Integrations;

/// <summary>MCP-only metadata projected over the shared attribute-authored board descriptors.</summary>
public static class OratorioBindingMcpCatalog
{
    public const string AppId = "com.dotharness.oratorio";
    public const string UiResourcePrefix = "ui://oratorio";
    public const string BoardUiResourceUri = $"{UiResourcePrefix}/board.html";
    public const string ItemUiResourceUri = $"{UiResourcePrefix}/item.html";
    public const string ReviewUiResourceUri = $"{UiResourcePrefix}/review.html";
    public const string BoardNamespaceDescription = "Inspect and manage the authorized Oratorio project board. Read current board state before making claims; use mutation tools only when the user asks to change Oratorio state.";

    private static readonly string[] ModelAndAppVisibility = ["model", "app"];

    public static IReadOnlyList<Tool> McpBoardTools(OratorioDynamicToolCatalog tools) =>
        tools.BoardDescriptors.Select(descriptor =>
        {
            string? resourceUri = descriptor.LocalName switch
            {
                OratorioDynamicToolCatalog.ListBoardItemsName => BoardUiResourceUri,
                OratorioDynamicToolCatalog.GetBoardItemName => ItemUiResourceUri,
                OratorioDynamicToolCatalog.QueueReviewRoundName => ReviewUiResourceUri,
                _ => null
            };
            return new Tool
            {
                Name = descriptor.LocalName,
                Description = descriptor.Description,
                InputSchema = descriptor.InputSchema,
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = descriptor.LocalName is
                        OratorioDynamicToolCatalog.ListBoardItemsName or
                        OratorioDynamicToolCatalog.GetBoardItemName,
                    DestructiveHint = false,
                    OpenWorldHint = false
                },
                Meta = resourceUri is null ? null : UiMeta(resourceUri)
            };
        }).ToArray();

    public static IReadOnlyList<Resource> McpAppResources() =>
    [
        McpResource(BoardUiResourceUri, "Oratorio board", "board.html"),
        McpResource(ItemUiResourceUri, "Oratorio item", "item.html"),
        McpResource(ReviewUiResourceUri, "Oratorio review", "review.html")
    ];

    public static string? ResolveUiFile(string? uri) => uri switch
    {
        BoardUiResourceUri => "board.html",
        ItemUiResourceUri => "item.html",
        ReviewUiResourceUri => "review.html",
        _ => null
    };

    public static JsonObject ResourceMeta() => new()
    {
        ["ui"] = new JsonObject { ["prefersBorder"] = true }
    };

    private static Resource McpResource(string uri, string name, string fileName) => new()
    {
        Uri = uri,
        Name = name,
        Description = $"Bundled Oratorio MCP App: {fileName}",
        MimeType = "text/html;profile=mcp-app",
        Meta = ResourceMeta()
    };

    private static JsonObject UiMeta(string resourceUri) => new()
    {
        ["ui"] = new JsonObject
        {
            ["resourceUri"] = resourceUri,
            ["visibility"] = JsonSerializer.SerializeToNode(ModelAndAppVisibility)
        }
    };
}
