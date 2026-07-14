using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCraft.Protocol.AppServer;

namespace DotCraft.AppBinding;

/// <summary>
/// Legacy App Binding v1 tool descriptor retained only for the temporary M1 adapter.
/// Runtime Dynamic Tools use <see cref="RuntimeDynamicToolDeclaration"/> instead.
/// </summary>
public sealed class AppBoundToolSpec
{
    public string? Namespace { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public JsonObject? InputSchema { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DeferLoading { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelToolApprovalDescriptor? Approval { get; set; }

    [JsonPropertyName("_meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LegacyAppBindingToolMeta? Meta { get; set; }
}

/// <summary>
/// Legacy App Binding v1 invocation result retained only for the temporary M1 adapter.
/// </summary>
public sealed class AppBoundToolCallResult
{
    public bool Success { get; set; }

    public List<ExtChannelToolContentItem>? ContentItems { get; set; }

    public JsonNode? StructuredResult { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    [JsonPropertyName("_meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Meta { get; set; }
}
