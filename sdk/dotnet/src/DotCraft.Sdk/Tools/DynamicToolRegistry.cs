using System.Reflection;
using System.Text.Json;

namespace DotCraft.Sdk.Tools;

/// <summary>
/// Reflection-based registry for attribute-authored dynamic tools. Discovers
/// <see cref="DynamicToolAttribute"/> methods on a target object, auto-generates each tool's
/// JSON Schema, and dispatches calls with argument binding and structured error mapping.
/// </summary>
/// <remarks>
/// Two authoring conventions are supported per method:
/// <list type="bullet">
/// <item>a single typed-arguments record/POCO parameter (schema from the type), or</item>
/// <item>flat parameters annotated with <see cref="System.ComponentModel.DescriptionAttribute"/>.</item>
/// </list>
/// A parameter assignable to <see cref="DynamicToolRegistryOptions.ContextType"/> and a
/// <see cref="CancellationToken"/> are injected and excluded from the schema.
/// Results are normalized to <see cref="DynamicToolOutcome"/>; use
/// <see cref="InvokeJsonEnvelopeAsync(string,string,JsonElement,CancellationToken)"/> for the
/// <c>{ok,data}</c> JSON envelope.
/// </remarks>
public sealed class DynamicToolRegistry
{
    private readonly DynamicToolRegistryOptions _options;
    private readonly Dictionary<string, RegisteredTool> _tools = new(StringComparer.Ordinal);
    private readonly List<DynamicToolDescriptor> _descriptors = new();

    public DynamicToolRegistry(DynamicToolRegistryOptions? options = null)
    {
        _options = options ?? new DynamicToolRegistryOptions();
    }

    /// <summary>
    /// Registers every <see cref="DynamicToolAttribute"/> method on <paramref name="target"/>
    /// under the given namespace.
    /// </summary>
    public void Register(object target, string @namespace)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrEmpty(@namespace);

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        foreach (MethodInfo method in target.GetType().GetMethods(flags))
        {
            var attribute = method.GetCustomAttribute<DynamicToolAttribute>();
            if (attribute is null)
            {
                continue;
            }

            string fullName = $"{@namespace}.{attribute.Name}";
            if (_tools.ContainsKey(fullName))
            {
                throw new InvalidOperationException($"Duplicate dynamic tool '{fullName}'.");
            }

            string description = attribute.Description
                ?? method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description
                ?? "";

            (RegisteredTool tool, JsonElement schema) = BuildTool(target, method, fullName);

            _tools[fullName] = tool;
            _descriptors.Add(new DynamicToolDescriptor
            {
                Namespace = @namespace,
                LocalName = attribute.Name,
                Description = description,
                InputSchema = schema,
                Order = attribute.Order,
                DeferLoading = attribute.DeferLoading,
            });
        }
    }

    /// <summary>Lists all tool declarations, ordered by (Order, Name).</summary>
    public IReadOnlyList<DynamicToolDescriptor> ListDescriptors()
        => _descriptors
            .OrderBy(d => d.Order)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>Invokes a tool by namespace + short name, returning a normalized outcome.</summary>
    public Task<DynamicToolOutcome> InvokeAsync(string @namespace, string shortName, JsonElement arguments, CancellationToken cancellationToken)
        => InvokeAsync(@namespace, shortName, arguments, context: null, cancellationToken);

    /// <summary>Invokes a tool by namespace + short name with a context object.</summary>
    public async Task<DynamicToolOutcome> InvokeAsync(string @namespace, string shortName, JsonElement arguments, object? context, CancellationToken cancellationToken)
    {
        string fullName = $"{@namespace}.{shortName}";
        if (!_tools.TryGetValue(fullName, out RegisteredTool? tool))
        {
            return DynamicToolOutcome.Error(_options.UnknownToolCode, $"Unknown tool '{fullName}'.");
        }

        object?[] invokeArgs;
        try
        {
            ValidateArguments(arguments, tool.Schema);
            invokeArgs = BindArguments(tool, arguments, context, cancellationToken);
        }
        catch (JsonException ex)
        {
            return DynamicToolOutcome.Error(_options.InvalidArgumentCode, ex.Message, null, _options.InvalidArgumentHint);
        }

        try
        {
            object? result = tool.Method.Invoke(tool.Target, invokeArgs);
            object? data = await UnwrapAsync(result).ConfigureAwait(false);
            return DynamicToolOutcome.Success(data);
        }
        catch (TargetInvocationException ex)
            when (ex.InnerException is OperationCanceledException)
        {
            throw ex.InnerException;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is DynamicToolException toolError)
        {
            return DynamicToolOutcome.Error(toolError.Code, toolError.Message, toolError.Field, toolError.Hint);
        }
        catch (DynamicToolException toolError)
        {
            return DynamicToolOutcome.Error(toolError.Code, toolError.Message, toolError.Field, toolError.Hint);
        }
        catch (Exception ex)
        {
            Exception inner = (ex as TargetInvocationException)?.InnerException ?? ex;
            _options.InternalErrorLogger?.Invoke(inner, fullName);
            return DynamicToolOutcome.Error(_options.InternalErrorCode, _options.InternalErrorMessage);
        }
    }

    /// <summary>Invokes a tool and shapes the outcome into the <c>{ok,data}</c> JSON envelope.</summary>
    public Task<JsonElement> InvokeJsonEnvelopeAsync(string @namespace, string shortName, JsonElement arguments, CancellationToken cancellationToken)
        => InvokeJsonEnvelopeAsync(@namespace, shortName, arguments, context: null, cancellationToken);

    /// <summary>Invokes a tool with a context object and shapes the outcome into the JSON envelope.</summary>
    public async Task<JsonElement> InvokeJsonEnvelopeAsync(string @namespace, string shortName, JsonElement arguments, object? context, CancellationToken cancellationToken)
    {
        DynamicToolOutcome outcome = await InvokeAsync(@namespace, shortName, arguments, context, cancellationToken).ConfigureAwait(false);
        return DynamicToolEnvelope.ToJson(outcome, _options.JsonOptions);
    }

    private (RegisteredTool Tool, JsonElement Schema) BuildTool(object target, MethodInfo method, string fullName)
    {
        ParameterInfo[] parameters = method.GetParameters();
        var slots = new ParamSlot[parameters.Length];
        var schemaParams = new List<ParameterInfo>();
        int contextCount = 0;
        int cancellationCount = 0;

        for (int i = 0; i < parameters.Length; i++)
        {
            ParameterInfo p = parameters[i];
            if (p.ParameterType.IsByRef || p.IsOut)
            {
                throw new InvalidOperationException(
                    $"Dynamic tool '{fullName}' parameter '{p.Name}' cannot be ref or out.");
            }

            if (_options.ContextType is { } contextType &&
                p.ParameterType == typeof(object))
            {
                throw new InvalidOperationException(
                    $"Dynamic tool '{fullName}' parameter '{p.Name}' is ambiguous: object cannot be used as a tool argument or context parameter when ContextType is configured.");
            }
            else if (_options.ContextType is { } assignableContextType &&
                p.ParameterType.IsAssignableFrom(assignableContextType))
            {
                contextCount++;
                slots[i] = new ParamSlot(SlotKind.Context, null, p);
            }
            else if (p.ParameterType == typeof(CancellationToken))
            {
                cancellationCount++;
                slots[i] = new ParamSlot(SlotKind.Cancellation, null, null);
            }
            else
            {
                slots[i] = new ParamSlot(SlotKind.Pending, null, p);
                schemaParams.Add(p);
            }
        }

        if (contextCount > 1)
        {
            throw new InvalidOperationException($"Dynamic tool '{fullName}' can declare at most one context parameter.");
        }

        if (cancellationCount > 1)
        {
            throw new InvalidOperationException($"Dynamic tool '{fullName}' can declare at most one CancellationToken parameter.");
        }

        bool recordMode = schemaParams.Count == 1 && IsComplexType(schemaParams[0].ParameterType);
        Type? argsType = null;
        JsonElement schema;

        if (recordMode)
        {
            argsType = schemaParams[0].ParameterType;
            schema = DynamicToolSchemaGenerator.Generate(argsType, _options.JsonOptions);
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Kind == SlotKind.Pending)
                {
                    slots[i] = new ParamSlot(SlotKind.RecordArgs, null, slots[i].Parameter);
                }
            }
        }
        else
        {
            schema = DynamicToolSchemaGenerator.GenerateFromParameters(schemaParams, _options.JsonOptions);
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Kind == SlotKind.Pending)
                {
                    ParameterInfo p = slots[i].Parameter!;
                    slots[i] = new ParamSlot(SlotKind.FlatArg, ToCamelCase(p.Name ?? "arg"), p);
                }
            }
        }

        return (new RegisteredTool(target, method, slots, recordMode, argsType, schema), schema);
    }

    private object?[] BindArguments(RegisteredTool tool, JsonElement arguments, object? context, CancellationToken cancellationToken)
    {
        var values = new object?[tool.Slots.Length];
        object? recordArgs = tool.RecordMode ? DeserializeRecord(tool.ArgsType!, arguments) : null;

        for (int i = 0; i < tool.Slots.Length; i++)
        {
            ParamSlot slot = tool.Slots[i];
            values[i] = slot.Kind switch
            {
                SlotKind.Context => BindContext(slot, context),
                SlotKind.Cancellation => cancellationToken,
                SlotKind.RecordArgs => recordArgs,
                SlotKind.FlatArg => BindFlatArgument(slot, arguments),
                _ => null,
            };
        }

        return values;
    }

    private object? DeserializeRecord(Type argsType, JsonElement arguments)
    {
        object? args = arguments.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? CreateDefault(argsType)
            : arguments.Deserialize(argsType, _options.JsonOptions);
        return args ?? CreateDefault(argsType);
    }

    private object? BindFlatArgument(ParamSlot slot, JsonElement arguments)
    {
        ParameterInfo parameter = slot.Parameter!;
        if (arguments.ValueKind == JsonValueKind.Object &&
            TryGetProperty(arguments, slot.PropertyName!, out JsonElement value) &&
            value.ValueKind != JsonValueKind.Null)
        {
            return value.Deserialize(parameter.ParameterType, _options.JsonOptions);
        }

        if (parameter.HasDefaultValue)
        {
            return parameter.DefaultValue;
        }

        return CreateDefault(parameter.ParameterType);
    }

    private static object? BindContext(ParamSlot slot, object? context)
    {
        Type parameterType = slot.Parameter!.ParameterType;
        if (context is null)
        {
            if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) is null)
            {
                throw new InvalidOperationException(
                    $"Dynamic tool context for '{parameterType.FullName}' was not supplied.");
            }

            return null;
        }

        if (!parameterType.IsInstanceOfType(context))
        {
            throw new InvalidOperationException(
                $"Dynamic tool context type '{context.GetType().FullName}' is not assignable to '{parameterType.FullName}'.");
        }

        return context;
    }

    private static void ValidateArguments(JsonElement arguments, JsonElement schema)
    {
        if (arguments.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            using JsonDocument empty = JsonDocument.Parse("{}");
            ValidateValue(empty.RootElement, schema, "$");
            return;
        }

        ValidateValue(arguments, schema, "$");
    }

    private static void ValidateValue(JsonElement value, JsonElement schema, string path)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            HashSet<string>? propertyNames = null;
            JsonElement properties = default;
            if (schema.TryGetProperty("properties", out properties) && properties.ValueKind == JsonValueKind.Object)
            {
                propertyNames = properties.EnumerateObject()
                    .Select(property => property.Name)
                    .ToHashSet(StringComparer.Ordinal);
            }

            if (schema.TryGetProperty("additionalProperties", out JsonElement additional) &&
                additional.ValueKind == JsonValueKind.False)
            {
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    if (propertyNames is null || !propertyNames.Contains(property.Name))
                    {
                        throw new JsonException($"Unknown property '{path}.{property.Name}'.");
                    }
                }
            }

            if (schema.TryGetProperty("required", out JsonElement required) &&
                required.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement requiredName in required.EnumerateArray())
                {
                    string name = requiredName.GetString()!;
                    if (!value.TryGetProperty(name, out JsonElement requiredValue) ||
                        requiredValue.ValueKind == JsonValueKind.Null)
                    {
                        throw new JsonException($"Required property '{path}.{name}' is missing.");
                    }
                }
            }

            if (propertyNames is not null)
            {
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    if (properties.TryGetProperty(property.Name, out JsonElement propertySchema))
                    {
                        ValidateValue(property.Value, propertySchema, $"{path}.{property.Name}");
                    }
                }
            }

            return;
        }

        if (value.ValueKind == JsonValueKind.Array &&
            schema.TryGetProperty("items", out JsonElement itemSchema))
        {
            int index = 0;
            foreach (JsonElement item in value.EnumerateArray())
            {
                ValidateValue(item, itemSchema, $"{path}[{index++}]");
            }
        }
    }

    private bool TryGetProperty(JsonElement obj, string camelName, out JsonElement value)
    {
        if (obj.TryGetProperty(camelName, out value))
        {
            return true;
        }

        if (_options.JsonOptions.PropertyNameCaseInsensitive)
        {
            foreach (JsonProperty property in obj.EnumerateObject())
            {
                if (string.Equals(property.Name, camelName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static async Task<object?> UnwrapAsync(object? result)
    {
        switch (result)
        {
            case null:
                return null;
            case Task task:
                await task.ConfigureAwait(false);
                return GetTaskResult(task);
            case ValueTask valueTask:
                await valueTask.ConfigureAwait(false);
                return null;
            default:
                Type type = result.GetType();
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>))
                {
                    object asTask = type.GetMethod("AsTask")!.Invoke(result, null)!;
                    var task = (Task)asTask;
                    await task.ConfigureAwait(false);
                    return GetTaskResult(task);
                }

                return result;
        }
    }

    private static object? GetTaskResult(Task task)
    {
        Type type = task.GetType();
        if (!type.IsGenericType)
        {
            return null;
        }

        PropertyInfo? resultProperty = type.GetProperty("Result");
        return resultProperty?.GetValue(task);
    }

    private static object? CreateDefault(Type type)
        => type.IsValueType ? Activator.CreateInstance(type) : null;

    private static bool IsComplexType(Type type)
    {
        Type t = Nullable.GetUnderlyingType(type) ?? type;
        if (t == typeof(string) || t.IsPrimitive || t.IsEnum)
        {
            return false;
        }

        if (t == typeof(decimal) || t == typeof(DateTime) || t == typeof(DateTimeOffset)
            || t == typeof(TimeSpan) || t == typeof(Guid) || t == typeof(Uri))
        {
            return false;
        }

        if (typeof(JsonElement).IsAssignableFrom(t) || typeof(System.Text.Json.Nodes.JsonNode).IsAssignableFrom(t)
            || typeof(JsonDocument).IsAssignableFrom(t))
        {
            return false;
        }

        if (t.IsArray || typeof(System.Collections.IEnumerable).IsAssignableFrom(t))
        {
            return false;
        }

        return t.IsClass || t.IsValueType;
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0]))
        {
            return name;
        }

        Span<char> buffer = stackalloc char[name.Length];
        name.CopyTo(buffer);
        buffer[0] = char.ToLowerInvariant(buffer[0]);
        return new string(buffer);
    }

    private enum SlotKind
    {
        Pending,
        Context,
        Cancellation,
        RecordArgs,
        FlatArg,
    }

    private readonly record struct ParamSlot(SlotKind Kind, string? PropertyName, ParameterInfo? Parameter);

    private sealed record RegisteredTool(
        object Target,
        MethodInfo Method,
        ParamSlot[] Slots,
        bool RecordMode,
        Type? ArgsType,
        JsonElement Schema);
}
