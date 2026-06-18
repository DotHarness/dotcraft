using System.ComponentModel;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Plugins;
using DotCraft.Protocol.AppServer;

namespace DotCraft.AppBinding;

/// <summary>
/// Marks a managed C# method as an app-bound dynamic tool.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class DynamicToolAttribute(string name) : Attribute
{
    /// <summary>
    /// Model-visible dynamic tool name.
    /// </summary>
    public string Name { get; } = string.IsNullOrWhiteSpace(name)
        ? throw new ArgumentException("Dynamic tool name is required.", nameof(name))
        : name;

    /// <summary>
    /// Stable ordering value used when exposing tool specs.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Whether the generated dynamic tool should be exposed through deferred loading.
    /// </summary>
    public bool DeferLoading { get; set; }
}

/// <summary>
/// Builds <see cref="DynamicToolSpec"/> instances from attributed managed methods.
/// </summary>
public static class DynamicToolSpecFactory
{
    /// <summary>
    /// Creates a dynamic tool spec for one attributed managed method.
    /// </summary>
    public static DynamicToolSpec Create(MethodInfo method, string? toolNamespace = null)
    {
        ArgumentNullException.ThrowIfNull(method);

        var attribute = method.GetCustomAttribute<DynamicToolAttribute>()
            ?? throw new InvalidOperationException($"Method '{method.DeclaringType?.FullName}.{method.Name}' is missing DynamicToolAttribute.");
        var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description;
        if (string.IsNullOrWhiteSpace(description))
            throw new InvalidOperationException($"Dynamic tool '{attribute.Name}' must declare DescriptionAttribute.");

        var schema = BuildInputSchema(method, attribute.Name);
        if (!PluginFunctionSchemaValidator.TryValidateSchema(schema, out var schemaMessage))
            throw new InvalidOperationException($"Dynamic tool '{attribute.Name}' generated an invalid input schema: {schemaMessage}");

        return new DynamicToolSpec
        {
            Namespace = toolNamespace,
            Name = attribute.Name,
            Description = description,
            InputSchema = schema,
            DeferLoading = attribute.DeferLoading
        };
    }

    /// <summary>
    /// Creates ordered dynamic tool specs for attributed managed methods on the target type.
    /// </summary>
    public static IReadOnlyList<DynamicToolSpec> CreateFor<TTarget>(string? toolNamespace = null)
        where TTarget : class =>
        DiscoverMethods(typeof(TTarget))
            .Select(method => Create(method, toolNamespace))
            .ToList();

    internal static IReadOnlyList<MethodInfo> DiscoverMethods(Type targetType)
    {
        var methods = targetType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(method => new
            {
                Method = method,
                Attribute = method.GetCustomAttribute<DynamicToolAttribute>()
            })
            .Where(entry => entry.Attribute != null)
            .OrderBy(entry => entry.Attribute!.Order)
            .ThenBy(entry => entry.Attribute!.Name, StringComparer.Ordinal)
            .Select(entry => entry.Method)
            .ToList();

        var duplicateNames = methods
            .GroupBy(method => method.GetCustomAttribute<DynamicToolAttribute>()!.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicateNames.Count > 0)
            throw new InvalidOperationException($"Duplicate dynamic tool names: {string.Join(", ", duplicateNames)}.");

        var duplicateOrders = methods
            .GroupBy(method => method.GetCustomAttribute<DynamicToolAttribute>()!.Order)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicateOrders.Count > 0)
            throw new InvalidOperationException($"Duplicate dynamic tool orders: {string.Join(", ", duplicateOrders)}.");

        return methods;
    }

    private static JsonObject BuildInputSchema(MethodInfo method, string toolName)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var parameter in ToolParameters(method))
        {
            var description = parameter.GetCustomAttribute<DescriptionAttribute>()?.Description;
            if (string.IsNullOrWhiteSpace(description))
                throw new InvalidOperationException($"Dynamic tool '{toolName}' parameter '{parameter.Name}' must declare DescriptionAttribute.");

            var parameterName = parameter.Name
                ?? throw new InvalidOperationException($"Dynamic tool '{toolName}' contains a parameter without a name.");
            var propertySchema = BuildParameterSchema(parameter, toolName);
            propertySchema["description"] = description;
            properties[parameterName] = propertySchema;
            if (!parameter.HasDefaultValue)
                required.Add(parameterName);
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false
        };
        if (required.Count > 0)
            schema["required"] = required;
        return schema;
    }

    private static JsonObject BuildParameterSchema(ParameterInfo parameter, string toolName)
    {
        var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
        if (type == typeof(string))
            return new JsonObject { ["type"] = "string" };
        if (type == typeof(bool))
            return new JsonObject { ["type"] = "boolean" };
        if (type == typeof(int) || type == typeof(long) || type == typeof(short))
            return new JsonObject { ["type"] = "integer" };
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return new JsonObject { ["type"] = "number" };
        if (type == typeof(JsonObject))
            return new JsonObject { ["type"] = "object" };
        if (IsStringListType(type))
        {
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" }
            };
        }

        throw new InvalidOperationException(
            $"Dynamic tool '{toolName}' parameter '{parameter.Name}' has unsupported type '{parameter.ParameterType}'.");
    }

    internal static IEnumerable<ParameterInfo> ToolParameters(MethodInfo method) =>
        method.GetParameters().Where(parameter =>
            parameter.ParameterType != typeof(ManagedAppBindingToolCallContext)
            && parameter.ParameterType != typeof(CancellationToken));

    internal static bool IsStringListType(Type type)
    {
        if (type == typeof(string[]))
            return true;

        if (!type.IsGenericType)
            return false;

        var genericDefinition = type.GetGenericTypeDefinition();
        return (genericDefinition == typeof(List<>)
                || genericDefinition == typeof(IReadOnlyList<>)
                || genericDefinition == typeof(IEnumerable<>))
               && type.GetGenericArguments()[0] == typeof(string);
    }
}

public interface IManagedDynamicToolRegistry<TTarget>
    where TTarget : class
{
    IReadOnlyList<DynamicToolSpec> ToolSpecs { get; }

    bool ContainsTool(string toolName);

    ValueTask<DynamicToolCallResult> InvokeAsync(
        TTarget target,
        ManagedAppBindingToolCallContext context,
        JsonObject arguments,
        CancellationToken cancellationToken);
}

/// <summary>
/// Dispatches attributed managed dynamic tools and exposes their generated specs.
/// </summary>
public sealed class ManagedDynamicToolRegistry<TTarget>
    : IManagedDynamicToolRegistry<TTarget>
    where TTarget : class
{
    private readonly IReadOnlyDictionary<string, DynamicToolMethod> _methodsByName;

    /// <summary>
    /// Creates a registry from all attributed dynamic tool methods on <typeparamref name="TTarget"/>.
    /// </summary>
    public ManagedDynamicToolRegistry(string? toolNamespace = null)
    {
        var methods = DynamicToolSpecFactory.DiscoverMethods(typeof(TTarget));
        var dynamicToolMethods = methods
            .Select(method => new DynamicToolMethod(
                method,
                method.GetCustomAttribute<DynamicToolAttribute>()!.Name,
                method.GetParameters(),
                method.ReturnType))
            .ToArray();

        ToolSpecs = dynamicToolMethods
            .Select(method => DynamicToolSpecFactory.Create(method.Method, toolNamespace))
            .ToList();
        _methodsByName = dynamicToolMethods.ToDictionary(
            method => method.ToolName,
            StringComparer.Ordinal);
        foreach (var method in dynamicToolMethods)
            ValidateReturnType(method);
    }

    /// <summary>
    /// Ordered dynamic tool specs generated from the target type.
    /// </summary>
    public IReadOnlyList<DynamicToolSpec> ToolSpecs { get; }

    /// <summary>
    /// Returns whether this registry contains the given model-visible tool name.
    /// </summary>
    public bool ContainsTool(string toolName) => _methodsByName.ContainsKey(toolName);

    /// <summary>
    /// Invokes a registered dynamic tool against the target instance.
    /// </summary>
    public async ValueTask<DynamicToolCallResult> InvokeAsync(
        TTarget target,
        ManagedAppBindingToolCallContext context,
        JsonObject arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!_methodsByName.TryGetValue(context.ToolName, out var method))
            throw AppServerErrors.InvalidParams($"Dynamic tool '{context.ToolName}' is not supported.");

        var parameterValues = method.Parameters
            .Select(parameter => BindParameter(parameter, context, arguments, cancellationToken))
            .ToArray();

        object? result;
        try
        {
            result = method.Method.Invoke(target, parameterValues);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        return result switch
        {
            DynamicToolCallResult direct => direct,
            Task<DynamicToolCallResult> task => await task.ConfigureAwait(false),
            ValueTask<DynamicToolCallResult> valueTask => await valueTask.ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                $"Dynamic tool '{context.ToolName}' returned unsupported type '{method.ReturnType}'.")
        };
    }

    private static object? BindParameter(
        ParameterInfo parameter,
        ManagedAppBindingToolCallContext context,
        JsonObject arguments,
        CancellationToken cancellationToken)
    {
        if (parameter.ParameterType == typeof(ManagedAppBindingToolCallContext))
            return context;
        if (parameter.ParameterType == typeof(CancellationToken))
            return cancellationToken;

        var name = parameter.Name
            ?? throw new InvalidOperationException("Dynamic tool parameter is missing a name.");
        if (!arguments.TryGetPropertyValue(name, out var node) || node == null)
        {
            if (parameter.HasDefaultValue)
                return parameter.DefaultValue;
            throw AppServerErrors.InvalidParams($"'{name}' is required.");
        }

        var value = ConvertArgumentValue(parameter, node);
        if (value == null && !parameter.HasDefaultValue)
            throw AppServerErrors.InvalidParams($"'{name}' is required.");
        return value;
    }

    private static object? ConvertArgumentValue(ParameterInfo parameter, JsonNode node)
    {
        var nullableType = Nullable.GetUnderlyingType(parameter.ParameterType);
        var targetType = nullableType ?? parameter.ParameterType;
        var name = parameter.Name ?? "argument";

        if (targetType == typeof(string))
        {
            if (node.GetValueKind() != JsonValueKind.String)
                throw AppServerErrors.InvalidParams($"'{name}' must be a string.");
            var value = node.GetValue<string>()?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        if (targetType == typeof(bool))
            return ConvertBoolean(name, node);

        if (targetType == typeof(int))
            return ConvertNumber<int>(name, node, value => value.TryGetValue<int>(out var parsed) ? parsed : null);
        if (targetType == typeof(long))
            return ConvertNumber<long>(name, node, value => value.TryGetValue<long>(out var parsed) ? parsed : null);
        if (targetType == typeof(short))
            return ConvertNumber<short>(name, node, value => value.TryGetValue<short>(out var parsed) ? parsed : null);
        if (targetType == typeof(float))
            return ConvertNumber<float>(name, node, value => value.TryGetValue<float>(out var parsed) ? parsed : null);
        if (targetType == typeof(double))
            return ConvertNumber<double>(name, node, value => value.TryGetValue<double>(out var parsed) ? parsed : null);
        if (targetType == typeof(decimal))
            return ConvertNumber<decimal>(name, node, value => value.TryGetValue<decimal>(out var parsed) ? parsed : null);

        if (targetType == typeof(JsonObject))
        {
            if (node is not JsonObject obj)
                throw AppServerErrors.InvalidParams($"'{name}' must be a JSON object.");
            return obj.DeepClone();
        }

        if (DynamicToolSpecFactory.IsStringListType(targetType))
            return ConvertStringList(name, node, targetType);

        throw new InvalidOperationException(
            $"Dynamic tool parameter '{name}' has unsupported type '{parameter.ParameterType}'.");
    }

    private static bool ConvertBoolean(string name, JsonNode node)
    {
        if (node.GetValueKind() == JsonValueKind.True)
            return true;
        if (node.GetValueKind() == JsonValueKind.False)
            return false;
        if (node.GetValueKind() == JsonValueKind.String && bool.TryParse(node.GetValue<string>(), out var parsed))
            return parsed;
        throw AppServerErrors.InvalidParams($"'{name}' must be a boolean.");
    }

    private static object ConvertNumber<T>(
        string name,
        JsonNode node,
        Func<JsonValue, T?> converter)
        where T : struct
    {
        if (node is not JsonValue value || node.GetValueKind() != JsonValueKind.Number)
            throw AppServerErrors.InvalidParams($"'{name}' must be a number.");
        var parsed = converter(value);
        if (parsed == null)
            throw AppServerErrors.InvalidParams($"'{name}' is outside the supported numeric range.");
        return parsed.Value;
    }

    private static object ConvertStringList(string name, JsonNode node, Type targetType)
    {
        var result = new List<string>();
        if (node.GetValueKind() == JsonValueKind.String)
        {
            var value = node.GetValue<string>()?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
                result.Add(value);
        }
        else
        {
            if (node is not JsonArray array)
                throw AppServerErrors.InvalidParams($"'{name}' must be an array of strings.");
            foreach (var item in array)
            {
                if (item == null || item.GetValueKind() != JsonValueKind.String)
                    throw AppServerErrors.InvalidParams($"'{name}' must be an array of strings.");
                var value = item.GetValue<string>()?.Trim();
                if (!string.IsNullOrWhiteSpace(value) && !result.Contains(value, StringComparer.Ordinal))
                    result.Add(value);
            }
        }

        if (targetType == typeof(string[]))
            return result.ToArray();
        return result;
    }

    private static void ValidateReturnType(DynamicToolMethod method)
    {
        if (method.ReturnType == typeof(DynamicToolCallResult)
            || method.ReturnType == typeof(Task<DynamicToolCallResult>)
            || method.ReturnType == typeof(ValueTask<DynamicToolCallResult>))
        {
            return;
        }

        throw new InvalidOperationException($"Dynamic tool '{method.ToolName}' returned unsupported type '{method.ReturnType}'.");
    }

    private sealed record DynamicToolMethod(
        MethodInfo Method,
        string ToolName,
        ParameterInfo[] Parameters,
        Type ReturnType);
}

internal static class GeneratedDynamicToolArgumentBinder
{
    public static string BindRequiredString(JsonObject arguments, string name)
    {
        var value = BindString(arguments, name, hasDefaultValue: false, defaultValue: null);
        if (value == null)
            throw AppServerErrors.InvalidParams($"'{name}' is required.");
        return value;
    }

    public static string? BindOptionalString(JsonObject arguments, string name, string? defaultValue = null) =>
        BindString(arguments, name, hasDefaultValue: true, defaultValue);

    public static bool BindRequiredBool(JsonObject arguments, string name)
    {
        var value = BindNullableBool(arguments, name, hasDefaultValue: false, defaultValue: null);
        if (!value.HasValue)
            throw AppServerErrors.InvalidParams($"'{name}' is required.");
        return value.Value;
    }

    public static bool? BindOptionalBool(JsonObject arguments, string name, bool? defaultValue = null) =>
        BindNullableBool(arguments, name, hasDefaultValue: true, defaultValue);

    public static List<string>? BindOptionalStringList(JsonObject arguments, string name) =>
        BindStringList(arguments, name, hasDefaultValue: true, defaultValue: null);

    public static string[]? BindOptionalStringArray(JsonObject arguments, string name) =>
        BindStringList(arguments, name, hasDefaultValue: true, defaultValue: null)?.ToArray();

    public static List<string> BindRequiredStringList(JsonObject arguments, string name)
    {
        var value = BindStringList(arguments, name, hasDefaultValue: false, defaultValue: null);
        if (value == null)
            throw AppServerErrors.InvalidParams($"'{name}' is required.");
        return value;
    }

    public static string[] BindRequiredStringArray(JsonObject arguments, string name) =>
        BindRequiredStringList(arguments, name).ToArray();

    public static int? BindOptionalInt(JsonObject arguments, string name, int? defaultValue = null) =>
        BindNullableNumber<int>(arguments, name, hasDefaultValue: true, defaultValue, value => value.TryGetValue<int>(out var parsed) ? parsed : null);

    public static long? BindOptionalLong(JsonObject arguments, string name, long? defaultValue = null) =>
        BindNullableNumber<long>(arguments, name, hasDefaultValue: true, defaultValue, value => value.TryGetValue<long>(out var parsed) ? parsed : null);

    private static string? BindString(JsonObject arguments, string name, bool hasDefaultValue, string? defaultValue)
    {
        if (!TryGetArgument(arguments, name, hasDefaultValue, out var node))
            return defaultValue;

        if (node!.GetValueKind() != JsonValueKind.String)
            throw AppServerErrors.InvalidParams($"'{name}' must be a string.");

        var value = node.GetValue<string>()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool? BindNullableBool(JsonObject arguments, string name, bool hasDefaultValue, bool? defaultValue)
    {
        if (!TryGetArgument(arguments, name, hasDefaultValue, out var node))
            return defaultValue;

        if (node!.GetValueKind() == JsonValueKind.True)
            return true;
        if (node.GetValueKind() == JsonValueKind.False)
            return false;
        if (node.GetValueKind() == JsonValueKind.String && bool.TryParse(node.GetValue<string>(), out var parsed))
            return parsed;
        throw AppServerErrors.InvalidParams($"'{name}' must be a boolean.");
    }

    private static T? BindNullableNumber<T>(
        JsonObject arguments,
        string name,
        bool hasDefaultValue,
        T? defaultValue,
        Func<JsonValue, T?> converter)
        where T : struct
    {
        if (!TryGetArgument(arguments, name, hasDefaultValue, out var node))
            return defaultValue;

        if (node is not JsonValue value || node.GetValueKind() != JsonValueKind.Number)
            throw AppServerErrors.InvalidParams($"'{name}' must be a number.");
        var parsed = converter(value);
        if (parsed == null)
            throw AppServerErrors.InvalidParams($"'{name}' is outside the supported numeric range.");
        return parsed.Value;
    }

    private static List<string>? BindStringList(
        JsonObject arguments,
        string name,
        bool hasDefaultValue,
        List<string>? defaultValue)
    {
        if (!TryGetArgument(arguments, name, hasDefaultValue, out var node))
            return defaultValue;

        var result = new List<string>();
        if (node!.GetValueKind() == JsonValueKind.String)
        {
            var value = node.GetValue<string>()?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
                result.Add(value);
            return result;
        }

        if (node is not JsonArray array)
            throw AppServerErrors.InvalidParams($"'{name}' must be an array of strings.");

        foreach (var item in array)
        {
            if (item == null || item.GetValueKind() != JsonValueKind.String)
                throw AppServerErrors.InvalidParams($"'{name}' must be an array of strings.");
            var value = item.GetValue<string>()?.Trim();
            if (!string.IsNullOrWhiteSpace(value) && !result.Contains(value, StringComparer.Ordinal))
                result.Add(value);
        }

        return result;
    }

    private static bool TryGetArgument(JsonObject arguments, string name, bool hasDefaultValue, out JsonNode? node)
    {
        if (!arguments.TryGetPropertyValue(name, out node) || node == null)
        {
            if (hasDefaultValue)
                return false;
            throw AppServerErrors.InvalidParams($"'{name}' is required.");
        }

        return true;
    }
}
