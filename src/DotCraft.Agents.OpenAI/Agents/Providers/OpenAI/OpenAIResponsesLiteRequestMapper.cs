using System.ClientModel.Primitives;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace DotCraft.Agents;

internal static class OpenAIResponsesLiteRequestMapper
{
    internal const string FunctionsNamespace = "functions";

    internal sealed record OpenAIResponsesLiteRequest(
        CreateResponseOptions Options,
        BinaryData WireBody,
        DotCraft.Tracing.PromptCacheRequestShapeSnapshot Shape,
        IReadOnlyDictionary<string, ResponsesToolSearchMapper.LiteToolCallRoute> ToolCallRoutes);

    internal static OpenAIResponsesLiteRequest CreateResponseRequest(
        string model,
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        JsonArray? canonicalInput,
        OpenAIResponsesItemIdentityDiagnostics? canonicalItemIdentity,
        IChatClient rawRepresentationClient,
        string installationId,
        bool supportsFreeformTools)
    {
        var standard = ResponsesToolSearchMapper.CreateResponseRequest(
            model,
            messages,
            options,
            canonicalInput: canonicalInput,
            canonicalItemIdentity: canonicalItemIdentity,
            rawRepresentationClient: rawRepresentationClient,
            supportsFreeformTools: supportsFreeformTools);
        var body = BuildWireObject(ModelReaderWriter.Write(standard.Options), installationId);
        var input = body["input"]!.AsArray();
        return new OpenAIResponsesLiteRequest(
            standard.Options,
            BinaryData.FromString(body.ToJsonString()),
            ResponsesToolSearchMapper.WithFinalInputShape(standard.Shape, input) with
            {
                InputItemIdGeneratedCount = standard.Shape.InputItemIdGeneratedCount
                                            + input.Count - standard.Shape.InputItemCount,
                MaxOutputTokensPresentAfterOAuthRewrite = false,
                MaxOutputTokensRemovedByOAuthRewrite = standard.Shape.MaxOutputTokensRequested.HasValue
            },
            ResponsesToolSearchMapper.CollectLiteToolCallRoutes(options, input));
    }

    internal static BinaryData BuildWireBody(BinaryData standardBody, string? installationId) =>
        BinaryData.FromString(BuildWireObject(standardBody, installationId).ToJsonString());

    private static JsonObject BuildWireObject(BinaryData standardBody, string? installationId)
    {
        var canonicalJson = OpenAIResponsesRequestBodyCanonicalizer.NormalizeTopLevelObject(standardBody.ToString())
                            ?? throw new InvalidDataException("Responses request must be a JSON object.");
        var root = JsonNode.Parse(canonicalJson)!.AsObject();
        ApplyLiteDialect(root);
        if (!string.IsNullOrWhiteSpace(installationId))
        {
            var snapshot = OpenAIResponsesCodexMetadata.CreateSnapshot(installationId);
            MergeClientMetadata(root, OpenAIResponsesCodexMetadata.BuildClientMetadata(snapshot));
        }
        return root;
    }

    private static void ApplyLiteDialect(JsonObject root)
    {
        var tools = NormalizeTools(root["tools"] as JsonArray);
        var input = root["input"] as JsonArray ?? [];
        StripImageDetails(input);
        if (tools.Count > 0)
        {
            StripImageDetails(tools);
            input.Insert(0, new JsonObject
            {
                ["id"] = OpenAIResponsesItemIdentity.GenerateThreadPrefix(
                    "at", "openai.responses.additional_tools.v1", tools.ToJsonString()),
                ["type"] = "additional_tools",
                ["role"] = "developer",
                ["tools"] = tools
            });
        }

        root["input"] = input;
        root.Remove("tools");
        root.Remove("max_output_tokens");
        root["store"] = false;
        root["stream"] = true;
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
                && string.Equals(tool["name"]?.GetValue<string>(), FunctionsNamespace, StringComparison.Ordinal))
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
                ["name"] = FunctionsNamespace,
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
