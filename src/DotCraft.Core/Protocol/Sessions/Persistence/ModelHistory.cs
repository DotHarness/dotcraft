using System.Text.Json;
using DotCraft.Agents;
using Microsoft.Extensions.AI;
using SessionTurn = DotCraft.Sessions.SessionTurn;

namespace DotCraft.Sessions;

internal interface IRolloutReplayer
{
    Task<ModelHistoryReplayResult> ReplayModelHistoryAsync(
        string rolloutPath,
        IReadOnlyList<SessionTurn> survivingTurns,
        string? excludedTurnId = null,
        CancellationToken ct = default,
        string? expectedThreadId = null);
}

#pragma warning disable MEAI001 // Persist the complete runtime usage contract, including preview counters.
internal sealed class ModelHistoryCodec
{
    public const int CurrentSchemaVersion = 1;

    private const int CurrentResultSchemaVersion = 1;
    private const string ProviderFlatNameMetadataKey = "dotcraft.tool.provider_flat_name";
    private const string FunctionNamespaceMetadataKey = "openai.responses.function_call.namespace";
    private const string FunctionNamespaceAliasKey = "namespace";

    private static readonly JsonSerializerOptions JsonOptions = SessionPersistenceJsonOptions.Default;
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerOptions.Web);

    public ModelHistoryMessage Encode(ChatMessage message, string? turnId = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new ModelHistoryMessage
        {
            SchemaVersion = CurrentSchemaVersion,
            TurnId = turnId,
            Role = message.Role.Value,
            MessageId = message.MessageId,
            AuthorName = message.AuthorName,
            CreatedAt = message.CreatedAt,
            AdditionalProperties = SerializeAdditionalProperties(message.AdditionalProperties),
            Contents = message.Contents
                .Where(static content => content is not ToolCallArgumentsDeltaContent)
                .Select(EncodeContent)
                .ToList()
        };
    }

    public ChatMessage Decode(ModelHistoryMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            ValidateMessage(message);
            if (message.SchemaVersion != CurrentSchemaVersion)
                throw new NotSupportedException($"Unsupported model history schema version '{message.SchemaVersion}'.");

            var decoded = new ChatMessage(new ChatRole(message.Role), message.Contents!.Select(DecodeContent).ToList())
            {
                MessageId = message.MessageId,
                AuthorName = message.AuthorName,
                CreatedAt = message.CreatedAt,
                AdditionalProperties = DeserializeAdditionalProperties(message.AdditionalProperties)
            };
            return decoded;
        }
        catch (JsonException)
        {
            throw;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException or InvalidOperationException or NullReferenceException)
        {
            throw new JsonException("Model history message contains invalid persisted content.", ex);
        }
    }

    private static void ValidateMessage(ModelHistoryMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Role))
            throw new JsonException("Model history message is missing its role.");
        if (message.Contents is null)
            throw new JsonException("Model history message is missing its contents.");
        if (message.Contents.Any(static content => content is null))
            throw new JsonException("Model history message contains a null content entry.");
    }

    private static ModelHistoryContent EncodeContent(AIContent content)
    {
        return content switch
        {
            TextContent value => CreateContent("text", new PersistedTextContent
            {
                Text = value.Text,
                AdditionalProperties = SerializeAdditionalProperties(value.AdditionalProperties)
            }),
            TextReasoningContent value => CreateContent("reasoning", new PersistedReasoningContent
            {
                Text = value.Text,
                ProtectedData = value.ProtectedData,
                AdditionalProperties = SerializeAdditionalProperties(value.AdditionalProperties)
            }),
            DataContent value => CreateContent("data", new PersistedDataContent
            {
                Base64Data = value.Base64Data.ToString(),
                MediaType = value.MediaType,
                Name = value.Name,
                AdditionalProperties = SerializeAdditionalProperties(value.AdditionalProperties)
            }),
            FunctionCallContent value => EncodeFunctionCall(value),
            FunctionResultContent value => CreateContent("function_result", new PersistedFunctionResultContent
            {
                CallId = value.CallId,
                Result = EncodeFunctionResult(value.Result),
                AdditionalProperties = SerializeAdditionalProperties(value.AdditionalProperties)
            }),
            HostedImageGenerationContent value => CreateContent(
                "hosted_image_generation",
                new PersistedHostedImageGenerationContent
                {
                    Id = value.Id,
                    Status = value.Status,
                    RevisedPrompt = value.RevisedPrompt,
                    ImageBase64 = value.ImageBytes is null ? null : Convert.ToBase64String(value.ImageBytes),
                    MediaType = value.MediaType,
                    ErrorMessage = value.ErrorMessage,
                    AdditionalProperties = SerializeAdditionalProperties(value.AdditionalProperties)
                }),
            ImageGenerationToolCallContent value => CreateContent(
                "image_generation_tool_call",
                new PersistedImageGenerationToolCallContent
                {
                    CallId = value.CallId,
                    AdditionalProperties = SerializeAdditionalProperties(value.AdditionalProperties)
                }),
            ImageGenerationToolResultContent value => CreateContent(
                "image_generation_tool_result",
                new PersistedImageGenerationToolResultContent
                {
                    CallId = value.CallId,
                    Outputs = value.Outputs?.Select(EncodeContent).ToList(),
                    AdditionalProperties = SerializeAdditionalProperties(value.AdditionalProperties)
                }),
            ErrorContent value => CreateContent("error", new PersistedErrorContent
            {
                Message = value.Message,
                ErrorCode = value.ErrorCode,
                Details = value.Details,
                AdditionalProperties = SerializeAdditionalProperties(value.AdditionalProperties)
            }),
            UriContent value => CreateContent("uri", new PersistedUriContent
            {
                Uri = value.Uri.ToString(),
                MediaType = value.MediaType,
                AdditionalProperties = SerializeAdditionalProperties(value.AdditionalProperties)
            }),
            UsageContent value => CreateContent("usage", new PersistedUsageContent
            {
                InputTokenCount = value.Details.InputTokenCount,
                OutputTokenCount = value.Details.OutputTokenCount,
                TotalTokenCount = value.Details.TotalTokenCount,
                CachedInputTokenCount = value.Details.CachedInputTokenCount,
                ReasoningTokenCount = value.Details.ReasoningTokenCount,
                InputAudioTokenCount = value.Details.InputAudioTokenCount,
                InputTextTokenCount = value.Details.InputTextTokenCount,
                OutputAudioTokenCount = value.Details.OutputAudioTokenCount,
                OutputTextTokenCount = value.Details.OutputTextTokenCount,
                AdditionalCounts = value.Details.AdditionalCounts is null
                    ? null
                    : new Dictionary<string, long>(value.Details.AdditionalCounts, StringComparer.Ordinal),
                AdditionalProperties = SerializeAdditionalProperties(value.AdditionalProperties)
            }),
            _ => throw new NotSupportedException(
                $"AI content type '{content.GetType().FullName}' is not supported by model history schema v{CurrentSchemaVersion}.")
        };
    }

    private static ModelHistoryContent EncodeFunctionCall(FunctionCallContent value)
    {
        var functionNamespace = ReadConsistentMetadataValue(
            value.AdditionalProperties,
            FunctionNamespaceMetadataKey,
            FunctionNamespaceAliasKey);
        var providerFlatName = ReadConsistentMetadataValue(
            value.AdditionalProperties,
            ProviderFlatNameMetadataKey);
        return CreateContent("function_call", new PersistedFunctionCallContent
        {
            CallId = value.CallId,
            Name = value.Name,
            Arguments = SerializeJsonValue(value.Arguments),
            InformationalOnly = value.InformationalOnly,
            Namespace = functionNamespace,
            ProviderFlatName = providerFlatName,
            AdditionalProperties = SerializeAdditionalProperties(value.AdditionalProperties)
        });
    }

    private static AIContent DecodeContent(ModelHistoryContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrWhiteSpace(content.Kind))
            throw new JsonException("Model history content is missing its kind.");
        if (content.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new JsonException($"Model history content '{content.Kind}' is missing its payload.");
        if (content.Payload.ValueKind != JsonValueKind.Object)
            throw new JsonException($"Model history content '{content.Kind}' payload must be an object.");

        return content.Kind switch
        {
            "text" => DecodeText(content),
            "reasoning" => DecodeReasoning(content),
            "data" => DecodeData(content),
            "function_call" => DecodeFunctionCall(content),
            "function_result" => DecodeFunctionResult(content),
            "hosted_image_generation" => DecodeHostedImageGeneration(content),
            "image_generation_tool_call" => DecodeImageGenerationToolCall(content),
            "image_generation_tool_result" => DecodeImageGenerationToolResult(content),
            "error" => DecodeError(content),
            "uri" => DecodeUri(content),
            "usage" => DecodeUsage(content),
            _ => throw new NotSupportedException($"Unsupported model history content kind '{content.Kind}'.")
        };
    }

    private static AIContent DecodeText(ModelHistoryContent content)
    {
        var payload = DeserializePayload<PersistedTextContent>(content);
        return ApplyAdditionalProperties(new TextContent(payload.Text), payload.AdditionalProperties);
    }

    private static AIContent DecodeReasoning(ModelHistoryContent content)
    {
        var payload = DeserializePayload<PersistedReasoningContent>(content);
        return ApplyAdditionalProperties(
            new TextReasoningContent(payload.Text) { ProtectedData = payload.ProtectedData },
            payload.AdditionalProperties);
    }

    private static AIContent DecodeData(ModelHistoryContent content)
    {
        var payload = DeserializePayload<PersistedDataContent>(content);
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(payload.Base64Data);
        }
        catch (FormatException ex)
        {
            throw new JsonException("Model history data content has invalid base64 data.", ex);
        }

        return ApplyAdditionalProperties(
            new DataContent(bytes, payload.MediaType) { Name = payload.Name },
            payload.AdditionalProperties);
    }

    private static AIContent DecodeFunctionCall(ModelHistoryContent content)
    {
        var payload = DeserializePayload<PersistedFunctionCallContent>(content);
        var properties = DeserializeAdditionalProperties(payload.AdditionalProperties);
        ValidateAndRestoreToolIdentity(
            properties,
            payload.Namespace,
            payload.ProviderFlatName,
            out properties);
        var arguments = DeserializeDictionary(payload.Arguments);
        return new FunctionCallContent(payload.CallId, payload.Name, arguments)
        {
            InformationalOnly = payload.InformationalOnly,
            AdditionalProperties = properties
        };
    }

    private static AIContent DecodeFunctionResult(ModelHistoryContent content)
    {
        var payload = DeserializePayload<PersistedFunctionResultContent>(content);
        return ApplyAdditionalProperties(
            new FunctionResultContent(payload.CallId, DecodeFunctionResult(payload.Result)),
            payload.AdditionalProperties);
    }

    private static AIContent DecodeHostedImageGeneration(ModelHistoryContent content)
    {
        var payload = DeserializePayload<PersistedHostedImageGenerationContent>(content);
        byte[]? imageBytes = null;
        if (payload.ImageBase64 is not null)
        {
            try
            {
                imageBytes = Convert.FromBase64String(payload.ImageBase64);
            }
            catch (FormatException ex)
            {
                throw new JsonException("Model history hosted image content has invalid base64 data.", ex);
            }
        }

        return ApplyAdditionalProperties(
            new HostedImageGenerationContent
            {
                Id = payload.Id,
                Status = payload.Status,
                RevisedPrompt = payload.RevisedPrompt,
                ImageBytes = imageBytes,
                MediaType = payload.MediaType,
                ErrorMessage = payload.ErrorMessage
            },
            payload.AdditionalProperties);
    }

    private static AIContent DecodeImageGenerationToolCall(ModelHistoryContent content)
    {
        var payload = DeserializePayload<PersistedImageGenerationToolCallContent>(content);
        return ApplyAdditionalProperties(
            new ImageGenerationToolCallContent(payload.CallId),
            payload.AdditionalProperties);
    }

    private static AIContent DecodeImageGenerationToolResult(ModelHistoryContent content)
    {
        var payload = DeserializePayload<PersistedImageGenerationToolResultContent>(content);
        return ApplyAdditionalProperties(
            new ImageGenerationToolResultContent(payload.CallId)
            {
                Outputs = payload.Outputs?.Select(DecodeContent).ToList()
            },
            payload.AdditionalProperties);
    }

    private static AIContent DecodeError(ModelHistoryContent content)
    {
        var payload = DeserializePayload<PersistedErrorContent>(content);
        return ApplyAdditionalProperties(
            new ErrorContent(payload.Message)
            {
                ErrorCode = payload.ErrorCode,
                Details = payload.Details
            },
            payload.AdditionalProperties);
    }

    private static AIContent DecodeUri(ModelHistoryContent content)
    {
        var payload = DeserializePayload<PersistedUriContent>(content);
        return ApplyAdditionalProperties(
            new UriContent(payload.Uri, payload.MediaType),
            payload.AdditionalProperties);
    }

    private static AIContent DecodeUsage(ModelHistoryContent content)
    {
        var payload = DeserializePayload<PersistedUsageContent>(content);
        return ApplyAdditionalProperties(
            new UsageContent(new UsageDetails
            {
                InputTokenCount = payload.InputTokenCount,
                OutputTokenCount = payload.OutputTokenCount,
                TotalTokenCount = payload.TotalTokenCount,
                CachedInputTokenCount = payload.CachedInputTokenCount,
                ReasoningTokenCount = payload.ReasoningTokenCount,
                InputAudioTokenCount = payload.InputAudioTokenCount,
                InputTextTokenCount = payload.InputTextTokenCount,
                OutputAudioTokenCount = payload.OutputAudioTokenCount,
                OutputTextTokenCount = payload.OutputTextTokenCount,
                AdditionalCounts = DecodeAdditionalCounts(payload.AdditionalCounts)
            }),
            payload.AdditionalProperties);
    }

    private static ModelHistoryContent CreateContent<TPayload>(string kind, TPayload payload) where TPayload : notnull =>
        new()
        {
            Kind = kind,
            Payload = JsonSerializer.SerializeToElement(payload, PayloadJsonOptions)
        };

    private static TPayload DeserializePayload<TPayload>(ModelHistoryContent content) where TPayload : class =>
        content.Payload.Deserialize<TPayload>(PayloadJsonOptions)
        ?? throw new JsonException($"Model history content '{content.Kind}' deserialized to null.");

    private static PersistedFunctionResult EncodeFunctionResult(object? result)
    {
        if (result is IEnumerable<AIContent> contents)
        {
            return new PersistedFunctionResult
            {
                SchemaVersion = CurrentResultSchemaVersion,
                Kind = "contents",
                Json = null,
                Contents = contents.Select(EncodeContent).ToList()
            };
        }

        return new PersistedFunctionResult
        {
            SchemaVersion = CurrentResultSchemaVersion,
            Kind = "json",
            Json = SerializeJsonValue(result),
            Contents = null
        };
    }

    private static object? DecodeFunctionResult(PersistedFunctionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.SchemaVersion != CurrentResultSchemaVersion)
            throw new NotSupportedException($"Unsupported function result schema version '{result.SchemaVersion}'.");

        return result.Kind switch
        {
            "json" when result.Json is { } json => DeserializeJsonValue(json),
            "json" => throw new JsonException("Model history JSON result is missing its value."),
            "contents" when result.Contents is not null && !result.Contents.Any(static content => content is null)
                => result.Contents.Select(DecodeContent).ToList(),
            "contents" when result.Contents is not null
                => throw new JsonException("Model history content result contains a null content entry."),
            "contents" => throw new JsonException("Model history content result is missing contents."),
            _ => throw new NotSupportedException($"Unsupported function result kind '{result.Kind}'.")
        };
    }

    private static JsonElement SerializeJsonValue(object? value) => value switch
    {
        JsonElement element => element.Clone(),
        null => JsonSerializer.SerializeToElement<object?>(null, JsonOptions),
        _ => JsonSerializer.SerializeToElement(value, value.GetType(), JsonOptions)
    };

    private static object? DeserializeJsonValue(JsonElement value) => NormalizeJsonValue(value);

    private static AdditionalPropertiesDictionary<long>? DecodeAdditionalCounts(
        Dictionary<string, long>? counts)
    {
        if (counts is null)
            return null;

        var result = new AdditionalPropertiesDictionary<long>();
        foreach (var (key, value) in counts)
            result[key] = value;
        return result;
    }

    private static IDictionary<string, object?>? DeserializeDictionary(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.Object)
            throw new JsonException("Model history function arguments must be a JSON object or null.");

        var dictionary = value.Deserialize<Dictionary<string, object?>>(JsonOptions)
            ?? throw new JsonException("Model history function arguments deserialized to null.");
        return dictionary.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value is JsonElement element ? NormalizeJsonValue(element) : pair.Value,
            StringComparer.Ordinal);
    }

    private static TContent ApplyAdditionalProperties<TContent>(TContent content, JsonElement? properties)
        where TContent : AIContent
    {
        content.AdditionalProperties = DeserializeAdditionalProperties(properties);
        return content;
    }

    private static string? ReadConsistentMetadataValue(
        AdditionalPropertiesDictionary? properties,
        params string[] keys)
    {
        string? result = null;
        foreach (var key in keys)
        {
            if (properties is null || !properties.TryGetValue(key, out var value))
                continue;
            if (value is not string text || string.IsNullOrWhiteSpace(text))
                throw new JsonException($"Tool identity metadata '{key}' must be a non-empty string.");
            if (result is not null && !string.Equals(result, text, StringComparison.Ordinal))
                throw new JsonException("Tool identity metadata fields conflict.");
            result = text;
        }

        return result;
    }

    private static void ValidateAndRestoreToolIdentity(
        AdditionalPropertiesDictionary? properties,
        string? functionNamespace,
        string? providerFlatName,
        out AdditionalPropertiesDictionary? restored)
    {
        ValidateMetadataValue(properties, providerFlatName, ProviderFlatNameMetadataKey);
        ValidateMetadataValue(
            properties,
            functionNamespace,
            FunctionNamespaceMetadataKey,
            FunctionNamespaceAliasKey);

        restored = properties;
        if (providerFlatName is not null && !ContainsAny(properties, ProviderFlatNameMetadataKey))
        {
            restored ??= new AdditionalPropertiesDictionary();
            restored[ProviderFlatNameMetadataKey] = providerFlatName;
        }
        if (functionNamespace is not null &&
            !ContainsAny(properties, FunctionNamespaceMetadataKey, FunctionNamespaceAliasKey))
        {
            restored ??= new AdditionalPropertiesDictionary();
            restored[FunctionNamespaceMetadataKey] = functionNamespace;
            restored[FunctionNamespaceAliasKey] = functionNamespace;
        }
    }

    private static void ValidateMetadataValue(
        AdditionalPropertiesDictionary? properties,
        string? expected,
        params string[] keys)
    {
        var actual = ReadConsistentMetadataValue(properties, keys);
        if (actual is not null && !string.Equals(actual, expected, StringComparison.Ordinal))
            throw new JsonException("Tool identity strong fields conflict with additional properties.");
    }

    private static bool ContainsAny(AdditionalPropertiesDictionary? properties, params string[] keys) =>
        properties is not null && keys.Any(properties.ContainsKey);

    private static JsonElement? SerializeAdditionalProperties(AdditionalPropertiesDictionary? properties) =>
        properties == null
            ? null
            : JsonSerializer.SerializeToElement(properties, JsonOptions);

    private static AdditionalPropertiesDictionary? DeserializeAdditionalProperties(JsonElement? properties) =>
        properties is null
            ? null
            : NormalizeAdditionalProperties(properties.Value.Deserialize<AdditionalPropertiesDictionary>(JsonOptions));

    private static AdditionalPropertiesDictionary? NormalizeAdditionalProperties(
        AdditionalPropertiesDictionary? properties)
    {
        if (properties == null)
            return null;

        var normalized = new AdditionalPropertiesDictionary();
        foreach (var (key, value) in properties)
            normalized[key] = value is JsonElement element ? NormalizeJsonValue(element) : value;
        return normalized;
    }

    private static object? NormalizeJsonValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when element.TryGetDecimal(out var number) => number,
        JsonValueKind.Number => element.GetDouble(),
        _ => element.Clone()
    };
}
#pragma warning restore MEAI001

internal abstract class PersistedModelContentPayload
{
    public required JsonElement? AdditionalProperties { get; init; }
}

internal sealed class PersistedTextContent : PersistedModelContentPayload
{
    public required string Text { get; init; }
}

internal sealed class PersistedReasoningContent : PersistedModelContentPayload
{
    public required string Text { get; init; }

    public required string? ProtectedData { get; init; }
}

internal sealed class PersistedDataContent : PersistedModelContentPayload
{
    public required string Base64Data { get; init; }

    public required string MediaType { get; init; }

    public required string? Name { get; init; }
}

internal sealed class PersistedFunctionCallContent : PersistedModelContentPayload
{
    public required string CallId { get; init; }

    public required string Name { get; init; }

    public required JsonElement Arguments { get; init; }

    public required bool InformationalOnly { get; init; }

    public required string? Namespace { get; init; }

    public required string? ProviderFlatName { get; init; }
}

internal sealed class PersistedFunctionResultContent : PersistedModelContentPayload
{
    public required string CallId { get; init; }

    public required PersistedFunctionResult Result { get; init; }
}

internal sealed class PersistedFunctionResult
{
    public required int SchemaVersion { get; init; }

    public required string Kind { get; init; }

    public required JsonElement? Json { get; init; }

    public required List<ModelHistoryContent>? Contents { get; init; }
}

internal sealed class PersistedHostedImageGenerationContent : PersistedModelContentPayload
{
    public required string Id { get; init; }

    public required string Status { get; init; }

    public required string? RevisedPrompt { get; init; }

    public required string? ImageBase64 { get; init; }

    public required string MediaType { get; init; }

    public required string? ErrorMessage { get; init; }
}

internal sealed class PersistedImageGenerationToolCallContent : PersistedModelContentPayload
{
    public required string CallId { get; init; }
}

internal sealed class PersistedImageGenerationToolResultContent : PersistedModelContentPayload
{
    public required string CallId { get; init; }

    public required List<ModelHistoryContent>? Outputs { get; init; }
}

internal sealed class PersistedErrorContent : PersistedModelContentPayload
{
    public required string Message { get; init; }

    public required string? ErrorCode { get; init; }

    public required string? Details { get; init; }
}

internal sealed class PersistedUriContent : PersistedModelContentPayload
{
    public required string Uri { get; init; }

    public required string MediaType { get; init; }
}

internal sealed class PersistedUsageContent : PersistedModelContentPayload
{
    public required long? InputTokenCount { get; init; }

    public required long? OutputTokenCount { get; init; }

    public required long? TotalTokenCount { get; init; }

    public required long? CachedInputTokenCount { get; init; }

    public required long? ReasoningTokenCount { get; init; }

    public required long? InputAudioTokenCount { get; init; }

    public required long? InputTextTokenCount { get; init; }

    public required long? OutputAudioTokenCount { get; init; }

    public required long? OutputTextTokenCount { get; init; }

    public required Dictionary<string, long>? AdditionalCounts { get; init; }
}

internal sealed class ModelHistoryMessage
{
    public int SchemaVersion { get; init; } = ModelHistoryCodec.CurrentSchemaVersion;

    public string? TurnId { get; init; }

    public string Role { get; init; } = ChatRole.User.Value;

    public string? MessageId { get; init; }

    public string? AuthorName { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public JsonElement? AdditionalProperties { get; init; }

    public List<ModelHistoryContent> Contents { get; init; } = [];
}

internal sealed class ModelHistoryContent
{
    public string Kind { get; init; } = string.Empty;

    public JsonElement Payload { get; init; }
}

internal sealed record ModelHistoryReplayResult(
    IReadOnlyList<ChatMessage> Messages,
    bool HasModelHistoryRecords,
    IReadOnlyList<ModelHistoryReplayWarning>? Warnings = null,
    int RejectedRecords = 0,
    IReadOnlySet<string>? FallbackTurnIds = null,
    long BytesRead = 0,
    int RecordsDecoded = 0);

internal sealed record ModelHistoryReplayWarning(
    string Code,
    string Message,
    string? TurnId = null);
