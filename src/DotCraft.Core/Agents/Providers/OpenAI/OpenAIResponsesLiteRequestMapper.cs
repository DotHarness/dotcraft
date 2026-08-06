using System.ClientModel.Primitives;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace DotCraft.Agents;

/// <summary>
/// Builds the Responses Lite wire contract while retaining the standard mapper's item identity,
/// provider-history and tool-schema behavior.
/// </summary>
internal static class OpenAIResponsesLiteRequestMapper
{
    internal sealed record OpenAIResponsesLiteRequest(
        CreateResponseOptions Options,
        BinaryData WireBody);

    internal static OpenAIResponsesLiteRequest CreateResponseRequest(
        string model,
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        JsonArray? canonicalInput,
        OpenAIResponsesItemIdentityDiagnostics? canonicalItemIdentity,
        IChatClient rawRepresentationClient,
        string installationId)
    {
        var standard = ResponsesToolSearchMapper.CreateResponseRequest(
            model,
            messages,
            options,
            canonicalInput: canonicalInput,
            canonicalItemIdentity: canonicalItemIdentity,
            rawRepresentationClient: rawRepresentationClient);
        return new OpenAIResponsesLiteRequest(
            standard.Options,
            BuildWireBody(standard.Options, installationId));
    }

    internal static BinaryData BuildWireBody(CreateResponseOptions options, string installationId)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(installationId))
            throw new ArgumentException("Installation id must be non-empty.", nameof(installationId));

        var sdkJson = ModelReaderWriter.Write(options).ToString();
        var canonicalJson = OpenAIResponsesRequestBodyCanonicalizer.NormalizeTopLevelObject(sdkJson)
                            ?? throw new InvalidDataException("Responses request must be a JSON object.");
        var root = JsonNode.Parse(canonicalJson)?.AsObject()
                   ?? throw new InvalidDataException("Responses request must be a JSON object.");

        ApplyLiteDialect(root);

        var snapshot = OpenAIResponsesCodexMetadata.CreateSnapshot(installationId);
        MergeClientMetadata(root, OpenAIResponsesCodexMetadata.BuildClientMetadata(snapshot));
        return BinaryData.FromString(root.ToJsonString());
    }

    internal static BinaryData BuildCompactWireBody(BinaryData standardBody)
    {
        ArgumentNullException.ThrowIfNull(standardBody);
        var canonicalJson = OpenAIResponsesRequestBodyCanonicalizer.NormalizeTopLevelObject(
                                standardBody.ToString())
                            ?? throw new InvalidDataException("Responses compact request must be a JSON object.");
        var root = JsonNode.Parse(canonicalJson)?.AsObject()
                   ?? throw new InvalidDataException("Responses compact request must be a JSON object.");
        ApplyLiteDialect(root);
        return BinaryData.FromString(root.ToJsonString());
    }

    private static void ApplyLiteDialect(JsonObject root)
    {
        var instructions = root["instructions"]?.GetValue<string>();
        var tools = NormalizeTools(root["tools"] as JsonArray);
        var originalInput = root["input"]?.DeepClone() as JsonArray ?? [];
        var liteInput = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "additional_tools",
                ["role"] = "developer",
                ["tools"] = tools
            }
        };
        if (!string.IsNullOrEmpty(instructions))
        {
            liteInput.Add(new JsonObject
            {
                ["type"] = "message",
                ["role"] = "developer",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "input_text",
                        ["text"] = instructions
                    }
                }
            });
        }
        foreach (var item in originalInput)
            liteInput.Add(item?.DeepClone());

        StripImageDetails(liteInput);
        root["input"] = liteInput;
        root.Remove("instructions");
        root.Remove("tools");
        root.Remove("max_output_tokens");
        root["store"] = false;
        root["stream"] = true;
        // The Responses Lite endpoint rejects parallel_tool_calls=true instead of ignoring it.
        // Preserve omission when no tool control was emitted, but force every emitted value off.
        if (root.ContainsKey("parallel_tool_calls"))
            root["parallel_tool_calls"] = false;
        var reasoning = root["reasoning"] as JsonObject ?? new JsonObject();
        reasoning["context"] = "all_turns";
        root["reasoning"] = reasoning;

    }

    private static JsonArray NormalizeTools(JsonArray? source)
    {
        var result = new JsonArray();
        var functions = new JsonArray();
        var functionsDescription = string.Empty;
        int? functionsIndex = null;

        foreach (var node in source ?? [])
        {
            if (node is not JsonObject tool)
            {
                result.Add(node?.DeepClone());
                continue;
            }

            var type = tool["type"]?.GetValue<string>();
            if (string.Equals(type, "function", StringComparison.Ordinal)
                || string.Equals(type, "custom", StringComparison.Ordinal))
            {
                functionsIndex ??= result.Count;
                functions.Add(tool.DeepClone());
                continue;
            }

            if (string.Equals(type, "namespace", StringComparison.Ordinal)
                && string.Equals(tool["name"]?.GetValue<string>(), "functions", StringComparison.Ordinal))
            {
                functionsIndex ??= result.Count;
                var description = tool["description"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(description))
                    functionsDescription = description;
                if (tool["tools"] is JsonArray namespacedTools)
                {
                    foreach (var namespacedTool in namespacedTools)
                        functions.Add(namespacedTool?.DeepClone());
                }
                continue;
            }

            result.Add(tool.DeepClone());
        }

        if (functionsIndex is { } index && functions.Count > 0)
        {
            result.Insert(index, new JsonObject
            {
                ["type"] = "namespace",
                ["name"] = "functions",
                ["description"] = functionsDescription,
                ["tools"] = functions
            });
        }

        return result;
    }

    private static void MergeClientMetadata(
        JsonObject root,
        IReadOnlyDictionary<string, string> authoritativeMetadata)
    {
        var metadata = root["client_metadata"] as JsonObject ?? new JsonObject();
        foreach (var pair in authoritativeMetadata)
            metadata[pair.Key] = pair.Value;
        root["client_metadata"] = metadata;
    }

    private static void StripImageDetails(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            if (obj["type"] is JsonValue typeValue
                && typeValue.TryGetValue<string>(out var type)
                && string.Equals(type, "input_image", StringComparison.Ordinal))
            {
                obj.Remove("detail");
            }
            foreach (var child in obj.ToArray())
            {
                if (child.Value is not null)
                    StripImageDetails(child.Value);
            }
            return;
        }

        if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                if (child is not null)
                    StripImageDetails(child);
            }
        }
    }
}
