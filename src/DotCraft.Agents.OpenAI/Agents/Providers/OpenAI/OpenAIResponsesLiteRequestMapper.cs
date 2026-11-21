using System.ClientModel.Primitives;
using System.Text.Json.Nodes;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace DotCraft.Agents;

internal static class OpenAIResponsesLiteRequestMapper
{
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
        var body = BuildWireObject(ModelReaderWriter.Write(standard.Options), installationId, options);
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
            ResponsesToolSearchMapper.CollectLiteToolCallRoutes(options));
    }

    internal static BinaryData BuildWireBody(BinaryData standardBody, string? installationId, ChatOptions? options = null) =>
        BinaryData.FromString(BuildWireObject(standardBody, installationId, options).ToJsonString());

    private static JsonObject BuildWireObject(BinaryData standardBody, string? installationId, ChatOptions? options)
    {
        var canonicalJson = OpenAIResponsesRequestBodyCanonicalizer.NormalizeTopLevelObject(standardBody.ToString())
                            ?? throw new InvalidDataException("Responses request must be a JSON object.");
        var root = JsonNode.Parse(canonicalJson)!.AsObject();
        ApplyLiteDialect(root, options);
        if (!string.IsNullOrWhiteSpace(installationId))
        {
            var snapshot = OpenAIResponsesCodexMetadata.CreateSnapshot(installationId);
            MergeClientMetadata(root, OpenAIResponsesCodexMetadata.BuildClientMetadata(snapshot));
        }
        return root;
    }

    private static void ApplyLiteDialect(JsonObject root, ChatOptions? options)
    {
        var tools = NormalizeTools(root["tools"] as JsonArray);
        var selected = SelectRequiredTool(root, tools, options);
        if (selected is not null)
        {
            tools = [selected];
            root["tool_choice"] = "required";
        }

        var input = root["input"] as JsonArray ?? [];
        foreach (var item in input.OfType<JsonObject>())
        {
            if (item["type"]?.GetValue<string>() == "tool_search_output" && item["tools"] is JsonArray discovered)
                item["tools"] = selected is null ? NormalizeTools(discovered) : new JsonArray();
        }
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

            var projected = tool.DeepClone().AsObject();
            if (type == "namespace" && tool["name"]?.GetValue<string>() is { } localNamespace)
                projected["name"] = OpenAIResponsesLiteToolIdentity.ProjectNamespace(localNamespace);
            result.Add(projected);
        }

        if (functionsIndex is { } index && functions.Count > 0)
        {
            result.Insert(index, new JsonObject
            {
                ["type"] = "namespace",
                ["name"] = OpenAIResponsesLiteToolIdentity.FunctionsNamespace,
                ["description"] = string.Empty,
                ["tools"] = functions
            });
        }

        return result;
    }

    private static JsonObject? SelectRequiredTool(JsonObject root, JsonArray tools, ChatOptions? options)
    {
        if (root["tool_choice"] is not JsonObject choice
            || choice["type"]?.GetValue<string>() is not ("function" or "custom")
            || choice["name"]?.GetValue<string>() is not { } name)
        {
            return null;
        }

        var localNamespace = choice["namespace"]?.GetValue<string>();
        if (localNamespace is null && options?.Tools?.FirstOrDefault(tool => tool.Name == name) is { } selected)
        {
            if (ToolNamespaceMetadataResolver.TryGet(selected, out var resolved))
                localNamespace = resolved;
            if (CanonicalToolIdentityMetadataResolver.TryGet(selected, out var canonical, out _))
                name = canonical.Name;
        }

        var wireNamespace = OpenAIResponsesLiteToolIdentity.ProjectNamespace(localNamespace);
        var group = tools.OfType<JsonObject>().FirstOrDefault(tool => tool["type"]?.GetValue<string>() == "namespace"
            && tool["name"]?.GetValue<string>() == wireNamespace);
        if (group?["tools"] is not JsonArray members
            || members.OfType<JsonObject>().FirstOrDefault(tool => tool["name"]?.GetValue<string>() == name) is not { } member)
        {
            return null;
        }

        var projection = group.DeepClone().AsObject();
        projection["tools"] = new JsonArray(member.DeepClone());
        return projection;
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
