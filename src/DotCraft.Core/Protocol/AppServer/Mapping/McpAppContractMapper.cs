using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Mcp;
using DotCraft.Tools;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions.Wire;

namespace DotCraft.AppServer;

internal static class McpAppContractMapper
{
    public static Contract.McpAppResource ToContract(McpAppResourceContent resource) => new()
    {
        Uri = resource.Uri.AbsoluteUri,
        MimeType = resource.MimeType,
        Html = resource.Text ?? Encoding.UTF8.GetString(resource.Blob.Span),
        Ui = new Contract.McpAppResourceMetadata
        {
            PrefersBorder = resource.Metadata.PrefersBorder ?? false,
            RequestedDomain = OmitIfNull(resource.Metadata.Domain),
            Csp = new Contract.McpAppResourceCsp
            {
                ConnectDomains = Strings(resource.Metadata.Csp?.ConnectDomains),
                ResourceDomains = Strings(resource.Metadata.Csp?.ResourceDomains),
                FrameDomains = Strings(resource.Metadata.Csp?.FrameDomains),
                BaseUriDomains = Strings(resource.Metadata.Csp?.BaseUriDomains)
            }
        }
    };

    public static Contract.McpAppToolResult ToContract(ToolExecutionResult result)
    {
        if (result.RawSourceResult is { } raw)
        {
            var source = JsonNode.Parse(raw.GetRawText())?.AsObject();
            return CreateToolResult(
                source?["content"],
                source?["structuredContent"],
                source?["_meta"],
                source?["isError"]?.GetValue<bool>() ?? !result.Success,
                result.Error?.Code,
                result.Error?.Message);
        }

        var content = new JsonArray();
        if (!string.IsNullOrEmpty(result.Content))
            content.Add(new JsonObject { ["type"] = "text", ["text"] = result.Content });
        return CreateToolResult(
            content,
            result.StructuredContent is { } structured ? JsonNode.Parse(structured.GetRawText()) : null,
            result.Meta is { } meta ? JsonNode.Parse(meta.GetRawText()) : null,
            !result.Success,
            result.Error?.Code,
            result.Error?.Message);
    }

    public static JsonElement ToElement(JsonNode? value) =>
        JsonSerializer.SerializeToElement(value, SessionWireJsonOptions.Default);

    public static JsonElement? ToNullableElement(JsonNode? value) =>
        value is null ? null : ToElement(value);

    private static Contract.McpAppToolResult CreateToolResult(
        JsonNode? content,
        JsonNode? structuredContent,
        JsonNode? meta,
        bool isError,
        string? errorCode,
        string? errorMessage) => new()
    {
        Content = ToElement(content ?? new JsonArray()),
        StructuredContent = OmitIfNull(ToNullableElement(structuredContent)),
        Meta = OmitIfNull(ToNullableElement(meta)),
        IsError = isError,
        ErrorCode = OmitIfNull(errorCode),
        ErrorMessage = OmitIfNull(errorMessage)
    };

    private static DotCraft.Protocol.Optional<IReadOnlyList<string>> Strings(
        IEnumerable<string>? values) => new(values?.ToArray() ?? []);

    private static DotCraft.Protocol.Optional<T?> OmitIfNull<T>(T? value) =>
        value is null ? default : new DotCraft.Protocol.Optional<T?>(value);
}
