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
                Name = fullName,
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
            return DynamicToolOutcome.Error(_options.InternalErrorCode, inner.Message);
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

        for (int i = 0; i < parameters.Length; i++)
        {
            ParameterInfo p = parameters[i];
            if (_options.ContextType is { } contextType && contextType.IsAssignableFrom(p.ParameterType))
            {
                slots[i] = new ParamSlot(SlotKind.Context, null, null);
            }
            else if (p.ParameterType == typeof(CancellationToken))
            {
                slots[i] = new ParamSlot(SlotKind.Cancellation, null, null);
            }
            else
            {
                slots[i] = new ParamSlot(SlotKind.Pending, null, p);
                schemaParams.Add(p);
            }
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

        return (new RegisteredTool(target, method, slots, recordMode, argsType), schema);
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
                SlotKind.Context => context,
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

    private sealed record RegisteredTool(object Target, MethodInfo Method, ParamSlot[] Slots, bool RecordMode, Type? ArgsType);
}
