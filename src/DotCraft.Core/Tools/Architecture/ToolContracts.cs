using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>Identifies the implementation family that contributed a tool.</summary>
public enum ToolSourceKind
{
    /// <summary>A tool implemented by the DotCraft server.</summary>
    CoreNative,
    /// <summary>A trusted in-process plugin tool.</summary>
    PluginNative,
    /// <summary>A tool exposed by an MCP server.</summary>
    Mcp,
    /// <summary>A thread-scoped callback exposed by an AppServer client.</summary>
    RuntimeDynamic,
    /// <summary>A read-only App Binding projection source.</summary>
    LegacyAppBinding,
}

/// <summary>Controls how a permitted tool is published to the model.</summary>
public enum ToolExposure
{
    /// <summary>The tool is included directly in the model tool list.</summary>
    Direct,
    /// <summary>The tool is discoverable through deferred loading.</summary>
    Deferred,
    /// <summary>The tool is direct for the model but absent from nested code-mode tools.</summary>
    DirectModelOnly,
    /// <summary>The tool is not published to the model.</summary>
    Hidden,
}

/// <summary>Identifies callers that may invoke a registered tool.</summary>
[Flags]
public enum ToolInvocationAudience
{
    /// <summary>No caller is permitted.</summary>
    None = 0,
    /// <summary>A model-generated tool call.</summary>
    Model = 1,
    /// <summary>A trusted DotCraft host call.</summary>
    Host = 2,
    /// <summary>An authorized embedded application call.</summary>
    App = 4,
}

/// <summary>Describes whether a runtime binding was available when registered.</summary>
public enum ToolBindingAvailability
{
    /// <summary>The binding may be invoked after its live lease is checked.</summary>
    Available,
    /// <summary>The binding has no usable executor.</summary>
    Unavailable,
}

/// <summary>Classifies the trusted thread shape supplied to tool sources during planning.</summary>
public enum ToolPlanningThreadKind
{
    /// <summary>The thread could not be safely classified.</summary>
    Unknown,
    /// <summary>A user-facing top-level conversation, including ordinary forks and siblings.</summary>
    UserTopLevel,
    /// <summary>A thread owned by a product module, such as an Agent Teams mission thread.</summary>
    ModuleManaged,
    /// <summary>A child session created by the SubAgent runtime.</summary>
    SubAgentChild,
    /// <summary>Unattended automation, cron, heartbeat, or equivalent background work.</summary>
    Unattended,
    /// <summary>An internal or ephemeral DotCraft helper thread.</summary>
    Internal,
}

/// <summary>A case-sensitive canonical tool name with an optional namespace.</summary>
public readonly record struct ToolName
{
    /// <summary>Creates a canonical tool name.</summary>
    /// <param name="namespace">The namespace, or <see langword="null"/> for a top-level function.</param>
    /// <param name="name">The source-independent local name.</param>
    public ToolName(string? @namespace, string name)
    {
        if (@namespace is not null && string.IsNullOrWhiteSpace(@namespace))
            throw new ArgumentException("A tool namespace must be non-empty when supplied.", nameof(@namespace));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A tool name is required.", nameof(name));
        if (@namespace is not null && !IsSafeComponent(@namespace))
            throw new ArgumentException("A tool namespace must contain only ASCII letters, digits, and underscores.", nameof(@namespace));
        if (!IsSafeComponent(name))
            throw new ArgumentException("A tool name must contain only ASCII letters, digits, and underscores.", nameof(name));
        var flatLength = name.Length + (@namespace is null ? 0 : @namespace.Length + 2);
        if (flatLength > ProviderToolProjector.MaximumNameBytes)
            throw new ArgumentException("A flattened tool identity must not exceed 64 ASCII bytes.", nameof(name));

        Namespace = @namespace;
        Name = name;
    }

    /// <summary>Gets the namespace, or <see langword="null"/> for a top-level function.</summary>
    public string? Namespace { get; }

    /// <summary>Gets the local name.</summary>
    public string Name { get; }

    /// <inheritdoc />
    public override string ToString() => Namespace is null ? Name : $"{Namespace}.{Name}";

    /// <summary>Safely validates untrusted provider identity components.</summary>
    internal static bool TryCreate(string? @namespace, string? name, out ToolName toolName)
    {
        toolName = default;
        if ((@namespace is not null && string.IsNullOrWhiteSpace(@namespace))
            || string.IsNullOrWhiteSpace(name)
            || (@namespace is not null && !IsSafeComponent(@namespace))
            || !IsSafeComponent(name))
        {
            return false;
        }

        if (name.Length + (@namespace is null ? 0 : @namespace.Length + 2)
            > ProviderToolProjector.MaximumNameBytes)
        {
            return false;
        }

        toolName = new ToolName(@namespace, name);
        return true;
    }

    private static bool IsSafeComponent(string value) =>
        value.All(static character => character is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '_');
}

/// <summary>Identifies a tool in its source's native identity space.</summary>
public readonly record struct SourceToolId
{
    /// <summary>Creates a source tool identifier.</summary>
    public SourceToolId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A source tool identifier is required.", nameof(value));
        Value = value;
    }

    /// <summary>Gets the source-native identifier.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Identifies one durable semantic definition.</summary>
public readonly record struct ToolDefinitionId
{
    /// <summary>Creates a definition identifier.</summary>
    public ToolDefinitionId(ToolSourceKind kind, string sourceId, SourceToolId sourceToolId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            throw new ArgumentException("A source identifier is required.", nameof(sourceId));
        Kind = kind;
        SourceId = sourceId;
        SourceToolId = sourceToolId;
    }

    /// <summary>Gets the source family.</summary>
    public ToolSourceKind Kind { get; }
    /// <summary>Gets the source instance identifier.</summary>
    public string SourceId { get; }
    /// <summary>Gets the source-native tool identifier.</summary>
    public SourceToolId SourceToolId { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Kind}:{SourceId}:{SourceToolId}";
}

/// <summary>Identifies one live executor binding.</summary>
public readonly record struct RuntimeBindingId
{
    /// <summary>Creates a runtime binding identifier.</summary>
    public RuntimeBindingId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A runtime binding identifier is required.", nameof(value));
        Value = value;
    }

    /// <summary>Gets the identifier.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Identifies a trusted local presentation registration.</summary>
public readonly record struct PresentationId
{
    /// <summary>Creates a presentation identifier.</summary>
    public PresentationId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A presentation identifier is required.", nameof(value));
        Value = value;
    }

    /// <summary>Gets the identifier.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Safe, non-secret provenance suitable for diagnostics and persisted items.</summary>
public sealed record ToolProvenance
{
    /// <summary>Creates safe provenance.</summary>
    public ToolProvenance(ToolSourceKind kind, string sourceId, string? origin = null)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            throw new ArgumentException("A source identifier is required.", nameof(sourceId));
        Kind = kind;
        SourceId = sourceId;
        Origin = origin;
    }

    /// <summary>Gets the source family.</summary>
    public ToolSourceKind Kind { get; }
    /// <summary>Gets the safe source identifier.</summary>
    public string SourceId { get; }
    /// <summary>Gets an optional safe origin such as workspace, plugin, thread, or binding.</summary>
    public string? Origin { get; }
}

/// <summary>Source-provided hints consumed by server-authoritative policy.</summary>
/// <param name="RequiresApproval">Whether the source requests approval.</param>
/// <param name="ReadOnly">Whether the source declares the operation read-only.</param>
/// <param name="Destructive">Whether the source declares destructive behavior.</param>
/// <param name="OpenWorld">Whether the source declares open-world interaction.</param>
public sealed record ToolPolicyHints(
    bool RequiresApproval = false,
    bool ReadOnly = false,
    bool Destructive = false,
    bool OpenWorld = false);

/// <summary>Trusted metadata selecting a registered local renderer.</summary>
public sealed class ToolPresentationDescriptor
{
    /// <summary>Creates a presentation descriptor.</summary>
    public ToolPresentationDescriptor(
        PresentationId id,
        IReadOnlyDictionary<string, JsonElement>? options = null)
    {
        Id = id;
        Options = JsonCollections.Clone(options);
    }

    /// <summary>Gets the presentation registration identifier.</summary>
    public PresentationId Id { get; }
    /// <summary>Gets immutable renderer options.</summary>
    public IReadOnlyDictionary<string, JsonElement> Options { get; }
}

/// <summary>An immutable source-qualified semantic tool definition.</summary>
public sealed class ToolDefinition
{
    internal const int MaximumNamespaceDescriptionLength = 4096;

    /// <summary>Creates a tool definition without embedding a live executor.</summary>
    public ToolDefinition(
        ToolDefinitionId id,
        ToolName name,
        string description,
        JsonElement inputSchema,
        JsonElement? outputSchema = null,
        IReadOnlyDictionary<string, JsonElement>? annotations = null,
        ToolPolicyHints? policyHints = null,
        ToolPresentationDescriptor? presentation = null,
        ToolProvenance? provenance = null,
        string? namespaceDescription = null)
    {
        if (string.IsNullOrWhiteSpace(id.SourceId) || string.IsNullOrWhiteSpace(id.SourceToolId.Value))
            throw new ArgumentException("A non-default definition identifier is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(name.Name))
            throw new ArgumentException("A non-default canonical name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("A tool description is required.", nameof(description));
        if (inputSchema.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("A tool input schema must be a JSON object.", nameof(inputSchema));
        if (outputSchema is { ValueKind: not JsonValueKind.Object })
            throw new ArgumentException("A tool output schema must be a JSON object when supplied.", nameof(outputSchema));

        Id = id;
        Name = name;
        Description = description;
        InputSchema = inputSchema.Clone();
        OutputSchema = outputSchema?.Clone();
        Annotations = JsonCollections.Clone(annotations);
        PolicyHints = policyHints ?? new ToolPolicyHints();
        Presentation = presentation;
        Provenance = provenance ?? new ToolProvenance(id.Kind, id.SourceId);
        NamespaceDescription = NormalizeNamespaceDescription(namespaceDescription);
    }

    /// <summary>Gets the durable definition identifier.</summary>
    public ToolDefinitionId Id { get; }
    /// <summary>Gets the canonical name.</summary>
    public ToolName Name { get; }
    /// <summary>Gets the model-facing description.</summary>
    public string Description { get; }
    /// <summary>Gets a cloned input JSON Schema.</summary>
    public JsonElement InputSchema { get; }
    /// <summary>Gets a cloned output JSON Schema when declared.</summary>
    public JsonElement? OutputSchema { get; }
    /// <summary>Gets immutable source annotations.</summary>
    public IReadOnlyDictionary<string, JsonElement> Annotations { get; }
    /// <summary>Gets policy hints that cannot expand server authority.</summary>
    public ToolPolicyHints PolicyHints { get; }
    /// <summary>Gets optional trusted presentation metadata.</summary>
    public ToolPresentationDescriptor? Presentation { get; }
    /// <summary>Gets safe source provenance.</summary>
    public ToolProvenance Provenance { get; }
    /// <summary>
    /// Gets the untrusted model-facing description of the containing namespace. This metadata
    /// assists tool planning and deferred search; it is never promoted to a system instruction.
    /// </summary>
    public string? NamespaceDescription { get; }

    private static string? NormalizeNamespaceDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        return normalized.Length <= MaximumNamespaceDescriptionLength
            ? normalized
            : normalized[..MaximumNamespaceDescriptionLength];
    }
}
