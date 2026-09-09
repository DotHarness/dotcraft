using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    /// <summary>
    /// Whether the tool should be included in <see cref="BuiltInToolCatalog"/>.
    /// </summary>
    public bool CatalogVisible { get; set; }
}

/// <summary>
/// Assembly-level hook used by <see cref="ToolRegistry"/> and <see cref="BuiltInToolCatalog"/> to
/// consume source-generated tool metadata before falling back to reflection scanning.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class GeneratedToolCatalogAttribute(Type providerType) : Attribute
{
    public Type ProviderType { get; } = providerType;
}

/// <summary>
/// Describes metadata emitted by the DotCraft tool source generator.
/// </summary>
public sealed record GeneratedToolDescriptor(
    string Name,
    string Description,
    string Icon,
    Func<IDictionary<string, object?>?, string>? DisplayFormatter,
    int? MaxResultChars,
    bool StreamArgumentsEnabled,
    bool CatalogVisible,
    bool RpcEligible = false);

/// <summary>
/// Immutable compile-time declaration for a generated tool contract.
/// </summary>
public sealed class GeneratedToolDeclaration
{
    /// <summary>Creates a declaration from generator-owned schema JSON.</summary>
    public GeneratedToolDeclaration(
        string name,
        string description,
        string inputSchemaJson,
        Type? outputType,
        bool rpcEligible = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputSchemaJson);

        Name = name;
        Description = description;
        InputSchema = GeneratedToolSchema.Parse(inputSchemaJson);
        OutputSchema = GeneratedToolSchema.CreateReturnSchema(outputType, AIJsonUtilities.DefaultOptions);
        RpcEligible = rpcEligible;
    }

    /// <summary>Gets the model-visible tool name.</summary>
    public string Name { get; }

    /// <summary>Gets the model-visible tool description.</summary>
    public string Description { get; }

    /// <summary>Gets the immutable input JSON Schema.</summary>
    public JsonElement InputSchema { get; }

    /// <summary>Gets the immutable output JSON Schema when one is declared.</summary>
    public JsonElement? OutputSchema { get; }

    /// <summary>Gets whether the native declaration may be exported by a Remote Tool Host.</summary>
    public bool RpcEligible { get; }
}

internal interface IGeneratedToolMetadata
{
    bool StreamArgumentsEnabled { get; }

    int? MaxResultChars { get; }

    string? Icon { get; }

    Func<IDictionary<string, object?>?, string>? DisplayFormatter { get; }

    bool RpcEligible { get; }
}

/// <summary>
/// Base class used by source-generated DotCraft tool wrappers.
/// </summary>
public abstract class GeneratedAIFunction : AIFunction, IGeneratedToolMetadata
{
    private static readonly JsonSerializerOptions ToolInputSerializerOptions = CreateToolInputSerializerOptions();
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

    /// <summary>Creates a generated function backed by a reusable declaration.</summary>
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

    public override JsonSerializerOptions JsonSerializerOptions => ToolInputSerializerOptions;

    /// <summary>Gets the serializer options used only for generated tool results.</summary>
    protected JsonSerializerOptions OutputJsonSerializerOptions => AIJsonUtilities.DefaultOptions;

    public bool StreamArgumentsEnabled => _metadata.StreamArgumentsEnabled;

    public int? MaxResultChars => _metadata.MaxResultChars;

    public string? Icon => string.IsNullOrEmpty(_metadata.Icon) ? null : _metadata.Icon;

    public Func<IDictionary<string, object?>?, string>? DisplayFormatter => _metadata.DisplayFormatter;

    public bool RpcEligible => _metadata.RpcEligible;

    private static JsonSerializerOptions CreateToolInputSerializerOptions()
    {
        var options = new JsonSerializerOptions(AIJsonUtilities.DefaultOptions);
        options.Converters.Insert(0, new StrictToolInputEnumConverterFactory());
        return options;
    }
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
    private static readonly ConcurrentDictionary<Type, JsonElement?> ReturnSchemas = new();

    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static JsonElement? CreateReturnSchema(Type? returnType, JsonSerializerOptions serializerOptions)
    {
        var unwrapped = UnwrapReturnType(returnType);
        if (unwrapped == null)
            return null;

        return ReturnSchemas.GetOrAdd(
            unwrapped,
            static (type, options) => AIJsonUtilities.CreateJsonSchema(
                type,
                serializerOptions: options),
            serializerOptions);
    }

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

/// <summary>
/// Binds generated tool arguments to their declared CLR types.
/// </summary>
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

    private static bool IsAIContentResultType(Type type)
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
