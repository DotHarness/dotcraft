namespace Oratorio.Server.DotCraft;

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

    public static IReadOnlyList<object> McpBoardTools(OratorioDynamicToolCatalog tools) =>
        tools.BoardDescriptors.Select(descriptor =>
        {
            string? resourceUri = descriptor.LocalName switch
            {
                OratorioDynamicToolCatalog.ListBoardItemsName => BoardUiResourceUri,
                OratorioDynamicToolCatalog.GetBoardItemName => ItemUiResourceUri,
                OratorioDynamicToolCatalog.QueueReviewRoundName => ReviewUiResourceUri,
                _ => null
            };
            return (object)new
            {
                name = descriptor.LocalName,
                description = descriptor.Description,
                inputSchema = descriptor.InputSchema,
                annotations = new
                {
                    readOnlyHint = descriptor.LocalName is
                        OratorioDynamicToolCatalog.ListBoardItemsName or
                        OratorioDynamicToolCatalog.GetBoardItemName,
                    destructiveHint = false,
                    openWorldHint = false
                },
                _meta = resourceUri is null
                    ? null
                    : new
                    {
                        ui = new
                        {
                            resourceUri,
                            visibility = ModelAndAppVisibility
                        }
                    }
            };
        }).ToArray();

    public static IReadOnlyList<object> McpAppResources() =>
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

    private static object McpResource(string uri, string name, string fileName) => new
    {
        uri,
        name,
        description = $"Bundled Oratorio MCP App: {fileName}",
        mimeType = "text/html;profile=mcp-app",
        _meta = new { ui = new { prefersBorder = true } }
    };
}
