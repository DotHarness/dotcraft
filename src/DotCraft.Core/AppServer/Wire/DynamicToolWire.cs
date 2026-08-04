using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace DotCraft.AppServer;

/// <summary>
/// A Runtime Dynamic Tool declaration supplied by an AppServer client.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RuntimeDynamicToolFunctionSpec), "function")]
[JsonDerivedType(typeof(RuntimeDynamicToolNamespaceSpec), "namespace")]
public abstract class RuntimeDynamicToolDeclarationSpec
{
    /// <summary>Model-visible function name or namespace name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable description used for model planning.</summary>
    public string Description { get; set; } = string.Empty;
}
/// <summary>
/// A single Runtime Dynamic Function. Top-level functions have no namespace.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RuntimeDynamicToolFunctionSpec : RuntimeDynamicToolDeclarationSpec
{
    /// <summary>JSON Schema for the function arguments.</summary>
    public JsonObject? InputSchema { get; set; }

    /// <summary>Whether this function is discovered through its containing namespace.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DeferLoading { get; set; }

    /// <summary>Optional DotCraft approval hint evaluated by the server.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelToolApprovalSpec? Approval { get; set; }
}
/// <summary>
/// A named namespace containing Runtime Dynamic Functions.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RuntimeDynamicToolNamespaceSpec : RuntimeDynamicToolDeclarationSpec
{
    /// <summary>Functions contributed under <see cref="RuntimeDynamicToolDeclarationSpec.Name"/>.</summary>
    public List<RuntimeDynamicToolDeclarationSpec> Tools { get; set; } = [];
}
/// <summary>
/// Result returned by an AppServer client for a Runtime Dynamic Tool invocation.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RuntimeDynamicToolCallResult
{
    public bool Success { get; set; }

    public List<RuntimeDynamicToolContentItem>? ContentItems { get; set; }

    public JsonNode? StructuredContent { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }
}

/// <summary>
/// A validated text or image content item returned by a Runtime Dynamic Tool.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RuntimeDynamicToolContentItem
{
    public string Type { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MediaType { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DataBase64 { get; set; }
}
