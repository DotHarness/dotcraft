using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using DotCraft.Sessions.Wire;

namespace DotCraft.Mcp;

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
    long generation) : IToolRuntime, IMcpToolRuntimeMetadata
{
    public string ServerName => serverName;
    public long Generation => generation;

    private const int MaxPersistedResultBytes = 2 * 1024 * 1024;

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
            (raw, structured, meta) = BoundPersistedResult(raw, structured, meta, result.IsError == true);
            var normalized = NormalizeModelContent(result);
            var contentItems = NormalizeModelContentItems(result);
            if (result.IsError == true)
            {
                return new ToolExecutionResult(
                    false,
                    normalized,
                    structured,
                    meta,
                    raw,
                    new ToolError(ToolErrorCodes.ExecutionFailed,
                        string.IsNullOrWhiteSpace(normalized) ? "The MCP tool returned an error." : normalized),
                    contentItems: contentItems);
            }

            return ToolExecutionResult.Succeeded(
                normalized,
                structured,
                meta,
                raw,
                contentItems: contentItems);
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

    internal static (JsonElement Raw, JsonElement? Structured, JsonElement? Meta) BoundPersistedResult(
        JsonElement raw,
        JsonElement? structured,
        JsonElement? meta,
        bool isError)
    {
        var serialized = raw.GetRawText();
        if (Encoding.UTF8.GetByteCount(serialized) <= MaxPersistedResultBytes)
            return (raw, structured, meta);

        var preview = TruncateUtf8(serialized, MaxPersistedResultBytes / 8);
        var bounded = new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = $"{preview}\n\n[MCP result truncated before persistence.]"
                }
            },
            ["isError"] = isError
        };
        return (
            JsonSerializer.SerializeToElement(bounded, SessionWireJsonOptions.Default),
            null,
            null);
    }

    private static string TruncateUtf8(string value, int maximumBytes)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maximumBytes)
            return value;

        var length = Math.Min(value.Length, maximumBytes / 4);
        if (length > 0 && char.IsHighSurrogate(value[length - 1]))
            length--;
        return value[..length];
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

    internal static IReadOnlyList<AIContent>? NormalizeModelContentItems(CallToolResult result)
    {
        var items = new List<AIContent>();
        foreach (var content in result.Content)
        {
            switch (content)
            {
            case TextContentBlock text when !string.IsNullOrWhiteSpace(text.Text):
                items.Add(new TextContent(text.Text));
                break;
            case ImageContentBlock image when !string.IsNullOrWhiteSpace(image.MimeType):
                try
                {
                    items.Add(new DataContent(image.DecodedData, image.MimeType));
                }
                catch (FormatException)
                {
                    items.Add(new TextContent("[Invalid MCP image payload]"));
                }
                break;
            default:
                items.Add(new TextContent($"[{content.Type} content]"));
                break;
            }
        }

        return items.Count == 0 ? null : items;
    }
}
