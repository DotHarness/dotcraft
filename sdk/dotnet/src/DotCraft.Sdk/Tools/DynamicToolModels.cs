using System.Text.Json;

namespace DotCraft.Sdk.DynamicTools;

/// <summary>
/// Structured error thrown by a dynamic tool body. The registry maps it to a
/// <see cref="DynamicToolOutcome"/> error without leaking the exception.
/// </summary>
public class DynamicToolException : Exception
{
    public DynamicToolException(string code, string message, string? field = null, string? hint = null)
        : base(message)
    {
        Code = code;
        Field = field;
        Hint = hint;
    }

    /// <summary>Stable machine-readable error code.</summary>
    public string Code { get; }

    /// <summary>Optional offending field name.</summary>
    public string? Field { get; }

    /// <summary>Optional remediation hint.</summary>
    public string? Hint { get; }
}

/// <summary>
/// Description of one attribute-authored Runtime Dynamic Tool.
/// </summary>
public sealed class DynamicToolDescriptor
{
    /// <summary>Namespace supplied when the target was registered.</summary>
    public string Namespace { get; set; } = "";

    /// <summary>Local tool name supplied by <see cref="DynamicToolAttribute"/>.</summary>
    public string LocalName { get; set; } = "";

    /// <summary>Qualified <c>namespace.localName</c> identity.</summary>
    public string QualifiedName =>
        string.IsNullOrEmpty(Namespace) ? LocalName : $"{Namespace}.{LocalName}";

    public string Description { get; set; } = "";

    /// <summary>Full JSON Schema (preferred input for the AppServer).</summary>
    public JsonElement InputSchema { get; set; }

    /// <summary>Declaration sort order (lower first), then by <see cref="QualifiedName"/>.</summary>
    public int Order { get; set; }

    /// <summary>Whether the host should load the tool body lazily.</summary>
    public bool DeferLoading { get; set; }
}

/// <summary>
/// Normalized result of invoking a dynamic tool: either success with arbitrary data,
/// or a structured error. Callers shape this into their own wire result (see
/// <see cref="DynamicToolEnvelope"/> for the <c>{ok,data}</c> convention).
/// </summary>
public sealed class DynamicToolOutcome
{
    private DynamicToolOutcome(bool ok, object? data, string? code, string? message, string? field, string? hint)
    {
        Ok = ok;
        Data = data;
        Code = code;
        Message = message;
        Field = field;
        Hint = hint;
    }

    public bool Ok { get; }

    public object? Data { get; }

    public string? Code { get; }

    public string? Message { get; }

    public string? Field { get; }

    public string? Hint { get; }

    public static DynamicToolOutcome Success(object? data)
        => new(true, data, null, null, null, null);

    public static DynamicToolOutcome Error(string code, string message, string? field = null, string? hint = null)
        => new(false, null, code, message, field, hint);
}

/// <summary>
/// Configuration for a <see cref="DynamicToolRegistry"/>: JSON options, the optional context
/// type injected into tool methods, default error codes, and an internal-error sink.
/// </summary>
public sealed class DynamicToolRegistryOptions
{
    /// <summary>Serializer options for schema generation and argument binding.</summary>
    public JsonSerializerOptions JsonOptions { get; init; } = DynamicToolJson.Options;

    /// <summary>
    /// Optional context type. A method parameter assignable to this type receives the context
    /// object passed to <c>InvokeAsync</c> and is excluded from the generated schema.
    /// </summary>
    public Type? ContextType { get; init; }

    /// <summary>Error code used when a tool name is not registered.</summary>
    public string UnknownToolCode { get; init; } = "UNKNOWN_TOOL";

    /// <summary>Error code used when argument deserialization fails.</summary>
    public string InvalidArgumentCode { get; init; } = "INVALID_ARGUMENT";

    /// <summary>Optional hint attached to invalid-argument errors.</summary>
    public string? InvalidArgumentHint { get; init; }

    /// <summary>Error code used for unexpected exceptions from a tool body.</summary>
    public string InternalErrorCode { get; init; } = "INTERNAL";

    /// <summary>Model-safe message used for unexpected exceptions from a tool body.</summary>
    public string InternalErrorMessage { get; init; } = "The dynamic tool failed unexpectedly.";

    /// <summary>Optional sink for unexpected tool exceptions (exception, full tool name).</summary>
    public Action<Exception, string>? InternalErrorLogger { get; init; }
}

/// <summary>
/// Helper that shapes a <see cref="DynamicToolOutcome"/> into the <c>{ok,data}</c> /
/// <c>{ok:false,error:{code,message,field?,hint?}}</c> JSON envelope.
/// </summary>
public static class DynamicToolEnvelope
{
    public static JsonElement ToJson(DynamicToolOutcome outcome, JsonSerializerOptions options)
        => outcome.Ok
            ? JsonSerializer.SerializeToElement(new { ok = true, data = outcome.Data }, options)
            : JsonSerializer.SerializeToElement(
                new
                {
                    ok = false,
                    error = new
                    {
                        code = outcome.Code,
                        message = outcome.Message,
                        field = outcome.Field,
                        hint = outcome.Hint,
                    },
                },
                options);
}
