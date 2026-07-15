using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
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

internal static class ResponsesToolSearchMapper
{
    internal const string FunctionCallNamespaceMetadataKey = "openai.responses.function_call.namespace";
    internal const string PromptCacheKeyAdditionalProperty = "prompt_cache_key";
    internal const string HostedImageGenerationEnabledAdditionalProperty = "dotcraft.openai.responses.image_generation.enabled";

    private const int PromptCacheRequestShapeSchemaVersion = 1;
    private const string OpenAIResponsesProtocolName = "openai-responses";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal sealed record OpenAIResponsesRequest(
        CreateResponseOptions Options,
        PromptCacheRequestShapeSnapshot Shape);

    public static bool HasNativeToolSearch(ChatOptions? options) =>
        options?.Tools?.Any(static tool =>
            string.Equals(tool.Name, NativeToolSearchTool.ToolName, StringComparison.Ordinal)) == true;

    internal static ChatOptions? PreparePromptCacheOptions(
        ChatOptions? options,
        string? preferredPromptCacheKey = null)
    {
        var promptCacheKey = ResolvePromptCacheKey(options, preferredPromptCacheKey);
        var hasProviderContinuation =
            options?.ConversationId is not null ||
            options?.ContinuationToken is not null;
        if (string.IsNullOrWhiteSpace(promptCacheKey) && !hasProviderContinuation)
            return options;

        var prepared = options?.Clone() ?? new ChatOptions();
        prepared.ConversationId = null;
        prepared.ContinuationToken = null;
        if (!string.IsNullOrWhiteSpace(promptCacheKey))
            ApplyPromptCacheKey(prepared, promptCacheKey);
        return prepared;
    }

    internal static void ApplyPromptCacheKey(ChatOptions options, string promptCacheKey)
    {
        if (string.IsNullOrWhiteSpace(promptCacheKey))
            return;

        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        options.AdditionalProperties[PromptCacheKeyAdditionalProperty] = promptCacheKey.Trim();
        PatchOpenAIResponsesRawRepresentationFactory(options, promptCacheKey.Trim());
    }

    internal static void EnableHostedImageGeneration(ChatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        options.AdditionalProperties[HostedImageGenerationEnabledAdditionalProperty] = true;
    }

    public static CreateResponseOptions CreateResponseOptions(
        string model,
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options,
        bool includeReasoning = true) =>
        CreateResponseRequest(model, chatMessages, options, includeReasoning).Options;

    internal static OpenAIResponsesRequest CreateResponseRequest(
        string model,
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options,
        bool includeReasoning = true,
        bool removesUnsupportedOAuthResponsesFields = false)
    {
        var messages = chatMessages as IReadOnlyList<ChatMessage> ?? chatMessages.ToList();
        var callNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var responseOptions = new CreateResponseOptions
        {
            Model = model,
            StreamingEnabled = true,
            StoredOutputEnabled = false
        };

        var instructions = BuildInstructions(messages, options);
        if (!string.IsNullOrWhiteSpace(instructions))
            responseOptions.Instructions = instructions;

        var input = BuildInput(messages, callNames, options);
        var tools = BuildTools(options);

        if (options?.AllowMultipleToolCalls is { } allowMultiple)
            responseOptions.ParallelToolCallsEnabled = allowMultiple;
        if (options?.MaxOutputTokens is { } maxOutputTokens)
            responseOptions.MaxOutputTokenCount = maxOutputTokens;
        ResponseReasoningOptions? reasoningOptions = null;
        if (includeReasoning)
        {
            // Always emit include=reasoning.encrypted_content so reasoning items round-trip across
            // turns when store=false. ReasoningOptions only ships when the caller configured it.
            // IncludedProperties serializes to $.include on its own; do not also Patch.Set the
            // same path or the wire body ends up with duplicate keys that downstream JSON parsers
            // (e.g. JsonNode.Parse in the ChatGPT metadata pipeline policy) reject.
            responseOptions.IncludedProperties.Add(IncludedResponseProperty.ReasoningEncryptedContent);
            reasoningOptions = CreateReasoningOptions(options?.Reasoning);
            if (reasoningOptions is { } reasoning)
                responseOptions.ReasoningOptions = reasoning;
        }

#pragma warning disable SCME0001
        responseOptions.Patch.Set(
            "$.input"u8,
            BinaryData.FromString(input.ToJsonString(JsonOptions)));
        responseOptions.Patch.Set(
            "$.tools"u8,
            BinaryData.FromString(tools.ToJsonString(JsonOptions)));
#pragma warning restore SCME0001

        var promptCacheKey = ResolvePromptCacheKeyWithSource(options);
        if (!string.IsNullOrWhiteSpace(promptCacheKey.Value))
            PatchResponsePromptCacheKey(responseOptions, promptCacheKey.Value!);

        return new OpenAIResponsesRequest(
            responseOptions,
            CreatePromptCacheRequestShapeSnapshot(
                model,
                promptCacheKey,
                instructions,
                input,
                tools,
                includeReasoning,
                reasoningOptions,
                responseOptions,
                options,
                removesUnsupportedOAuthResponsesFields));
    }

    internal static string? ResolvePromptCacheKey(
        ChatOptions? options,
        string? preferredPromptCacheKey = null) =>
        ResolvePromptCacheKeyWithSource(options, preferredPromptCacheKey).Value;

    private static PromptCacheKeyResolution ResolvePromptCacheKeyWithSource(
        ChatOptions? options,
        string? preferredPromptCacheKey = null)
    {
        if (TryReadString(options?.AdditionalProperties, PromptCacheKeyAdditionalProperty, out var configured))
            return new PromptCacheKeyResolution(configured?.Trim(), "additionalProperties");

        if (!string.IsNullOrWhiteSpace(preferredPromptCacheKey))
            return new PromptCacheKeyResolution(preferredPromptCacheKey.Trim(), "preferred");

        var active = OpenAIResponsesCodexRuntimeScope.Current?.ThreadId
            ?? TracingChatClient.CurrentSessionKey
            ?? TracingChatClient.GetActiveSessionKey();
        return string.IsNullOrWhiteSpace(active)
            ? new PromptCacheKeyResolution(null, null)
            : new PromptCacheKeyResolution(active!.Trim(), "activeSession");
    }

    internal static void PatchOpenAIResponsesRawRepresentationFactory(ChatOptions options, string promptCacheKey)
    {
        var existingFactory = options.RawRepresentationFactory;
        options.RawRepresentationFactory = client =>
        {
            var raw = existingFactory?.Invoke(client) ?? new CreateResponseOptions();
            if (raw is CreateResponseOptions responseOptions)
            {
                responseOptions.StoredOutputEnabled = false;
                if (!responseOptions.IncludedProperties.Contains(IncludedResponseProperty.ReasoningEncryptedContent))
                    responseOptions.IncludedProperties.Add(IncludedResponseProperty.ReasoningEncryptedContent);
                PatchResponsePromptCacheKey(responseOptions, promptCacheKey);
            }

            return raw;
        };
    }

    internal static void PatchResponsePromptCacheKey(CreateResponseOptions options, string promptCacheKey) =>
        PatchValue(options, "$.prompt_cache_key", promptCacheKey);

    internal static void PatchResponseServiceTier(CreateResponseOptions options, string serviceTier) =>
        PatchValue(options, "$.service_tier", serviceTier);

    public static async IAsyncEnumerable<StreamingResponseUpdate> NormalizeToolSearchCalls(
        IAsyncEnumerable<StreamingResponseUpdate> updates,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in NormalizeToolSearchCalls(
                           updates,
                           functionCallNamespaces: null,
                           cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return update;
        }
    }

    public static async IAsyncEnumerable<StreamingResponseUpdate> NormalizeToolSearchCalls(
        IAsyncEnumerable<StreamingResponseUpdate> updates,
        IDictionary<string, string>? functionCallNamespaces,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in updates.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (update is StreamingResponseOutputItemDoneUpdate doneWithNamespace
                && TryReadFunctionCallNamespace(doneWithNamespace.Item, out var functionCallId, out var functionNamespace))
            {
                functionCallNamespaces?[functionCallId] = functionNamespace;
            }

            yield return update is StreamingResponseOutputItemDoneUpdate done
                && TryCreateSyntheticToolSearchCall(done.Item, out var functionCall)
                    ? new StreamingResponseOutputItemDoneUpdate
                    {
                        SequenceNumber = done.SequenceNumber,
                        OutputIndex = done.OutputIndex,
                        Item = functionCall
                    }
                    : update;
        }
    }

    public static async IAsyncEnumerable<StreamingResponseUpdate> CaptureHostedImageGenerationCalls(
        IAsyncEnumerable<StreamingResponseUpdate> updates,
        Queue<HostedImageGenerationContent> captured,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(captured);
        var capturedIds = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var update in updates.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (update is StreamingResponseOutputItemDoneUpdate done
                && TryCreateHostedImageGenerationContent(done.Item, out var content))
            {
                EnqueueIfNew(content);
            }
            else if (TryCreateHostedImageGenerationContent(update, out content))
            {
                EnqueueIfNew(content);
            }

            yield return update;
        }

        void EnqueueIfNew(HostedImageGenerationContent content)
        {
            if (!string.IsNullOrWhiteSpace(content.Id) && !capturedIds.Add(content.Id))
                return;

            captured.Enqueue(content);
        }
    }

    public static void ApplyRecordedFunctionCallNamespaces(
        ChatResponseUpdate update,
        IReadOnlyDictionary<string, string> functionCallNamespaces)
    {
        if (functionCallNamespaces.Count == 0)
            return;

        foreach (var call in update.Contents.OfType<FunctionCallContent>())
        {
            if (!functionCallNamespaces.TryGetValue(call.CallId, out var functionNamespace)
                || string.IsNullOrWhiteSpace(functionNamespace))
            {
                continue;
            }

            call.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            call.AdditionalProperties[FunctionCallNamespaceMetadataKey] = functionNamespace;
            call.AdditionalProperties["namespace"] = functionNamespace;
        }
    }

    private static JsonArray BuildInput(
        IEnumerable<ChatMessage> messages,
        Dictionary<string, string> callNames,
        ChatOptions? options)
    {
        var input = new JsonArray();
        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System)
                continue;

            var textBuffer = new StringBuilder();
            var contentParts = new JsonArray();
            void FlushTextPart()
            {
                if (textBuffer.Length == 0)
                    return;

                contentParts.Add(CreateTextContentPart(message.Role, textBuffer.ToString()));
                textBuffer.Clear();
            }

            void FlushMessage()
            {
                FlushTextPart();
                if (contentParts.Count == 0)
                    return;

                input.Add(CreateMessageItem(message.Role, contentParts));
                contentParts = [];
            }

            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case TextContent text:
                        textBuffer.Append(text.Text);
                        break;

                    case TextReasoningContent reasoning when message.Role == ChatRole.Assistant:
                        FlushMessage();
                        if (TryCreateReasoningItem(reasoning, out var reasoningItem))
                            input.Add(reasoningItem);
                        break;

                    case HostedImageGenerationContent imageGeneration:
                        FlushMessage();
                        input.Add(CreateImageGenerationCallItem(imageGeneration));
                        break;

                    case FunctionCallContent call:
                        FlushMessage();
                        if (!string.IsNullOrWhiteSpace(call.CallId))
                            callNames[call.CallId] = call.Name;

                        input.Add(CreateFunctionCallItem(call));
                        break;

                    case FunctionResultContent result:
                        FlushMessage();
                        callNames.TryGetValue(result.CallId, out var toolName);
                        input.Add(string.Equals(toolName, NativeToolSearchTool.ToolName, StringComparison.Ordinal)
                            || IsToolSearchOutput(result.Result)
                            ? CreateToolSearchOutputItem(result)
                            : CreateFunctionCallOutputItem(result));
                        break;

                    default:
                        FlushTextPart();
                        contentParts.Add(CreateContentPartOrPlaceholder(message.Role, content));
                        break;
                }
            }

            FlushMessage();
        }

        if (input.Count == 0 && !string.IsNullOrWhiteSpace(options?.Instructions))
            input.Add(CreateMessageItem(ChatRole.User, "Continue."));

        return input;
    }

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

            if (string.Equals(tool.Name, NativeToolSearchTool.ToolName, StringComparison.Ordinal))
            {
                tools.Add(new JsonObject
                {
                    ["type"] = "tool_search",
                    ["execution"] = "client",
                    ["description"] = tool.Description,
                    ["parameters"] = CloneJsonElement(GetJsonSchema(tool))
                });
                continue;
            }

            var functionTool = CreateFunctionTool(tool);
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

    private static JsonObject CreateFunctionTool(AITool tool)
    {
        var functionName = CanonicalToolIdentityMetadataResolver.TryGet(
            tool,
            out var canonicalName,
            out _)
            ? canonicalName.Name
            : tool.Name;
        var functionTool = new JsonObject
        {
            ["type"] = "function",
            ["name"] = functionName,
            ["description"] = tool.Description,
            ["parameters"] = CloneJsonElement(GetJsonSchema(tool))
        };

        if (tool is IOpenAIResponsesFunctionToolMetadata { Strict: { } strict })
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

    private static ResponseReasoningOptions? CreateReasoningOptions(ReasoningOptions? reasoning)
    {
        if (reasoning == null)
            return null;

        var options = new ResponseReasoningOptions();
        var hasValue = false;
        if (reasoning.Effort is { } effort)
        {
            options.ReasoningEffortLevel = new ResponseReasoningEffortLevel(NormalizeReasoningEffortToken(effort));
            hasValue = true;
        }

        if (reasoning.Output is { } output && output != ReasoningOutput.None)
        {
            options.ReasoningSummaryVerbosity = output == ReasoningOutput.Full
                ? ResponseReasoningSummaryVerbosity.Detailed
                : ResponseReasoningSummaryVerbosity.Auto;
            hasValue = true;
        }

        return hasValue ? options : null;
    }

    private static PromptCacheRequestShapeSnapshot CreatePromptCacheRequestShapeSnapshot(
        string model,
        PromptCacheKeyResolution promptCacheKey,
        string? instructions,
        JsonArray input,
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

    private static JsonObject CreateMessageItem(ChatRole role, string text)
    {
        var contentParts = new JsonArray
        {
            CreateTextContentPart(role, text)
        };
        return CreateMessageItem(role, contentParts);
    }

    private static JsonObject CreateMessageItem(ChatRole role, JsonArray contentParts)
    {
        var itemRole = role == ChatRole.Assistant ? "assistant" : "user";
        return new JsonObject
        {
            ["type"] = "message",
            ["role"] = itemRole,
            ["content"] = contentParts
        };
    }

    private static JsonObject CreateTextContentPart(ChatRole role, string text)
    {
        var textType = role == ChatRole.Assistant ? "output_text" : "input_text";
        return new JsonObject
        {
            ["type"] = textType,
            ["text"] = text
        };
    }

    private static JsonObject CreateContentPartOrPlaceholder(ChatRole role, AIContent content)
    {
        if (role == ChatRole.User)
        {
            if (TryCreateUserImagePart(content, out var imagePart, out var placeholderText))
                return imagePart;
            if (!string.IsNullOrWhiteSpace(placeholderText))
                return CreateTextContentPart(role, placeholderText);
        }

        return CreateTextContentPart(role, DescribeUnsupportedContent(content));
    }

    private static bool TryCreateUserImagePart(
        AIContent content,
        out JsonObject part,
        out string? placeholderText)
    {
        part = null!;
        placeholderText = null;
        string? imageUri = null;

        switch (content)
        {
            case DataContent data when ModelImageInputPreparer.IsImageMediaType(data.MediaType):
                var prepared = ModelImageInputPreparer.Prepare(data);
                if (prepared.Content == null)
                {
                    placeholderText = prepared.PlaceholderText;
                    return false;
                }

                imageUri = CreateDataUri(prepared.Content);
                break;

            case UriContent uri when ModelImageInputPreparer.IsSupportedRemoteImageMediaType(uri.MediaType):
                imageUri = uri.Uri?.ToString();
                break;
        }

        if (string.IsNullOrWhiteSpace(imageUri))
            return false;

        part = new JsonObject
        {
            ["type"] = "input_image",
            ["image_url"] = imageUri
        };
        return true;
    }

    private static string CreateDataUri(DataContent content) =>
        $"data:{NormalizeMediaType(content.MediaType)};base64,{Convert.ToBase64String(content.Data.ToArray())}";

    private static JsonObject CreateFunctionCallItem(FunctionCallContent call)
    {
        if (string.Equals(call.Name, NativeToolSearchTool.ToolName, StringComparison.Ordinal))
        {
            return new JsonObject
            {
                ["type"] = "tool_search_call",
                ["execution"] = "client",
                ["call_id"] = call.CallId,
                ["status"] = "completed",
                ["arguments"] = CreateToolSearchArgumentsObject(call.Arguments)
            };
        }

        var item = new JsonObject
        {
            ["type"] = "function_call",
            ["call_id"] = call.CallId,
            ["name"] = call.Name,
            ["arguments"] = SerializeArguments(call.Arguments)
        };

        if (TryGetFunctionCallNamespace(call, out var functionNamespace))
            item["namespace"] = functionNamespace;

        return item;
    }

    private static JsonObject CreateImageGenerationCallItem(HostedImageGenerationContent content)
    {
        var item = new JsonObject
        {
            ["type"] = HostedImageGenerationContent.ToolName + "_call",
            ["id"] = string.IsNullOrWhiteSpace(content.Id) ? Guid.NewGuid().ToString("N") : content.Id,
            ["status"] = string.IsNullOrWhiteSpace(content.Status) ? "completed" : content.Status
        };

        if (!string.IsNullOrWhiteSpace(content.RevisedPrompt))
            item["revised_prompt"] = content.RevisedPrompt;
        if (content.ImageBytes is { Length: > 0 })
            item["result"] = Convert.ToBase64String(content.ImageBytes);
        return item;
    }

    private static JsonObject CreateFunctionCallOutputItem(FunctionResultContent result) =>
        new()
        {
            ["type"] = "function_call_output",
            ["call_id"] = result.CallId,
            ["output"] = SerializeResult(result.Result)
        };

    private static bool TryCreateReasoningItem(
        TextReasoningContent reasoning,
        out JsonObject item)
    {
        item = null!;

        if (TryCreateReasoningItemFromRaw(reasoning.RawRepresentation, out item))
        {
            if (!item.ContainsKey("encrypted_content") && !string.IsNullOrWhiteSpace(reasoning.ProtectedData))
                item["encrypted_content"] = reasoning.ProtectedData;
            EnsureReasoningDefaults(item);
            return true;
        }

        if (string.IsNullOrWhiteSpace(reasoning.ProtectedData))
            return false;

        item = new JsonObject
        {
            ["type"] = "reasoning",
            ["content"] = new JsonArray(),
            ["summary"] = new JsonArray(),
            ["encrypted_content"] = reasoning.ProtectedData
        };
        return true;
    }

    private static bool TryCreateReasoningItemFromRaw(
        object? rawRepresentation,
        out JsonObject item)
    {
        item = null!;
        if (rawRepresentation == null)
            return false;

        try
        {
            var rawJson = ModelReaderWriter.Write(rawRepresentation).ToString();
            if (JsonNode.Parse(rawJson) is not JsonObject obj)
                return false;
            if (!string.Equals(ReadJsonString(obj, "type"), "reasoning", StringComparison.Ordinal))
                return false;

            item = CloneJsonObject(obj);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or JsonException)
        {
            return false;
        }
    }

    private static void EnsureReasoningDefaults(JsonObject item)
    {
        item["type"] = "reasoning";
        if (!item.ContainsKey("content"))
            item["content"] = new JsonArray();
        if (!item.ContainsKey("summary"))
            item["summary"] = new JsonArray();
    }

    private static JsonObject CreateToolSearchOutputItem(FunctionResultContent result) =>
        new()
        {
            ["type"] = "tool_search_output",
            ["execution"] = "client",
            ["call_id"] = result.CallId,
            ["status"] = "completed",
            ["tools"] = ExtractToolSearchTools(result.Result)
        };

    private static JsonObject CreateToolSearchArgumentsObject(object? arguments)
    {
        var args = ArgumentsToJsonObject(arguments);
        var normalized = CloneJsonObject(args);

        if (!normalized.ContainsKey("query")
            && ReadJsonString(args, "q") is { } q)
        {
            normalized["query"] = q;
        }

        if (!normalized.ContainsKey("max_results")
            && ReadJsonInt(args, "maxResults") is { } maxResults)
        {
            normalized["max_results"] = maxResults;
        }

        if (!normalized.ContainsKey("query"))
            normalized["query"] = string.Empty;

        normalized.Remove("q");
        normalized.Remove("maxResults");
        return normalized;
    }

    private static Dictionary<string, object?> ReadToolSearchArguments(JsonElement item)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (TryGetProperty(item, "arguments", out var argumentsElement))
        {
            if (argumentsElement.ValueKind == JsonValueKind.Object)
            {
                return JsonSerializer.Deserialize<Dictionary<string, object?>>(
                    argumentsElement.GetRawText(),
                    JsonOptions) ?? arguments;
            }

            if (argumentsElement.ValueKind == JsonValueKind.String
                && argumentsElement.GetString() is { } argumentsText
                && TryDeserializeArguments(argumentsText, out var parsedArguments))
            {
                return parsedArguments;
            }
        }

        if (ReadString(item, "query") is { } query)
            arguments["query"] = query;
        return arguments;
    }

    private static string ConcatenateInstructionContent(IEnumerable<AIContent> contents)
    {
        var sb = new StringBuilder();
        var needsSeparatorBeforeText = false;
        foreach (var content in contents)
        {
            if (content is TextContent text)
            {
                if (needsSeparatorBeforeText
                    && !string.IsNullOrEmpty(text.Text)
                    && sb.Length > 0
                    && !char.IsWhiteSpace(sb[sb.Length - 1]))
                {
                    sb.AppendLine();
                }

                sb.Append(text.Text);
                needsSeparatorBeforeText = false;
                continue;
            }

            if (sb.Length > 0 && !char.IsWhiteSpace(sb[sb.Length - 1]))
                sb.AppendLine();
            sb.Append(DescribeUnsupportedContent(content));
            needsSeparatorBeforeText = true;
        }

        return sb.ToString();
    }

    private static JsonArray ExtractToolSearchTools(object? result)
    {
        var node = JsonSerializer.SerializeToNode(result, JsonOptions);
        if (node is JsonObject obj
            && (obj["tools"] as JsonArray ?? obj["Tools"] as JsonArray) is { } tools)
        {
            return CloneJsonArray(tools);
        }

        return [];
    }

    private static bool IsToolSearchOutput(object? result) =>
        ExtractToolSearchTools(result).Count > 0;

    private static bool IsHostedImageGenerationEnabled(ChatOptions? options) =>
        TryReadBool(options?.AdditionalProperties, HostedImageGenerationEnabledAdditionalProperty, out var enabled) &&
        enabled;

    private static bool IsReservedImageGenerationFunction(AITool tool)
    {
        if (!string.Equals(tool.Name, ProviderHostedCapabilityPlanner.ToolName, StringComparison.Ordinal))
            return false;

        return ToolNamespaceMetadataResolver.TryGet(tool, out var toolNamespace) &&
               string.Equals(toolNamespace, ProviderHostedCapabilityPlanner.ToolNamespace, StringComparison.Ordinal);
    }

    private static string SerializeArguments(object? arguments)
    {
        if (arguments == null)
            return "{}";
        if (arguments is string text)
            return text;
        if (arguments is JsonElement element)
            return element.GetRawText();
        if (arguments is JsonObject node)
            return node.ToJsonString(JsonOptions);
        return JsonSerializer.Serialize(arguments, JsonOptions);
    }

    private static string SerializeResult(object? result)
    {
        if (result == null)
            return string.Empty;
        if (result is string text)
            return text;
        if (result is AIContent content)
            return SerializeAIContentResult([content]);
        if (result is IEnumerable<AIContent> contents)
            return SerializeAIContentResult(contents);
        if (result is JsonElement element)
            return element.GetRawText();
        if (result is JsonNode node)
            return node.ToJsonString(JsonOptions);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    private static string SerializeAIContentResult(IEnumerable<AIContent> contents)
    {
        var parts = new List<string>();
        foreach (var content in contents)
        {
            switch (content)
            {
                case TextContent text when !string.IsNullOrEmpty(text.Text):
                    parts.Add(text.Text);
                    break;

                case DataContent data:
                    parts.Add(DescribeUnsupportedContent(data));
                    break;

                case UriContent uri:
                    parts.Add(DescribeUnsupportedContent(uri));
                    break;

                default:
                    parts.Add(DescribeUnsupportedContent(content));
                    break;
            }
        }

        return string.Join("\n", parts);
    }

    private static string DescribeUnsupportedContent(AIContent content)
    {
        var typeName = content.GetType().Name;
        var mediaType = content switch
        {
            DataContent data => NormalizeMediaType(data.MediaType),
            UriContent uri => NormalizeMediaType(uri.MediaType),
            _ => null
        };

        return mediaType == null
            ? $"[Unsupported content: {typeName}]"
            : $"[Unsupported content: {typeName} ({mediaType})]";
    }

    private static string NormalizeMediaType(string? mediaType) =>
        string.IsNullOrWhiteSpace(mediaType)
            ? "application/octet-stream"
            : mediaType.Trim();

    private static JsonObject ArgumentsToJsonObject(object? arguments)
    {
        if (arguments is JsonObject jsonObject)
            return CloneJsonObject(jsonObject);
        if (arguments is JsonElement element && element.ValueKind == JsonValueKind.Object)
            return JsonNode.Parse(element.GetRawText()) as JsonObject ?? [];
        if (arguments == null)
            return [];

        var node = JsonSerializer.SerializeToNode(arguments, JsonOptions);
        return node as JsonObject ?? [];
    }

    private static string? ReadJsonString(JsonObject obj, string name) =>
        obj.TryGetPropertyValue(name, out var value) && value != null
            ? value.GetValue<string?>()
            : null;

    private static int? ReadJsonInt(JsonObject obj, string name)
    {
        if (!obj.TryGetPropertyValue(name, out var value) || value == null)
            return null;

        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<int>(out var number))
                return number;
            if (jsonValue.TryGetValue<long>(out var longNumber))
                return checked((int)longNumber);
            if (jsonValue.TryGetValue<string>(out var text) && int.TryParse(text, out number))
                return number;
        }

        return null;
    }

    private static bool TryDeserializeArguments(
        string argumentsText,
        out Dictionary<string, object?> arguments)
    {
        try
        {
            arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                argumentsText,
                JsonOptions) ?? [];
            return true;
        }
        catch (JsonException)
        {
            arguments = [];
            return false;
        }
    }

    private static bool TryCreateSyntheticToolSearchCall(
        ResponseItem item,
        out FunctionCallResponseItem functionCall)
    {
        functionCall = null!;

        try
        {
            var itemJson = ModelReaderWriter.Write(item).ToString();
            if (OpenAIResponsesRequestBodyCanonicalizer.NormalizeTopLevelObject(itemJson) is not { } normalizedJson)
                return false;

            using var document = JsonDocument.Parse(normalizedJson);
            var root = document.RootElement;
            if (!string.Equals(ReadString(root, "type"), "tool_search_call", StringComparison.Ordinal))
                return false;

            var callId = ReadString(root, "call_id")
                ?? ReadString(root, "id")
                ?? Guid.NewGuid().ToString("N");
            var arguments = ReadToolSearchArguments(root);
            functionCall = new FunctionCallResponseItem(
                callId,
                NativeToolSearchTool.ToolName,
                BinaryData.FromString(JsonSerializer.Serialize(arguments, JsonOptions)));
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    internal static bool TryCreateHostedImageGenerationContent(
        ResponseItem item,
        out HostedImageGenerationContent content)
    {
        content = null!;

        try
        {
            if (!TryReadJsonObjectFromRaw(item, out var rawObject))
                return false;
            if (!string.Equals(
                    ReadJsonString(rawObject, "type"),
                    HostedImageGenerationContent.ToolName + "_call",
                    StringComparison.Ordinal))
            {
                return false;
            }

            var id = ReadJsonString(rawObject, "id")
                ?? ReadJsonString(rawObject, "call_id")
                ?? Guid.NewGuid().ToString("N");
            var status = ReadJsonString(rawObject, "status") ?? "completed";
            var revisedPrompt = ReadJsonString(rawObject, "revised_prompt");

            if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                content = new HostedImageGenerationContent
                {
                    Id = id,
                    Status = status,
                    RevisedPrompt = revisedPrompt,
                    ErrorMessage = ReadImageGenerationError(rawObject)
                        ?? $"Image generation {status}."
                };
                return true;
            }

            if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
                return false;

            var result = ReadJsonString(rawObject, "result");
            if (string.IsNullOrWhiteSpace(result))
            {
                content = new HostedImageGenerationContent
                {
                    Id = id,
                    Status = status,
                    RevisedPrompt = revisedPrompt,
                    ErrorMessage = "Image generation completed without image data."
                };
                return true;
            }

            byte[] imageBytes;
            try
            {
                imageBytes = Convert.FromBase64String(result.Trim());
            }
            catch (FormatException)
            {
                content = new HostedImageGenerationContent
                {
                    Id = id,
                    Status = status,
                    RevisedPrompt = revisedPrompt,
                    ErrorMessage = "Image generation returned invalid image data."
                };
                return true;
            }

            content = new HostedImageGenerationContent
            {
                Id = id,
                Status = status,
                RevisedPrompt = revisedPrompt,
                ImageBytes = imageBytes,
                MediaType = "image/png"
            };
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    internal static bool TryCreateHostedImageGenerationContent(
        StreamingResponseUpdate update,
        out HostedImageGenerationContent content)
    {
        content = null!;

        try
        {
            var rawJson = ModelReaderWriter.Write(update).ToString();
            if (OpenAIResponsesRequestBodyCanonicalizer.NormalizeTopLevelObject(rawJson) is not { } normalizedJson)
                return false;

            using var document = JsonDocument.Parse(normalizedJson);
            var root = document.RootElement;
            var eventType = ReadString(root, "type");
            if (!string.Equals(eventType, "response.image_generation_call.completed", StringComparison.Ordinal) &&
                !string.Equals(eventType, "response.image_generation_call.failed", StringComparison.Ordinal))
            {
                return false;
            }

            var id = ReadString(root, "item_id")
                ?? ReadString(root, "id")
                ?? ReadString(root, "call_id")
                ?? Guid.NewGuid().ToString("N");
            var revisedPrompt = ReadString(root, "revised_prompt");
            var status = string.Equals(eventType, "response.image_generation_call.completed", StringComparison.Ordinal)
                ? "completed"
                : "failed";

            if (!string.Equals(status, "completed", StringComparison.Ordinal))
            {
                content = new HostedImageGenerationContent
                {
                    Id = id,
                    Status = status,
                    RevisedPrompt = revisedPrompt,
                    ErrorMessage = ReadImageGenerationError(root) ?? "Image generation failed."
                };
                return true;
            }

            var result = ReadString(root, "result");
            if (string.IsNullOrWhiteSpace(result))
            {
                content = new HostedImageGenerationContent
                {
                    Id = id,
                    Status = status,
                    RevisedPrompt = revisedPrompt,
                    ErrorMessage = "Image generation completed without image data."
                };
                return true;
            }

            byte[] imageBytes;
            try
            {
                imageBytes = Convert.FromBase64String(result.Trim());
            }
            catch (FormatException)
            {
                content = new HostedImageGenerationContent
                {
                    Id = id,
                    Status = status,
                    RevisedPrompt = revisedPrompt,
                    ErrorMessage = "Image generation returned invalid image data."
                };
                return true;
            }

            content = new HostedImageGenerationContent
            {
                Id = id,
                Status = status,
                RevisedPrompt = revisedPrompt,
                ImageBytes = imageBytes,
                MediaType = "image/png"
            };
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    private static string? ReadImageGenerationError(JsonObject rawObject)
    {
        if (ReadJsonString(rawObject, "error") is { } textError)
            return textError;

        if (!rawObject.TryGetPropertyValue("error", out var errorNode) || errorNode is not JsonObject errorObject)
            return null;

        var message = ReadJsonString(errorObject, "message");
        var code = ReadJsonString(errorObject, "code");
        return string.IsNullOrWhiteSpace(code)
            ? message
            : string.IsNullOrWhiteSpace(message)
                ? code
                : $"{code}: {message}";
    }

    private static string? ReadImageGenerationError(JsonElement root)
    {
        if (ReadString(root, "error") is { } textError)
            return textError;

        if (!TryGetProperty(root, "error", out var error) || error.ValueKind != JsonValueKind.Object)
            return null;

        var message = ReadString(error, "message");
        var code = ReadString(error, "code");
        return string.IsNullOrWhiteSpace(code)
            ? message
            : string.IsNullOrWhiteSpace(message)
                ? code
                : $"{code}: {message}";
    }

    internal static bool TryGetFunctionCallNamespace(
        FunctionCallContent call,
        out string functionNamespace)
    {
        functionNamespace = string.Empty;

        if (TryReadString(call.AdditionalProperties, FunctionCallNamespaceMetadataKey, out functionNamespace)
            || TryReadString(call.AdditionalProperties, "namespace", out functionNamespace))
        {
            return true;
        }

        return TryReadJsonObjectFromRaw(call.RawRepresentation, out var rawObject)
               && TryReadString(rawObject, "namespace", out functionNamespace);
    }

    private static bool TryReadFunctionCallNamespace(
        ResponseItem item,
        out string callId,
        out string functionNamespace)
    {
        callId = string.Empty;
        functionNamespace = string.Empty;

        if (!TryReadJsonObjectFromRaw(item, out var rawObject))
            return false;
        if (!string.Equals(ReadJsonString(rawObject, "type"), "function_call", StringComparison.Ordinal))
            return false;
        if (!TryReadString(rawObject, "namespace", out functionNamespace))
            return false;

        callId = ReadJsonString(rawObject, "call_id")
            ?? ReadJsonString(rawObject, "id")
            ?? string.Empty;
        return !string.IsNullOrWhiteSpace(callId);
    }

    private static bool TryReadJsonObjectFromRaw(
        object? rawRepresentation,
        out JsonObject rawObject)
    {
        rawObject = null!;
        if (rawRepresentation == null)
            return false;

        try
        {
            if (rawRepresentation is JsonObject obj)
            {
                rawObject = obj;
                return true;
            }

            if (rawRepresentation is JsonElement { ValueKind: JsonValueKind.Object } element)
            {
                if (OpenAIResponsesRequestBodyCanonicalizer.NormalizeTopLevelObject(element.GetRawText()) is not { } normalizedJson)
                    return false;

                rawObject = JsonNode.Parse(normalizedJson) as JsonObject ?? [];
                return true;
            }

            var rawJson = ModelReaderWriter.Write(rawRepresentation).ToString();
            if (OpenAIResponsesRequestBodyCanonicalizer.NormalizeTopLevelObject(rawJson) is not { } normalizedRawJson)
                return false;

            rawObject = JsonNode.Parse(normalizedRawJson) as JsonObject ?? [];
            return rawObject.Count > 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    private static bool TryReadString(
        AdditionalPropertiesDictionary? properties,
        string name,
        out string value)
    {
        value = string.Empty;
        if (properties == null || !properties.TryGetValue(name, out var propertyValue))
            return false;

        switch (propertyValue)
        {
            case string text when !string.IsNullOrWhiteSpace(text):
                value = text;
                return true;

            case JsonElement { ValueKind: JsonValueKind.String } element
                when !string.IsNullOrWhiteSpace(element.GetString()):
                value = element.GetString()!;
                return true;

            case JsonValue jsonValue when jsonValue.TryGetValue<string>(out var text)
                                      && !string.IsNullOrWhiteSpace(text):
                value = text;
                return true;

            default:
                return false;
        }
    }

    private static bool TryReadBool(
        AdditionalPropertiesDictionary? properties,
        string name,
        out bool value)
    {
        value = false;
        if (properties == null || !properties.TryGetValue(name, out var propertyValue))
            return false;

        switch (propertyValue)
        {
            case bool boolean:
                value = boolean;
                return true;

            case JsonElement { ValueKind: JsonValueKind.True }:
                value = true;
                return true;

            case JsonElement { ValueKind: JsonValueKind.False }:
                value = false;
                return true;

            case JsonValue jsonValue when jsonValue.TryGetValue<bool>(out var boolean):
                value = boolean;
                return true;

            case string text when bool.TryParse(text, out var boolean):
                value = boolean;
                return true;

            default:
                return false;
        }
    }

    private static bool TryReadString(
        JsonObject obj,
        string name,
        out string value)
    {
        value = string.Empty;
        var text = ReadJsonString(obj, name);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        value = text;
        return true;
    }

    private static JsonNode CloneJsonElement(JsonElement element) =>
        element.ValueKind == JsonValueKind.Undefined
            ? new JsonObject { ["type"] = "object" }
            : JsonNode.Parse(element.GetRawText()) ?? new JsonObject { ["type"] = "object" };

    private static JsonElement GetJsonSchema(AITool tool) =>
        tool is AIFunction function
            ? function.JsonSchema
            : JsonSerializer.SerializeToElement(new JsonObject { ["type"] = "object" });

    private static JsonObject CloneJsonObject(JsonObject obj) =>
        JsonNode.Parse(obj.ToJsonString(JsonOptions)) as JsonObject ?? [];

    private static JsonArray CloneJsonArray(JsonArray array) =>
        JsonNode.Parse(array.ToJsonString(JsonOptions)) as JsonArray ?? [];

    private static void PatchJson(CreateResponseOptions options, string path, JsonNode node)
    {
#pragma warning disable SCME0001
        options.Patch.Set(
            Encoding.UTF8.GetBytes(path),
            BinaryData.FromString(node.ToJsonString(JsonOptions)));
#pragma warning restore SCME0001
    }

    private static void PatchValue<T>(CreateResponseOptions options, string path, T value)
    {
#pragma warning disable SCME0001
        options.Patch.Set(
            Encoding.UTF8.GetBytes(path),
            BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions)));
#pragma warning restore SCME0001
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

    private static string? ReadString(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string NormalizeReasoningEffortToken(ReasoningEffort effort) => effort switch
    {
        ReasoningEffort.ExtraHigh => "xhigh",
        _ => NormalizeEnumToken(effort.ToString())
    };

    private static string NormalizeEnumToken(string value)
    {
        var chars = value.Where(static ch => ch is not '-' and not '_' and not ' ').ToArray();
        return new string(chars).ToLowerInvariant();
    }

    private sealed record PromptCacheKeyResolution(string? Value, string? Source);
}
