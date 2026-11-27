using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace DotCraft.Oratorio.Integrations;

public static class OratorioMcpCatalog
{
    public const string UiResourcePrefix = "ui://oratorio";
    public const string BoardUiResourceUri = $"{UiResourcePrefix}/board.html";
    public const string ItemUiResourceUri = $"{UiResourcePrefix}/item.html";
    public const string ReviewUiResourceUri = $"{UiResourcePrefix}/review.html";
    public const string CredentialUiResourceUri = $"{UiResourcePrefix}/credential.html";
    public const string ServerInstructions = "Inspect and manage the Oratorio project board and its repository connections. Read current Oratorio state before making claims, and use mutation tools only when the user asks to change Oratorio state.";

    private static readonly string[] ModelAndAppVisibility = ["model", "app"];
    private static readonly string[] AppOnlyVisibility = ["app"];

    public static IReadOnlyList<Tool> McpTools(OratorioDynamicToolCatalog tools) =>
        tools.McpDescriptors.Select(descriptor =>
        {
            string? resourceUri = descriptor.LocalName switch
            {
                OratorioDynamicToolCatalog.ListBoardItemsName => BoardUiResourceUri,
                OratorioDynamicToolCatalog.GetBoardItemName => ItemUiResourceUri,
                OratorioDynamicToolCatalog.QueueReviewRoundName => ReviewUiResourceUri,
                OratorioDynamicToolCatalog.RequestSourceCredentialName => CredentialUiResourceUri,
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
                        OratorioDynamicToolCatalog.GetBoardItemName or
                        OratorioDynamicToolCatalog.GetSourceSetupName or
                        OratorioDynamicToolCatalog.RequestSourceCredentialName,
                    DestructiveHint = false,
                    OpenWorldHint = false
                },
                Meta = descriptor.LocalName == OratorioDynamicToolCatalog.SaveSourceCredentialName
                    ? UiMeta(null, AppOnlyVisibility)
                    : resourceUri is null ? null : UiMeta(resourceUri, ModelAndAppVisibility)
            };
        }).ToArray();

    public static IReadOnlyList<Resource> McpAppResources() =>
    [
        McpResource(BoardUiResourceUri, "Oratorio board", "board.html"),
        McpResource(ItemUiResourceUri, "Oratorio item", "item.html"),
        McpResource(ReviewUiResourceUri, "Oratorio review", "review.html"),
        McpResource(CredentialUiResourceUri, "Oratorio credential", "credential.html")
    ];

    public static async ValueTask<ReadResourceResult> ReadUiResourceAsync(
        string? uri,
        CancellationToken cancellationToken = default)
    {
        if (uri is null)
            throw new McpProtocolException("Missing resources/read URI.", McpErrorCode.InvalidParams);
        if (ResolveUiFile(uri) is null)
            throw new McpProtocolException("Unknown MCP App resource.", McpErrorCode.InvalidParams);

        var html = await ReadEmbeddedUiHtmlAsync(uri, cancellationToken).ConfigureAwait(false);
        if (html is null)
            throw new McpProtocolException("MCP App resource is unavailable.", McpErrorCode.InternalError);

        return new ReadResourceResult
        {
            Contents =
            [
                new TextResourceContents
                {
                    Uri = uri,
                    MimeType = "text/html;profile=mcp-app",
                    Text = html,
                    Meta = ResourceMeta()
                }
            ]
        };
    }

    private static string? ResolveUiFile(string? uri) => uri switch
    {
        BoardUiResourceUri => "board.html",
        ItemUiResourceUri => "item.html",
        ReviewUiResourceUri => "review.html",
        CredentialUiResourceUri => "credential.html",
        _ => null
    };

    private static async Task<string?> ReadEmbeddedUiHtmlAsync(string uri, CancellationToken cancellationToken = default)
    {
        var fileName = ResolveUiFile(uri);
        if (fileName is null)
            return null;

        var resourceName = $"DotCraft.Oratorio.UiResources.{fileName}";
        await using var stream = typeof(OratorioMcpCatalog).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return null;

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static JsonObject ResourceMeta() => new()
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

    private static JsonObject UiMeta(string? resourceUri, string[] visibility)
    {
        var ui = new JsonObject { ["visibility"] = JsonSerializer.SerializeToNode(visibility) };
        if (resourceUri is not null)
        {
            ui["resourceUri"] = resourceUri;
        }

        return new JsonObject { ["ui"] = ui };
    }
}
