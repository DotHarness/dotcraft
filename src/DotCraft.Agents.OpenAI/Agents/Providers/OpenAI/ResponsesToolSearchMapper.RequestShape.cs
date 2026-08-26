using System.ClientModel.Primitives;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tools;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAI001, MEAI001

namespace DotCraft.Agents;

internal static partial class ResponsesToolSearchMapper
{
    private static string? BuildInstructions(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(options?.Instructions))
            parts.Add(options!.Instructions!.Trim());
        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                var text = ConcatenateInstructionContent(message.Contents);
                if (!string.IsNullOrWhiteSpace(text))
                    parts.Add(text.Trim());
            }
        }

        return parts.Count == 0 ? null : string.Join("\n\n", parts);
    }

    private static JsonArray BuildTools(ChatOptions? options)
    {
        var tools = new JsonArray();
        var namespaceToolArrays = new Dictionary<string, JsonArray>(StringComparer.Ordinal);
        var namespaceToolObjects = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var namespaceDescriptions = new Dictionary<string, List<string?>>(StringComparer.Ordinal);
        var hostedImageGenerationEnabled = IsHostedImageGenerationEnabled(options);
        foreach (var tool in options?.Tools ?? [])
        {
            if (hostedImageGenerationEnabled && IsReservedImageGenerationFunction(tool))
                continue;

            if (string.Equals(tool.Name, IDeferredToolSearchMarker.CanonicalName, StringComparison.Ordinal))
            {
                tools.Add(new JsonObject
                {
                    ["type"] = OpenAIHostedToolNames.ToolSearchType,
                    ["execution"] = "client",
                    ["description"] = tool.Description,
                    ["parameters"] = CloneJsonElement(GetJsonSchema(tool))
                });
                continue;
            }

            var functionTool = CreateFunctionTool(tool, options);
            if (ToolNamespaceMetadataResolver.TryGet(tool, out var toolNamespace))
            {
                ValidateProviderToolIdentity(toolNamespace, ReadJsonString(functionTool, "name")!);
                if (!namespaceToolArrays.TryGetValue(toolNamespace, out var namespaceTools))
                {
                    namespaceTools = [];
                    namespaceToolArrays[toolNamespace] = namespaceTools;
                    namespaceDescriptions[toolNamespace] = [];
                    var namespaceTool = CreateNamespaceTool(toolNamespace, namespaceTools);
                    namespaceToolObjects[toolNamespace] = namespaceTool;
                    tools.Add(namespaceTool);
                }

                namespaceTools.Add(functionTool);
                namespaceDescriptions[toolNamespace].Add(ToolNamespaceMetadataResolver.GetDescription(tool));
                continue;
            }

            ValidateProviderToolIdentity(null, ReadJsonString(functionTool, "name")!);
            tools.Add(functionTool);
        }

        foreach (var (namespaceName, descriptions) in namespaceDescriptions)
        {
            namespaceToolObjects[namespaceName]["description"] = ToolNamespaceDescriptionResolver.Resolve(
                namespaceName,
                descriptions,
                out _);
        }

        if (hostedImageGenerationEnabled)
        {
            tools.Add(new JsonObject
            {
                ["type"] = HostedImageGenerationContent.ToolName,
                ["output_format"] = "png"
            });
        }

        return tools;
    }

    private static JsonObject CreateFunctionTool(AITool tool, ChatOptions? options)
    {
        var functionName = CanonicalToolIdentityMetadataResolver.TryGet(
            tool,
            out var canonicalName,
            out _)
            ? canonicalName.Name
            : tool.Name;
        var strict = tool is IOpenAIResponsesFunctionToolMetadata metadata
            ? metadata.Strict ?? IsStrict(options)
            : IsStrict(options);
        var schema = GetJsonSchema(tool);
        var functionTool = new JsonObject
        {
            ["type"] = "function",
            ["name"] = functionName,
            ["description"] = tool.Description,
            ["parameters"] = CloneJsonElement(
                strict is true
                    ? AIJsonUtilities.TransformSchema(schema, OpenAIStrictSchemaTransformOptions)
                    : schema)
        };

        if (strict is not null)
            functionTool["strict"] = strict;

        return functionTool;
    }

    private static void ValidateProviderToolIdentity(string? toolNamespace, string localName)
    {
        static bool IsSafe(string value) => value.Length > 0 && value.All(static character =>
            character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '_');

        var flatLength = localName.Length + (toolNamespace is null ? 0 : toolNamespace.Length + 2);
        if (!IsSafe(localName)
            || toolNamespace is not null && !IsSafe(toolNamespace)
            || flatLength > ProviderToolProjector.MaximumNameBytes)
        {
            throw new InvalidOperationException(
                $"invalid_provider_tool_identity: '{(toolNamespace is null ? localName : $"{toolNamespace}/{localName}")}' is not provider-safe.");
        }
    }

    private static JsonObject CreateNamespaceTool(string namespaceName, JsonArray tools) =>
        new()
        {
            ["type"] = "namespace",
            ["name"] = namespaceName,
            ["description"] = $"Tools in the {namespaceName} namespace.",
            ["tools"] = tools
        };

    private static ResponseReasoningOptions CreateReasoningOptions(ReasoningOptions? reasoning)
    {
        var options = new ResponseReasoningOptions();
        if (reasoning?.Effort is { } effort)
        {
            options.ReasoningEffortLevel = new ResponseReasoningEffortLevel(NormalizeReasoningEffortToken(effort));
        }

        if (reasoning?.Output is { } output && output != ReasoningOutput.None)
        {
            options.ReasoningSummaryVerbosity = output == ReasoningOutput.Full
                ? ResponseReasoningSummaryVerbosity.Detailed
                : ResponseReasoningSummaryVerbosity.Auto;
        }

        return options;
    }

    private static PromptCacheRequestShapeSnapshot CreatePromptCacheRequestShapeSnapshot(
        string model,
        PromptCacheKeyResolution promptCacheKey,
        string? instructions,
        JsonArray input,
        OpenAIResponsesItemIdentityDiagnostics itemIdentity,
        JsonArray tools,
        bool includeReasoning,
        ResponseReasoningOptions? reasoningOptions,
        CreateResponseOptions responseOptions,
        ChatOptions? options,
        bool removesUnsupportedOAuthResponsesFields)
    {
        var inputItemHashes = input
            .Select(static item => item == null ? HashUtf8String("null") : HashJsonNode(item))
            .ToArray();
        var maxOutputTokensRequested = options?.MaxOutputTokens;
        var maxOutputTokensRemovedByOAuthRewrite = maxOutputTokensRequested.HasValue && removesUnsupportedOAuthResponsesFields;

        return new PromptCacheRequestShapeSnapshot(
            PromptCacheRequestShapeSchemaVersion,
            OpenAIResponsesProtocolName,
            model,
            string.IsNullOrWhiteSpace(promptCacheKey.Value) ? null : HashUtf8String(promptCacheKey.Value!),
            promptCacheKey.Source,
            HashJsonStringValue(instructions),
            HashJsonNode(tools),
            HashReasoningShape(includeReasoning, reasoningOptions),
            HashJsonNode(input),
            input.Count,
            inputItemHashes,
            Encoding.UTF8.GetByteCount(input.ToJsonString(JsonOptions)),
            itemIdentity.EligibleCount,
            itemIdentity.PresentCount,
            itemIdentity.GeneratedCount,
            itemIdentity.MissingCount,
            itemIdentity.InvalidSourceCount,
            maxOutputTokensRequested,
            maxOutputTokensRequested.HasValue && !maxOutputTokensRemovedByOAuthRewrite,
            maxOutputTokensRemovedByOAuthRewrite,
            options?.Reasoning?.Effort is { } effort ? NormalizeReasoningEffortToken(effort) : null,
            DescribeToolChoiceKind(options?.ToolMode),
            tools.Count,
            responseOptions.StreamingEnabled == true);
    }

    private static string DescribeToolChoiceKind(ChatToolMode? toolMode)
    {
        if (toolMode == null || ReferenceEquals(toolMode, ChatToolMode.Auto))
            return "Auto";
        if (ReferenceEquals(toolMode, ChatToolMode.None))
            return "None";
        if (ReferenceEquals(toolMode, ChatToolMode.RequireAny)
            || toolMode.GetType().Name.Contains("Required", StringComparison.Ordinal))
            return "Required";
        return toolMode.GetType().Name;
    }

    private static string? HashReasoningShape(
        bool includeReasoning,
        ResponseReasoningOptions? reasoningOptions)
    {
        if (!includeReasoning && reasoningOptions == null)
            return null;

        var shape = new JsonObject
        {
            ["includeReasoningEncryptedContent"] = includeReasoning
        };
        if (reasoningOptions != null && SerializeSdkModelToJsonNode(reasoningOptions) is { } reasoning)
            shape["reasoning"] = reasoning;

        return HashJsonNode(shape);
    }

    private static JsonNode? SerializeSdkModelToJsonNode(object value)
    {
        try
        {
            return JsonNode.Parse(ModelReaderWriter.Write(value).ToString());
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or JsonException or ArgumentException)
        {
            return JsonSerializer.SerializeToNode(value, JsonOptions);
        }
    }

    private static string HashJsonNode(JsonNode node) =>
        HashUtf8String(node.ToJsonString(JsonOptions));

    private static string? HashJsonStringValue(string? value) =>
        value == null ? null : HashUtf8Bytes(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));

    private static string HashUtf8String(string value) =>
        HashUtf8Bytes(Encoding.UTF8.GetBytes(value));

    private static string HashUtf8Bytes(byte[] bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
