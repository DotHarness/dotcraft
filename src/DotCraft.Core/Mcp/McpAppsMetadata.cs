using System.Text;
using System.Text.Json.Nodes;
using DotCraft.Tools;
using ModelContextProtocol.Protocol;

namespace DotCraft.Mcp;

/// <summary>Visibility audiences declared by stable MCP Apps tool metadata.</summary>
[Flags]
public enum McpAppVisibility
{
    /// <summary>The tool is visible to neither MCP model nor MCP App callers.</summary>
    None = 0,
    /// <summary>The tool may be published to the model.</summary>
    Model = 1,
    /// <summary>The tool may be called by an authorized MCP App.</summary>
    App = 2,
}

/// <summary>Validated stable MCP Apps metadata attached to an MCP tool.</summary>
/// <param name="ResourceUri">The optional <c>ui://</c> resource used to render the tool result.</param>
/// <param name="Visibility">The independently declared model and app audiences.</param>
public sealed record McpAppToolMetadata(Uri? ResourceUri, McpAppVisibility Visibility);

/// <summary>Validated resource Content Security Policy domain declarations.</summary>
public sealed record McpAppResourceCsp
{
    /// <summary>Creates immutable CSP domain collections.</summary>
    public McpAppResourceCsp(
        IEnumerable<string>? connectDomains = null,
        IEnumerable<string>? resourceDomains = null,
        IEnumerable<string>? frameDomains = null,
        IEnumerable<string>? baseUriDomains = null)
    {
        ConnectDomains = Copy(connectDomains);
        ResourceDomains = Copy(resourceDomains);
        FrameDomains = Copy(frameDomains);
        BaseUriDomains = Copy(baseUriDomains);
    }

    /// <summary>Gets allowed outbound connection origins.</summary>
    public IReadOnlyList<string> ConnectDomains { get; }
    /// <summary>Gets allowed image, media, font, and stylesheet origins.</summary>
    public IReadOnlyList<string> ResourceDomains { get; }
    /// <summary>Gets allowed nested frame origins.</summary>
    public IReadOnlyList<string> FrameDomains { get; }
    /// <summary>Gets allowed base URI origins.</summary>
    public IReadOnlyList<string> BaseUriDomains { get; }

    private static IReadOnlyList<string> Copy(IEnumerable<string>? source) =>
        source?.ToArray() ?? [];
}

/// <summary>Validated browser permission requests declared by an MCP App resource.</summary>
/// <param name="Camera">Whether camera access was requested.</param>
/// <param name="Microphone">Whether microphone access was requested.</param>
/// <param name="Geolocation">Whether geolocation access was requested.</param>
/// <param name="ClipboardWrite">Whether clipboard write access was requested.</param>
public sealed record McpAppResourcePermissions(
    bool Camera = false,
    bool Microphone = false,
    bool Geolocation = false,
    bool ClipboardWrite = false);

/// <summary>Validated stable MCP Apps metadata attached to an MCP UI resource.</summary>
/// <param name="Csp">Validated network and resource source declarations.</param>
/// <param name="Permissions">Validated permission requests; the M2 Desktop host denies all of them.</param>
/// <param name="Domain">The requested dedicated domain, retained as metadata but not granted in M2.</param>
/// <param name="PrefersBorder">Whether the resource prefers a host-rendered border.</param>
public sealed record McpAppResourceMetadata(
    McpAppResourceCsp? Csp,
    McpAppResourcePermissions? Permissions,
    string? Domain,
    bool? PrefersBorder);

/// <summary>A validated MCP App HTML resource body.</summary>
public sealed class McpAppResourceContent
{
    /// <summary>Creates a validated resource body.</summary>
    public McpAppResourceContent(
        Uri uri,
        string mimeType,
        string? text,
        ReadOnlyMemory<byte> blob,
        McpAppResourceMetadata metadata)
    {
        Uri = uri ?? throw new ArgumentNullException(nameof(uri));
        MimeType = mimeType ?? throw new ArgumentNullException(nameof(mimeType));
        Text = text;
        Blob = blob.ToArray();
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    /// <summary>Gets the exact resource URI.</summary>
    public Uri Uri { get; }
    /// <summary>Gets the validated MCP Apps HTML MIME type.</summary>
    public string MimeType { get; }
    /// <summary>Gets the text body when returned as text.</summary>
    public string? Text { get; }
    /// <summary>Gets the decoded binary body when returned as a blob.</summary>
    public ReadOnlyMemory<byte> Blob { get; }
    /// <summary>Gets validated resource security and presentation metadata.</summary>
    public McpAppResourceMetadata Metadata { get; }
}

/// <summary>Strict parsers for the stable MCP Apps metadata and resource contract.</summary>
public static class McpAppMetadataParser
{
    /// <summary>The stable MCP Apps extension identifier.</summary>
    public const string ExtensionIdentifier = "io.modelcontextprotocol/ui";

    /// <summary>The supported MCP Apps HTML MIME type.</summary>
    public const string HtmlMimeType = "text/html;profile=mcp-app";

    /// <summary>The definition annotation containing validated MCP Apps tool metadata.</summary>
    public const string ToolAnnotationKey = "mcp/app";

    /// <summary>The maximum accepted decoded MCP App resource size.</summary>
    public const int MaxResourceBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Parses tool metadata. Missing metadata is valid and defaults to model and app visibility;
    /// malformed stable fields fail closed.
    /// </summary>
    public static bool TryParseToolMetadata(JsonObject? meta, out McpAppToolMetadata metadata)
    {
        metadata = new McpAppToolMetadata(null, McpAppVisibility.Model | McpAppVisibility.App);
        if (meta is null || !meta.TryGetPropertyValue("ui", out var uiNode) || uiNode is null)
            return true;
        if (uiNode is not JsonObject ui)
        {
            metadata = new McpAppToolMetadata(null, McpAppVisibility.None);
            return false;
        }

        Uri? resourceUri = null;
        if (ui.TryGetPropertyValue("resourceUri", out var resourceNode) && resourceNode is not null)
        {
            if (resourceNode is not JsonValue resourceValue
                || !resourceValue.TryGetValue<string>(out var resourceText)
                || !TryParseUiUri(resourceText, out resourceUri))
            {
                metadata = new McpAppToolMetadata(null, McpAppVisibility.None);
                return false;
            }
        }

        var visibility = McpAppVisibility.Model | McpAppVisibility.App;
        if (ui.TryGetPropertyValue("visibility", out var visibilityNode) && visibilityNode is not null)
        {
            if (visibilityNode is not JsonArray visibilityValues)
            {
                metadata = new McpAppToolMetadata(resourceUri, McpAppVisibility.None);
                return false;
            }

            visibility = McpAppVisibility.None;
            foreach (var valueNode in visibilityValues)
            {
                if (valueNode is not JsonValue value
                    || !value.TryGetValue<string>(out var audience))
                {
                    metadata = new McpAppToolMetadata(resourceUri, McpAppVisibility.None);
                    return false;
                }

                visibility |= audience switch
                {
                    "model" => McpAppVisibility.Model,
                    "app" => McpAppVisibility.App,
                    _ => McpAppVisibility.None
                };
                if (audience is not ("model" or "app"))
                {
                    metadata = new McpAppToolMetadata(resourceUri, McpAppVisibility.None);
                    return false;
                }
            }
        }

        metadata = new McpAppToolMetadata(resourceUri, visibility);
        return true;
    }

    /// <summary>Reads validated MCP Apps metadata persisted on a canonical tool definition.</summary>
    public static bool TryGetToolMetadata(ToolDefinition definition, out McpAppToolMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(definition);
        metadata = new McpAppToolMetadata(null, McpAppVisibility.None);
        if (!definition.Annotations.TryGetValue(ToolAnnotationKey, out var annotation)
            || annotation.ValueKind != System.Text.Json.JsonValueKind.Object)
            return false;
        return TryParseToolMetadata(
            new JsonObject { ["ui"] = JsonNode.Parse(annotation.GetRawText()) },
            out metadata);
    }

    /// <summary>Serializes validated metadata for safe storage on a canonical definition.</summary>
    public static System.Text.Json.JsonElement ToDefinitionAnnotation(McpAppToolMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var visibility = new JsonArray();
        if (metadata.Visibility.HasFlag(McpAppVisibility.Model))
            visibility.Add("model");
        if (metadata.Visibility.HasFlag(McpAppVisibility.App))
            visibility.Add("app");
        var value = new JsonObject { ["visibility"] = visibility };
        if (metadata.ResourceUri is not null)
            value["resourceUri"] = metadata.ResourceUri.AbsoluteUri;
        return System.Text.Json.JsonSerializer.SerializeToElement(value);
    }

    /// <summary>Parses resource security and presentation metadata.</summary>
    public static bool TryParseResourceMetadata(JsonObject? meta, out McpAppResourceMetadata metadata)
    {
        metadata = new McpAppResourceMetadata(null, null, null, null);
        if (meta is null || !meta.TryGetPropertyValue("ui", out var uiNode) || uiNode is null)
            return true;
        if (uiNode is not JsonObject ui)
            return false;

        McpAppResourceCsp? csp = null;
        if (ui.TryGetPropertyValue("csp", out var cspNode) && cspNode is not null)
        {
            if (cspNode is not JsonObject cspObject
                || !TryReadDomains(cspObject, "connectDomains", out var connectDomains)
                || !TryReadDomains(cspObject, "resourceDomains", out var resourceDomains)
                || !TryReadDomains(cspObject, "frameDomains", out var frameDomains)
                || !TryReadDomains(cspObject, "baseUriDomains", out var baseUriDomains))
                return false;
            csp = new McpAppResourceCsp(connectDomains, resourceDomains, frameDomains, baseUriDomains);
        }

        McpAppResourcePermissions? permissions = null;
        if (ui.TryGetPropertyValue("permissions", out var permissionsNode) && permissionsNode is not null)
        {
            if (permissionsNode is not JsonObject permissionObject
                || !TryReadPermission(permissionObject, "camera", out var camera)
                || !TryReadPermission(permissionObject, "microphone", out var microphone)
                || !TryReadPermission(permissionObject, "geolocation", out var geolocation)
                || !TryReadPermission(permissionObject, "clipboardWrite", out var clipboardWrite))
                return false;
            permissions = new McpAppResourcePermissions(
                camera,
                microphone,
                geolocation,
                clipboardWrite);
        }

        string? domain = null;
        if (ui.TryGetPropertyValue("domain", out var domainNode) && domainNode is not null)
        {
            if (domainNode is not JsonValue domainValue
                || !domainValue.TryGetValue<string>(out domain)
                || string.IsNullOrWhiteSpace(domain)
                || domain.Length > 253
                || domain.Any(char.IsControl))
                return false;
        }

        if (!TryReadBoolean(ui, "prefersBorder", out var prefersBorder))
            return false;
        metadata = new McpAppResourceMetadata(csp, permissions, domain, prefersBorder);
        return true;
    }

    /// <summary>Validates a resource/read result as one bounded MCP App HTML resource.</summary>
    public static bool TryParseResourceContent(
        ReadResourceResult result,
        Uri expectedUri,
        out McpAppResourceContent? content,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(expectedUri);
        content = null;
        error = null;
        if (result.Contents is not { Count: 1 })
            return Fail("An MCP App resource must return exactly one content item.", out error);

        var item = result.Contents[0];
        if (!Uri.TryCreate(item.Uri, UriKind.Absolute, out var uri)
            || !string.Equals(uri.AbsoluteUri, expectedUri.AbsoluteUri, StringComparison.Ordinal))
            return Fail("The MCP App resource URI did not match the requested URI.", out error);
        if (!string.Equals(item.MimeType?.Replace(" ", string.Empty), HtmlMimeType, StringComparison.OrdinalIgnoreCase))
            return Fail($"The MCP App resource must use MIME type '{HtmlMimeType}'.", out error);
        if (!TryParseResourceMetadata(item.Meta, out var metadata))
            return Fail("The MCP App resource contains invalid UI metadata.", out error);

        switch (item)
        {
            case TextResourceContents text:
                if (Encoding.UTF8.GetByteCount(text.Text ?? string.Empty) > MaxResourceBytes)
                    return Fail("The MCP App resource exceeds the maximum size.", out error);
                content = new McpAppResourceContent(uri, HtmlMimeType, text.Text ?? string.Empty, default, metadata);
                return true;
            case BlobResourceContents blob:
                if (blob.DecodedData.Length > MaxResourceBytes)
                    return Fail("The MCP App resource exceeds the maximum size.", out error);
                content = new McpAppResourceContent(uri, HtmlMimeType, null, blob.DecodedData, metadata);
                return true;
            default:
                return Fail("The MCP App resource must contain text or a base64 blob.", out error);
        }
    }

    private static bool TryParseUiUri(string? value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            || !string.Equals(parsed.Scheme, "ui", StringComparison.OrdinalIgnoreCase))
            return false;
        uri = parsed;
        return true;
    }

    private static bool TryReadDomains(JsonObject source, string propertyName, out IReadOnlyList<string> domains)
    {
        domains = [];
        if (!source.TryGetPropertyValue(propertyName, out var node) || node is null)
            return true;
        if (node is not JsonArray array)
            return false;
        var values = new List<string>(array.Count);
        foreach (var item in array)
        {
            if (item is not JsonValue value
                || !value.TryGetValue<string>(out var domain)
                || !IsHttpsCspSource(domain))
                return false;
            values.Add(domain);
        }
        domains = values;
        return true;
    }

    private static bool TryReadBoolean(JsonObject source, string propertyName, out bool? value)
    {
        value = null;
        if (!source.TryGetPropertyValue(propertyName, out var node) || node is null)
            return true;
        if (node is not JsonValue jsonValue || !jsonValue.TryGetValue<bool>(out var parsed))
            return false;
        value = parsed;
        return true;
    }

    private static bool TryReadPermission(JsonObject source, string propertyName, out bool requested)
    {
        requested = false;
        if (!source.TryGetPropertyValue(propertyName, out var node) || node is null)
            return true;
        if (node is not JsonObject)
            return false;
        requested = true;
        return true;
    }

    private static bool IsHttpsCspSource(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var normalized = value.Replace("https://*.", "https://wildcard.", StringComparison.OrdinalIgnoreCase);
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(uri.PathAndQuery.Trim('/'))
        && string.IsNullOrEmpty(uri.Fragment)
        && string.IsNullOrEmpty(uri.UserInfo);
    }

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }
}
