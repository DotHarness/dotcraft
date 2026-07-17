using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.Core;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

internal sealed class AnthropicEagerToolInputStreamingChatClient(IChatClient innerClient)
    : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(messages, PrepareOptions(options), cancellationToken);

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(
            messages,
            PrepareOptions(options),
            cancellationToken))
        {
            yield return update;
        }
    }

    internal static ChatOptions? PrepareOptions(ChatOptions? options)
    {
        if (options?.Tools is not { Count: > 0 } tools)
            return options;

        List<AITool>? preparedTools = null;
        for (var index = 0; index < tools.Count; index++)
        {
            var original = tools[index];
            var prepared = PrepareTool(original);
            if (ReferenceEquals(original, prepared))
                continue;

            preparedTools ??= tools.ToList();
            preparedTools[index] = prepared;
        }

        if (preparedTools == null)
            return options;

        var preparedOptions = options.Clone();
        preparedOptions.Tools = preparedTools;
        return preparedOptions;
    }

    private static AITool PrepareTool(AITool tool)
    {
        if (tool is AIFunctionDeclaration function)
            return AnthropicToolDefinitionMapper.CreateEagerTool(function);

        if (tool.GetService(typeof(BetaToolUnion)) is not BetaToolUnion { Value: BetaTool betaTool })
            return tool;

        if (betaTool.EagerInputStreaming is not null)
            return tool;

        var eagerTool = new BetaTool(betaTool)
        {
            EagerInputStreaming = true
        };
        return new BetaToolUnion(eagerTool).AsAITool();
    }
}

internal static class AnthropicToolDefinitionMapper
{
    private static readonly JsonSerializerOptions RelaxedJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly HashSet<string> SupportedStringFormats = new(StringComparer.Ordinal)
    {
        "date-time",
        "time",
        "date",
        "duration",
        "email",
        "hostname",
        "uri",
        "ipv4",
        "ipv6",
        "uuid"
    };

    private static readonly HashSet<string> SupportedBaseSchemaProperties = new(StringComparer.Ordinal)
    {
        "type",
        "description",
        "title",
        "$ref",
        "$defs",
        "anyOf",
        "allOf",
        "enum",
        "const"
    };

    private static readonly HashSet<string> SupportedObjectSchemaProperties = new(
        SupportedBaseSchemaProperties,
        StringComparer.Ordinal)
    {
        "properties",
        "required",
        "additionalProperties"
    };

    private static readonly HashSet<string> SupportedStringSchemaProperties = new(
        SupportedBaseSchemaProperties,
        StringComparer.Ordinal)
    {
        "format"
    };

    private static readonly HashSet<string> SupportedArraySchemaProperties = new(
        SupportedBaseSchemaProperties,
        StringComparer.Ordinal)
    {
        "items",
        "minItems"
    };

    // Keep this transform in parity with AnthropicClientExtensions.JsonSchemaTransformCache
    // in the pinned Anthropic SDK. The SDK cache is internal, so the request adapter must
    // reproduce it before wrapping function declarations as provider-native tools.
    private static readonly AIJsonSchemaTransformCache JsonSchemaTransformCache = new(
        new AIJsonSchemaTransformOptions
        {
            DisallowAdditionalProperties = true,
            TransformSchemaNode = static (_, schemaNode) => TransformSchemaNode(schemaNode)
        });

    internal static AITool CreateEagerTool(AIFunctionDeclaration function)
    {
        var inputSchema = JsonSchemaTransformCache.GetOrCreateTransformedSchema(function);
        Dictionary<string, JsonElement> schemaData = [];
        if (inputSchema.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in inputSchema.EnumerateObject())
                schemaData[property.Name] = property.Value;
        }

        var betaTool = new BetaTool
        {
            Name = function.Name,
            Description = function.Description,
            InputSchema = new InputSchema(schemaData),
            DeferLoading = GetValue<bool?>(function, nameof(BetaTool.DeferLoading)),
            Strict = GetValue<bool?>(function, nameof(BetaTool.Strict)),
            InputExamples = GetValue<List<Dictionary<string, JsonElement>>>(
                function,
                nameof(BetaTool.InputExamples)),
            AllowedCallers = GetValue<List<ApiEnum<string, BetaToolAllowedCaller>>>(
                function,
                nameof(BetaTool.AllowedCallers)),
            EagerInputStreaming = true
        };
        return new BetaToolUnion(betaTool).AsAITool();
    }

    private static T? GetValue<T>(AIFunctionDeclaration function, string name) =>
        function.AdditionalProperties?.TryGetValue(name, out var value) is true && value is T typedValue
            ? typedValue
            : default;

    private static JsonNode TransformSchemaNode(JsonNode schemaNode)
    {
        if (schemaNode is not JsonObject schemaObject)
            return schemaNode;

        if (schemaObject.TryGetPropertyValue("oneOf", out var oneOfNode) && oneOfNode is not null)
        {
            schemaObject.Remove("oneOf");
            schemaObject["anyOf"] = oneOfNode;
        }

        var type = schemaObject.TryGetPropertyValue("type", out var typeNode) && typeNode is JsonValue
            ? typeNode.GetValue<string>()
            : null;
        List<KeyValuePair<string, string>>? removed = null;

        if (type == "string"
            && schemaObject.TryGetPropertyValue("format", out var formatNode)
            && formatNode?.GetValue<string>() is { } format
            && !SupportedStringFormats.Contains(format))
        {
            var serialized = formatNode.ToJsonString(RelaxedJsonOptions);
            schemaObject.Remove("format");
            (removed ??= []).Add(new("format", serialized));
        }

        if (type == "array"
            && schemaObject.TryGetPropertyValue("minItems", out var minItemsNode)
            && minItemsNode is JsonValue minItemsJsonValue
            && minItemsJsonValue.TryGetValue(out int minItems)
            && minItems is not (0 or 1))
        {
            var serialized = minItemsNode.ToJsonString(RelaxedJsonOptions);
            schemaObject.Remove("minItems");
            (removed ??= []).Add(new("minItems", serialized));
        }

        var supported = type switch
        {
            "object" => SupportedObjectSchemaProperties,
            "string" => SupportedStringSchemaProperties,
            "array" => SupportedArraySchemaProperties,
            _ => SupportedBaseSchemaProperties
        };

        foreach (var property in schemaObject.ToArray())
        {
            if (supported.Contains(property.Key))
                continue;

            var serialized = property.Value?.ToJsonString(RelaxedJsonOptions) ?? "null";
            schemaObject.Remove(property.Key);
            (removed ??= []).Add(new(property.Key, serialized));
        }

        if (removed is { Count: > 0 })
        {
            var existing = schemaObject.TryGetPropertyValue("description", out var descriptionNode)
                ? descriptionNode?.GetValue<string>()
                : null;
            var constraintInfo = "{" + string.Join(", ", removed.Select(item => $"{item.Key}: {item.Value}")) + "}";
            schemaObject["description"] = existing is not null
                ? $"{existing}\n\n{constraintInfo}"
                : constraintInfo;
        }

        return schemaNode;
    }
}
