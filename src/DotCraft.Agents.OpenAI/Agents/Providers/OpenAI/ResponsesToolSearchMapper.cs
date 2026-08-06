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

internal static partial class ResponsesToolSearchMapper
{
    internal const string FunctionCallNamespaceMetadataKey = "openai.responses.function_call.namespace";
    internal const string PromptCacheKeyAdditionalProperty = "prompt_cache_key";
    internal const string HostedImageGenerationEnabledAdditionalProperty = "dotcraft.openai.responses.image_generation.enabled";

    private const int PromptCacheRequestShapeSchemaVersion = 2;
    private const string OpenAIResponsesProtocolName = "openai-responses";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly AIJsonSchemaTransformOptions OpenAIStrictSchemaTransformOptions = new()
    {
        DisallowAdditionalProperties = true,
        ConvertBooleanSchemas = true,
        MoveDefaultKeywordToDescription = true,
        RequireAllProperties = true,
        TransformSchemaNode = static (_, node) =>
        {
            if (node is not JsonObject schema)
                return node;

            StringBuilder? additionalDescription = null;
            ReadOnlySpan<string> unsupportedProperties =
            [
                "contentEncoding", "contentMediaType", "not",
                "minLength", "maxLength", "pattern", "format",
                "minimum", "maximum", "multipleOf",
                "patternProperties",
                "minItems", "maxItems",
                "unevaluatedProperties", "propertyNames", "minProperties", "maxProperties",
                "unevaluatedItems", "contains", "minContains", "maxContains", "uniqueItems"
            ];
            foreach (var propertyName in unsupportedProperties)
            {
                if (schema[propertyName] is not { } propertyValue)
                    continue;

                schema.Remove(propertyName);
                if (additionalDescription is { Length: > 0 })
                    additionalDescription.AppendLine();
                (additionalDescription ??= new StringBuilder())
                    .Append(propertyName)
                    .Append(": ")
                    .Append(propertyValue);
            }

            if (additionalDescription is not null)
            {
                schema["description"] =
                    schema["description"] is { } description
                    && description.GetValueKind() == JsonValueKind.String
                        ? description.GetValue<string>() + Environment.NewLine + additionalDescription
                        : additionalDescription.ToString();
            }
            return node;
        }
    };

    internal sealed record OpenAIResponsesRequest(
        CreateResponseOptions Options,
        PromptCacheRequestShapeSnapshot Shape);

    internal sealed record BuildInputResult(
        JsonArray Input,
        OpenAIResponsesItemIdentityDiagnostics ItemIdentity);

    public static bool HasNativeToolSearch(ChatOptions? options) =>
        options?.Tools?.Any(static tool =>
            string.Equals(tool.Name, OpenAIHostedToolNames.ToolSearch, StringComparison.Ordinal)) == true;

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
        bool removesUnsupportedOAuthResponsesFields = false,
        JsonArray? canonicalInput = null,
        OpenAIResponsesItemIdentityDiagnostics? canonicalItemIdentity = null,
        IChatClient? rawRepresentationClient = null)
    {
        var messages = chatMessages as IReadOnlyList<ChatMessage> ?? chatMessages.ToList();
        var callNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var responseOptions =
            rawRepresentationClient is not null
            && options?.RawRepresentationFactory?.Invoke(rawRepresentationClient) is CreateResponseOptions rawOptions
                ? rawOptions
                : new CreateResponseOptions();
        responseOptions.Model ??= options?.ModelId ?? model;
        responseOptions.StreamingEnabled = true;
        responseOptions.StoredOutputEnabled = false;

        var instructions = BuildInstructions(messages, options);
        if (!string.IsNullOrWhiteSpace(instructions))
        {
            responseOptions.Instructions = string.IsNullOrWhiteSpace(responseOptions.Instructions)
                ? instructions
                : responseOptions.Instructions + Environment.NewLine + instructions;
        }

        var inputResult = canonicalInput == null
            ? BuildInput(messages, callNames, options, addContinuationWhenEmpty: true)
            : new BuildInputResult(
                canonicalInput,
                canonicalItemIdentity ?? OpenAIResponsesItemIdentityDiagnostics.FromInput(canonicalInput));
        var input = inputResult.Input;
        var tools = BuildTools(options);

        if (tools.Count > 0 && options?.AllowMultipleToolCalls is { } allowMultiple)
            responseOptions.ParallelToolCallsEnabled ??= allowMultiple;
        ApplyToolChoice(responseOptions, options, tools.Count);
        if (options?.MaxOutputTokens is { } maxOutputTokens)
            responseOptions.MaxOutputTokenCount ??= maxOutputTokens;
        if (options?.Temperature is { } temperature)
            responseOptions.Temperature ??= temperature;
        if (options?.TopP is { } topP)
            responseOptions.TopP ??= topP;
        ApplyResponseFormat(responseOptions, options);
        ResponseReasoningOptions? reasoningOptions = null;
        if (includeReasoning)
        {
            // Always emit include=reasoning.encrypted_content so reasoning items round-trip across
            // turns when store=false. ChatGPT also expects a reasoning object even when no effort
            // or summary preference is configured.
            // IncludedProperties serializes to $.include on its own; do not also Patch.Set the
            // same path or the wire body ends up with duplicate keys that downstream JSON parsers
            // (e.g. JsonNode.Parse in the ChatGPT metadata pipeline policy) reject.
            if (!responseOptions.IncludedProperties.Contains(IncludedResponseProperty.ReasoningEncryptedContent))
                responseOptions.IncludedProperties.Add(IncludedResponseProperty.ReasoningEncryptedContent);
            reasoningOptions = CreateReasoningOptions(options?.Reasoning);
            responseOptions.ReasoningOptions ??= reasoningOptions;
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
                responseOptions.Model ?? model,
                promptCacheKey,
                instructions,
                input,
                inputResult.ItemIdentity,
                tools,
                includeReasoning,
                responseOptions.ReasoningOptions,
                responseOptions,
                options,
                removesUnsupportedOAuthResponsesFields));
    }

    private static void ApplyToolChoice(
        CreateResponseOptions responseOptions,
        ChatOptions? options,
        int toolCount)
    {
        if (toolCount == 0 || responseOptions.ToolChoice is not null)
            return;

        responseOptions.ToolChoice = options?.ToolMode switch
        {
            NoneChatToolMode => ResponseToolChoice.CreateNoneChoice(),
            AutoChatToolMode => ResponseToolChoice.CreateAutoChoice(),
            RequiredChatToolMode { RequiredFunctionName: { } functionName } =>
                ResponseToolChoice.CreateFunctionChoice(functionName),
            RequiredChatToolMode => ResponseToolChoice.CreateRequiredChoice(),
            _ => null
        };
    }

    private static void ApplyResponseFormat(
        CreateResponseOptions responseOptions,
        ChatOptions? options)
    {
        if (responseOptions.TextOptions?.TextFormat is not null)
            return;

        ResponseTextFormat? format = options?.ResponseFormat switch
        {
            ChatResponseFormatText => ResponseTextFormat.CreateTextFormat(),
            ChatResponseFormatJson { Schema: { } schema } json =>
                ResponseTextFormat.CreateJsonSchemaFormat(
                    json.SchemaName ?? "json_schema",
                    BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(
                        AIJsonUtilities.TransformSchema(schema, OpenAIStrictSchemaTransformOptions),
                        JsonOptions)),
                    json.SchemaDescription,
                    IsStrict(options)),
            ChatResponseFormatJson => ResponseTextFormat.CreateJsonObjectFormat(),
            _ => null
        };
        if (format is not null)
            (responseOptions.TextOptions ??= new ResponseTextOptions()).TextFormat = format;
    }

    private static bool? IsStrict(ChatOptions? options) =>
        options?.AdditionalProperties?.TryGetValue("strict", out var value) == true
        && value is bool strict
            ? strict
            : null;

    internal static string? ResolvePromptCacheKey(
        ChatOptions? options,
        string? preferredPromptCacheKey = null) =>
        ResolvePromptCacheKeyWithSource(options, preferredPromptCacheKey).Value;

    private static PromptCacheKeyResolution ResolvePromptCacheKeyWithSource(
        ChatOptions? options,
        string? preferredPromptCacheKey = null)
    {
        if (TryReadString(options?.AdditionalProperties, ProviderPromptCacheMetadata.PromptCacheKey, out var neutralConfigured))
            return new PromptCacheKeyResolution(neutralConfigured?.Trim(), "additionalProperties");

        if (TryReadString(options?.AdditionalProperties, PromptCacheKeyAdditionalProperty, out var configured))
            return new PromptCacheKeyResolution(configured?.Trim(), "additionalProperties");

        if (!string.IsNullOrWhiteSpace(preferredPromptCacheKey))
            return new PromptCacheKeyResolution(preferredPromptCacheKey.Trim(), "preferred");

        var active = OpenAIResponsesCodexMetadata.ResolveRoutingIdentity().DefaultPromptCacheKey;
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
            call.AdditionalProperties[ProviderFunctionCallMetadata.NamespaceKey] = functionNamespace;
            call.AdditionalProperties["namespace"] = functionNamespace;
        }
    }

    internal static BuildInputResult BuildInputItems(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        IReadOnlyDictionary<string, string>? callNames = null,
        int itemOrdinalOffset = 0)
    {
        var mutableCallNames = callNames == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(callNames, StringComparer.Ordinal);
        return BuildInput(
            messages,
            mutableCallNames,
            options,
            addContinuationWhenEmpty: false,
            itemOrdinalOffset);
    }

    private static BuildInputResult BuildInput(
        IEnumerable<ChatMessage> messages,
        Dictionary<string, string> callNames,
        ChatOptions? options,
        bool addContinuationWhenEmpty,
        int itemOrdinalOffset = 0)
    {
        var input = new JsonArray();
        var identity = new OpenAIResponsesItemIdentityDiagnostics();
        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System)
                continue;

            var textBuffer = new StringBuilder();
            var contentParts = new JsonArray();
            var messageItemOrdinal = 0;
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

                var item = CreateMessageItem(message.Role, contentParts);
                OpenAIResponsesItemIdentity.Assign(
                    message,
                    item,
                    "msg",
                    itemOrdinalOffset + input.Count,
                    messageItemOrdinal++,
                    identity);
                input.Add(item);
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
                        {
                            OpenAIResponsesItemIdentity.Assign(
                                reasoning,
                                reasoningItem,
                                "rs",
                                itemOrdinalOffset + input.Count,
                                identity,
                                ReadJsonString(reasoningItem, "id"));
                            input.Add(reasoningItem);
                        }
                        break;

                    case HostedImageGenerationContent imageGeneration:
                        FlushMessage();
                        var imageItem = CreateImageGenerationCallItem(imageGeneration);
                        OpenAIResponsesItemIdentity.Assign(
                            imageGeneration,
                            imageItem,
                            "ig",
                            itemOrdinalOffset + input.Count,
                            identity,
                            imageGeneration.Id);
                        input.Add(imageItem);
                        break;

                    case FunctionCallContent call:
                        FlushMessage();
                        if (!string.IsNullOrWhiteSpace(call.CallId))
                            callNames[call.CallId] = call.Name;

                        var callItem = CreateFunctionCallItem(call);
                        OpenAIResponsesItemIdentity.Assign(
                            call,
                            callItem,
                            string.Equals(call.Name, OpenAIHostedToolNames.ToolSearch, StringComparison.Ordinal)
                                ? "tsc"
                                : "fc",
                            itemOrdinalOffset + input.Count,
                            identity,
                            ReadJsonString(callItem, "id"));
                        input.Add(callItem);
                        break;

                    case FunctionResultContent result:
                        FlushMessage();
                        callNames.TryGetValue(result.CallId, out var toolName);
                        var isToolSearchOutput =
                            string.Equals(toolName, OpenAIHostedToolNames.ToolSearch, StringComparison.Ordinal)
                            || IsToolSearchOutput(result.Result);
                        var resultItem = isToolSearchOutput
                            ? CreateToolSearchOutputItem(result)
                            : CreateFunctionCallOutputItem(result);
                        OpenAIResponsesItemIdentity.Assign(
                            result,
                            resultItem,
                            isToolSearchOutput ? "tso" : "fco",
                            itemOrdinalOffset + input.Count,
                            identity,
                            ReadJsonString(resultItem, "id"));
                        input.Add(resultItem);
                        break;

                    default:
                        FlushTextPart();
                        contentParts.Add(CreateContentPartOrPlaceholder(message.Role, content));
                        break;
                }
            }

            FlushMessage();
        }

        if (addContinuationWhenEmpty
            && input.Count == 0
            && !string.IsNullOrWhiteSpace(options?.Instructions))
        {
            var continuation = new ChatMessage(ChatRole.User, "Continue.");
            var item = CreateMessageItem(ChatRole.User, "Continue.");
            OpenAIResponsesItemIdentity.Assign(
                continuation,
                item,
                "msg",
                itemOrdinalOffset + input.Count,
                messageItemOrdinal: 0,
                identity);
            input.Add(item);
        }

        SanitizeResponseItemIds(input);
        return new BuildInputResult(input, identity);
    }

    private static void SanitizeResponseItemIds(JsonArray input)
    {
        foreach (var item in input.OfType<JsonObject>())
        {
            if (!item.TryGetPropertyValue("id", out var idNode))
                continue;

            var id = idNode is JsonValue value && value.TryGetValue<string>(out var text)
                ? text
                : null;
            if (id is null || !IsPrefixedResponseItemId(id))
                item.Remove("id");
        }
    }

    private static bool IsPrefixedResponseItemId(string id)
    {
        var separator = id.IndexOf('_');
        return separator > 0 && separator < id.Length - 1;
    }

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
        var itemRole = role == ChatRole.Assistant
            ? "assistant"
            : IsDeveloperRole(role)
                ? "developer"
                : "user";
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

    private static bool IsDeveloperRole(ChatRole role) =>
        string.Equals(role.Value, "developer", StringComparison.OrdinalIgnoreCase);

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
        if (string.Equals(call.Name, OpenAIHostedToolNames.ToolSearch, StringComparison.Ordinal))
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

}
