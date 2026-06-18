using System.Collections.Concurrent;
using System.Text.Json;
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

internal sealed record GeneratedToolDescriptor(
    string Name,
    string Description,
    string Icon,
    Func<IDictionary<string, object?>?, string>? DisplayFormatter,
    int? MaxResultChars,
    bool StreamArgumentsEnabled,
    bool CatalogVisible);

internal interface IGeneratedToolMetadata
{
    bool StreamArgumentsEnabled { get; }

    int? MaxResultChars { get; }

    string? Icon { get; }

    Func<IDictionary<string, object?>?, string>? DisplayFormatter { get; }
}

internal abstract class GeneratedAIFunction : AIFunction, IGeneratedToolMetadata
{
    private readonly string _name;
    private readonly string _description;
    private readonly JsonElement _jsonSchema;
    private readonly JsonElement? _returnJsonSchema;
    private readonly GeneratedToolDescriptor _metadata;

    protected GeneratedAIFunction(
        string name,
        string description,
        string jsonSchema,
        Type? returnType,
        GeneratedToolDescriptor metadata)
    {
        _name = name;
        _description = description;
        _jsonSchema = GeneratedToolSchema.Parse(jsonSchema);
        _returnJsonSchema = GeneratedToolSchema.CreateReturnSchema(returnType, JsonSerializerOptions);
        _metadata = metadata;
    }

    public override string Name => _name;

    public override string Description => _description;

    public override JsonElement JsonSchema => _jsonSchema;

    public override JsonElement? ReturnJsonSchema => _returnJsonSchema;

    public override JsonSerializerOptions JsonSerializerOptions => AIJsonUtilities.DefaultOptions;

    public bool StreamArgumentsEnabled => _metadata.StreamArgumentsEnabled;

    public int? MaxResultChars => _metadata.MaxResultChars;

    public string? Icon => string.IsNullOrEmpty(_metadata.Icon) ? null : _metadata.Icon;

    public Func<IDictionary<string, object?>?, string>? DisplayFormatter => _metadata.DisplayFormatter;
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

internal static class GeneratedToolArgumentBinder
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
