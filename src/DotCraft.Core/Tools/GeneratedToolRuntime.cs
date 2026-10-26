using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Marks a production tool method that should receive a source-generated wrapper without adding it
/// to the built-in tool catalog.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class GeneratedToolAttribute : Attribute
{
    /// <summary>
    /// Overrides the model-visible tool name. The method name is used when omitted.
    /// </summary>
    public string? Name { get; set; }

    public bool CatalogVisible { get; set; }
}

/// <summary>Points <see cref="BuiltInToolCatalog"/> at a type whose static <c>Descriptors</c> lists catalog entries.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class GeneratedToolCatalogAttribute(Type providerType) : Attribute
{
    public Type ProviderType { get; } = providerType;
}

public sealed record GeneratedToolDescriptor(
    string Name,
    string Description,
    int? MaxResultChars,
    bool StreamArgumentsEnabled,
    bool CatalogVisible,
    bool RpcEligible = false);

public sealed class GeneratedToolDeclaration
{
    public GeneratedToolDeclaration(
        string name,
        string description,
        string inputSchemaJson,
        Type? outputType,
        bool rpcEligible = false,
        string? presentationId = null,
        string? presentationOptionsJson = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputSchemaJson);

        Name = name;
        Description = description;
        InputSchema = GeneratedToolSchema.Parse(inputSchemaJson);
        OutputSchema = GeneratedToolSchema.CreateReturnSchema(outputType, OutputSerializerOptions);
        ReturnsContent = GeneratedToolSchema.ReturnsContent(outputType);
        RpcEligible = rpcEligible;
        Presentation = presentationId is null
            ? null
            : new ToolPresentationDescriptor(
                new PresentationId(presentationId),
                presentationOptionsJson is null
                    ? null
                    : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(presentationOptionsJson));
    }

    public string Name { get; }

    public string Description { get; }

    public JsonElement InputSchema { get; }

    public JsonElement? OutputSchema { get; }

    /// <summary>Gets whether the tool returns content items, such as text alongside images, rather than a value.</summary>
    public bool ReturnsContent { get; }

    /// <summary>Gets whether the native declaration may be exported by a Remote Tool Host.</summary>
    public bool RpcEligible { get; }

    public ToolPresentationDescriptor? Presentation { get; }

    internal JsonSerializerOptions InputSerializerOptions { get; } = CreateSerializerOptions(strictEnums: true);

    internal JsonSerializerOptions OutputSerializerOptions { get; } = CreateSerializerOptions(strictEnums: false);

    private static JsonSerializerOptions CreateSerializerOptions(bool strictEnums)
    {
        var options = new JsonSerializerOptions(AIJsonUtilities.DefaultOptions);
        options.TypeInfoResolver = new DeclarationResolver(
            options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver());
        if (strictEnums)
            options.Converters.Insert(0, new StrictToolInputEnumConverterFactory());
        options.MakeReadOnly();
        return options;
    }

    // Distinct resolver identities prevent System.Text.Json from sharing plugin type caches across declarations.
    private sealed class DeclarationResolver(IJsonTypeInfoResolver inner) : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) => inner.GetTypeInfo(type, options);
    }
}

internal interface IGeneratedToolMetadata
{
    bool StreamArgumentsEnabled { get; }

    int? MaxResultChars { get; }

    bool RpcEligible { get; }

    bool ReturnsContent { get; }

    ToolPresentationDescriptor? Presentation { get; }
}

public abstract class GeneratedAIFunction : AIFunction, IGeneratedToolMetadata
{
    private readonly GeneratedToolDeclaration _declaration;
    private readonly GeneratedToolDescriptor _metadata;

    protected GeneratedAIFunction(
        string name,
        string description,
        string jsonSchema,
        Type? returnType,
        GeneratedToolDescriptor metadata)
        : this(new GeneratedToolDeclaration(name, description, jsonSchema, returnType, metadata.RpcEligible), metadata)
    {
    }

    protected GeneratedAIFunction(
        GeneratedToolDeclaration declaration,
        GeneratedToolDescriptor metadata)
    {
        _declaration = declaration ?? throw new ArgumentNullException(nameof(declaration));
        _metadata = metadata;
    }

    public override string Name => _declaration.Name;

    public override string Description => _declaration.Description;

    public override JsonElement JsonSchema => _declaration.InputSchema;

    public override JsonElement? ReturnJsonSchema => _declaration.OutputSchema;

    public override JsonSerializerOptions JsonSerializerOptions => _declaration.InputSerializerOptions;

    /// <summary>Gets the serializer options used only for generated tool results.</summary>
    protected JsonSerializerOptions OutputJsonSerializerOptions => _declaration.OutputSerializerOptions;

    /// <summary>Reads live host context supplied by the runtime, never model arguments or planning state.</summary>
    protected static ToolInvocationContext GetInvocationContext(AIFunctionArguments arguments) =>
        arguments.Context?.TryGetValue(typeof(ToolInvocationContext), out var value) == true
        && value is ToolInvocationContext context
            ? context
            : throw new InvalidOperationException("This tool requires a live ToolInvocationContext supplied by its runtime.");

    public bool StreamArgumentsEnabled => _metadata.StreamArgumentsEnabled;

    public int? MaxResultChars => _metadata.MaxResultChars;

    public bool RpcEligible => _metadata.RpcEligible;

    public bool ReturnsContent => _declaration.ReturnsContent;

    public ToolPresentationDescriptor? Presentation => _declaration.Presentation;
}

internal sealed class StrictToolInputEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(StrictToolInputEnumConverter<>).MakeGenericType(typeToConvert))!;

    private sealed class StrictToolInputEnumConverter<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        private static readonly IReadOnlyDictionary<string, TEnum> Values = Enum.GetValues<TEnum>()
            .ToDictionary(WireName, static value => value, StringComparer.Ordinal);

        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException($"Expected a string value for {typeof(TEnum).Name}.");

            var wireValue = reader.GetString();
            return wireValue is not null && Values.TryGetValue(wireValue, out var value)
                ? value
                : throw new JsonException($"Invalid value '{wireValue}' for {typeof(TEnum).Name}.");
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
            writer.WriteStringValue(WireName(value));

        private static string WireName(TEnum value)
        {
            var field = typeof(TEnum).GetField(Enum.GetName(value)!, BindingFlags.Public | BindingFlags.Static)!;
            return field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name
                ?? JsonNamingPolicy.CamelCase.ConvertName(field.Name);
        }
    }
}

internal static class GeneratedToolMetadataResolver
{
    public static bool TryGet(AITool tool, out IGeneratedToolMetadata metadata)
    {
        if (tool is IGeneratedToolMetadata direct)
        {
            metadata = direct;
            return true;
        }

        metadata = null!;
        return false;
    }
}

internal static class GeneratedToolSchema
{
    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static JsonElement? CreateReturnSchema(Type? returnType, JsonSerializerOptions serializerOptions)
    {
        var unwrapped = UnwrapReturnType(returnType);
        if (unwrapped == null
            || unwrapped == typeof(ToolExecutionResult)
            || GeneratedToolArgumentBinder.IsAIContentResultType(unwrapped))
            return null;

        return AIJsonUtilities.CreateJsonSchema(unwrapped, serializerOptions: serializerOptions);
    }

    public static bool ReturnsContent(Type? returnType) =>
        UnwrapReturnType(returnType) is { } unwrapped && GeneratedToolArgumentBinder.IsAIContentResultType(unwrapped);

    private static Type? UnwrapReturnType(Type? returnType)
    {
        if (returnType == null || returnType == typeof(void) || returnType == typeof(Task) || returnType == typeof(ValueTask))
            return null;

        if (returnType.IsGenericType)
        {
            var genericDefinition = returnType.GetGenericTypeDefinition();
            if (genericDefinition == typeof(Task<>) || genericDefinition == typeof(ValueTask<>))
                return returnType.GetGenericArguments()[0];
        }

        return returnType;
    }
}

public static class GeneratedToolArgumentBinder
{
    public static T GetRequired<T>(
        AIFunctionArguments arguments,
        string name,
        JsonSerializerOptions serializerOptions)
    {
        if (!arguments.TryGetValue(name, out var value))
            throw new KeyNotFoundException($"Required argument '{name}' was not supplied.");

        return ConvertValue<T>(name, value, hasDefaultValue: false, defaultValue: default!, serializerOptions);
    }

    public static T GetOptional<T>(
        AIFunctionArguments arguments,
        string name,
        T defaultValue,
        JsonSerializerOptions serializerOptions)
    {
        if (!arguments.TryGetValue(name, out var value))
            return defaultValue;

        return ConvertValue(name, value, hasDefaultValue: true, defaultValue, serializerOptions);
    }

    public static object? MarshalResult(object? result, Type declaredType, JsonSerializerOptions serializerOptions)
    {
        if (declaredType == typeof(ToolExecutionResult))
        {
            return result as ToolExecutionResult ?? ToolExecutionResult.Failed(
                new ToolError(ToolErrorCodes.ResultInvalid, "The tool returned no execution result."));
        }

        if (result == null)
            return null;

        if (IsAIContentResultType(declaredType))
            return result;

        return JsonSerializer.SerializeToElement(result, declaredType, serializerOptions);
    }

    private static T ConvertValue<T>(
        string name,
        object? value,
        bool hasDefaultValue,
        T defaultValue,
        JsonSerializerOptions serializerOptions)
    {
        if (value == null)
        {
            if (CanAssignNull(typeof(T)))
                return default!;
            if (hasDefaultValue)
                return defaultValue;
            throw new JsonException($"Argument '{name}' cannot be null.");
        }

        if (value is T typed)
            return typed;

        if (value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Null)
            {
                if (CanAssignNull(typeof(T)))
                    return default!;
                if (hasDefaultValue)
                    return defaultValue;
            }

            return element.Deserialize<T>(serializerOptions)
                ?? (CanAssignNull(typeof(T)) ? default! : throw new JsonException($"Argument '{name}' could not be converted."));
        }

        var serialized = JsonSerializer.SerializeToElement(value, value.GetType(), serializerOptions);
        return serialized.Deserialize<T>(serializerOptions)
            ?? (CanAssignNull(typeof(T)) ? default! : throw new JsonException($"Argument '{name}' could not be converted."));
    }

    private static bool CanAssignNull(Type type) =>
        !type.IsValueType || Nullable.GetUnderlyingType(type) != null;

    internal static bool IsAIContentResultType(Type type)
    {
        if (typeof(AIContent).IsAssignableFrom(type))
            return true;

        if (type == typeof(string))
            return false;

        if (type.IsArray)
            return typeof(AIContent).IsAssignableFrom(type.GetElementType());

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                if (typeof(AIContent).IsAssignableFrom(argument)
                    && typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
