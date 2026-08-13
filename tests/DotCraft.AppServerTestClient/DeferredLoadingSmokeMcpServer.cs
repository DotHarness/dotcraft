using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.AppServerTestClient;

internal static class DeferredLoadingSmokeMcpServer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Web)
    {
        WriteIndented = false
    };

    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        using var input = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        await using var outputStream = Console.OpenStandardOutput();
        await using var output = new StreamWriter(outputStream, new UTF8Encoding(false))
        {
            AutoFlush = true
        };

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await input.ReadLineAsync(cancellationToken);
            if (line is null)
                break;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            JsonDocument? document = null;
            try
            {
                document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("method", out var methodElement))
                    continue;

                var method = methodElement.GetString();
                var hasId = root.TryGetProperty("id", out var idElement);
                if (!hasId)
                {
                    if (method == "notifications/initialized")
                        continue;

                    continue;
                }

                var response = method switch
                {
                    "initialize" => CreateSuccessResponse(idElement, CreateInitializeResult(root)),
                    "ping" => CreateSuccessResponse(idElement, new JsonObject()),
                    "tools/list" => CreateSuccessResponse(idElement, CreateToolsListResult()),
                    "tools/call" => CreateSuccessResponse(idElement, CreateToolCallResult(root)),
                    _ => CreateErrorResponse(idElement, -32601, $"Method not found: {method}")
                };

                await output.WriteLineAsync(response.ToJsonString(JsonOptions));
            }
            catch (Exception ex)
            {
                try
                {
                    JsonElement? id = document?.RootElement.TryGetProperty("id", out var idElement) == true
                        ? idElement
                        : null;
                    if (id.HasValue)
                    {
                        var response = CreateErrorResponse(id.Value, -32603, ex.Message);
                        await output.WriteLineAsync(response.ToJsonString(JsonOptions));
                    }
                }
                catch
                {
                    // A broken response cannot be recovered over stdio.
                }
            }
            finally
            {
                document?.Dispose();
            }
        }

        return 0;
    }

    private static JsonObject CreateInitializeResult(JsonElement request)
    {
        var protocolVersion = "2024-11-05";
        if (TryGetProperty(request, "params", out var parameters)
            && TryGetProperty(parameters, "protocolVersion", out var requestedProtocol)
            && requestedProtocol.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(requestedProtocol.GetString()))
        {
            protocolVersion = requestedProtocol.GetString()!;
        }

        return new JsonObject
        {
            ["protocolVersion"] = protocolVersion,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject()
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "dotcraft-deferred-loading-smoke",
                ["version"] = "0.1.0"
            }
        };
    }

    private static JsonObject CreateToolsListResult() =>
        new()
        {
            ["tools"] = new JsonArray(
                CreateEchoTool(),
                CreateAddNumbersTool())
        };

    private static JsonObject CreateEchoTool() =>
        new()
        {
            ["name"] = DeferredLoadingSmokeTools.Echo,
            ["description"] = "Echoes a smoke-test message for provider-native deferred loading validation.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["message"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Message to echo."
                    }
                },
                ["required"] = new JsonArray("message"),
                ["additionalProperties"] = false
            }
        };

    private static JsonObject CreateAddNumbersTool() =>
        new()
        {
            ["name"] = DeferredLoadingSmokeTools.AddNumbers,
            ["description"] = "Adds two integer numbers for provider-native deferred loading validation.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["a"] = new JsonObject { ["type"] = "integer" },
                    ["b"] = new JsonObject { ["type"] = "integer" }
                },
                ["required"] = new JsonArray("a", "b"),
                ["additionalProperties"] = false
            }
        };

    private static JsonObject CreateToolCallResult(JsonElement request)
    {
        if (!TryGetProperty(request, "params", out var parameters))
            return CreateTextResult("Missing tools/call params.", isError: true);

        var name = ReadString(parameters, "name") ?? string.Empty;
        var arguments = TryGetProperty(parameters, "arguments", out var argumentsElement)
            ? argumentsElement
            : default;

        return name switch
        {
            DeferredLoadingSmokeTools.Echo => CreateTextResult(
                $"{DeferredLoadingSmokeTools.Echo}: {ReadString(arguments, "message") ?? string.Empty}"),
            DeferredLoadingSmokeTools.AddNumbers => CreateTextResult(
                $"{DeferredLoadingSmokeTools.AddNumbers}: {ReadInt(arguments, "a") + ReadInt(arguments, "b")}"),
            _ => CreateTextResult($"Unknown tool: {name}", isError: true)
        };
    }

    private static JsonObject CreateTextResult(string text, bool isError = false) =>
        new()
        {
            ["content"] = new JsonArray(
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = text
                }),
            ["isError"] = isError
        };

    private static JsonObject CreateSuccessResponse(JsonElement id, JsonNode? result) =>
        new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = JsonNode.Parse(id.GetRawText()),
            ["result"] = result ?? new JsonObject()
        };

    private static JsonObject CreateErrorResponse(JsonElement id, int code, string message) =>
        new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = JsonNode.Parse(id.GetRawText()),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };

    private static string? ReadString(JsonElement element, string propertyName) =>
        TryGetProperty(element, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int ReadInt(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
            return 0;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var number) => number,
            _ => 0
        };
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
