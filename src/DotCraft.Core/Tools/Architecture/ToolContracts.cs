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

/// <summary>Immutable planning inputs for collecting source registrations.</summary>
public sealed class ToolPlanningContext
{
    /// <summary>Creates immutable planning inputs.</summary>
    public ToolPlanningContext(
        string threadId,
        string? turnId,
        string workspacePath,
        string mode,
        string? profile,
        IEnumerable<string>? providerCapabilities,
        long revision,
        ToolPlanningThreadKind threadKind = ToolPlanningThreadKind.Unknown,
        string? effectiveProviderId = null,
        string? effectiveMainModel = null)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            throw new ArgumentException("A thread identifier is required.", nameof(threadId));
        if (string.IsNullOrWhiteSpace(workspacePath))
            throw new ArgumentException("A workspace path is required.", nameof(workspacePath));
        if (string.IsNullOrWhiteSpace(mode))
            throw new ArgumentException("A mode is required.", nameof(mode));
        ThreadId = threadId;
        TurnId = turnId;
        WorkspacePath = workspacePath;
        Mode = mode;
        Profile = profile;
        ProviderCapabilities = (providerCapabilities ?? [])
            .ToFrozenSet(StringComparer.Ordinal);
        Revision = revision;
        ThreadKind = threadKind;
        EffectiveProviderId = effectiveProviderId;
        EffectiveMainModel = effectiveMainModel;
    }

    /// <summary>Gets the thread identifier.</summary>
    public string ThreadId { get; }
    /// <summary>Gets the Turn identifier when planning occurs for a Turn.</summary>
    public string? TurnId { get; }
    /// <summary>Gets the workspace root.</summary>
    public string WorkspacePath { get; }
    /// <summary>Gets the agent mode.</summary>
    public string Mode { get; }
    /// <summary>Gets the optional tool profile.</summary>
    public string? Profile { get; }
    /// <summary>Gets the provider capabilities frozen for this planning operation.</summary>
    public IReadOnlySet<string> ProviderCapabilities { get; }
    /// <summary>Gets the snapshot revision to create.</summary>
    public long Revision { get; }
    /// <summary>Gets the trusted thread classification frozen for this planning operation.</summary>
    public ToolPlanningThreadKind ThreadKind { get; }
    /// <summary>Gets the provider snapshot effective for the current thread.</summary>
    public string? EffectiveProviderId { get; }
    /// <summary>Gets the MainAgent model snapshot effective for the current thread.</summary>
    public string? EffectiveMainModel { get; }
}

/// <summary>Identifies the trusted host surface that initiated a direct tool invocation.</summary>
public sealed record ToolInvocationOrigin
{
    /// <summary>Creates an invocation origin.</summary>
    /// <param name="kind">A stable origin kind such as <c>mcpApp</c>.</param>
    /// <param name="sourceItemId">An optional safe Session item correlation identifier.</param>
    public ToolInvocationOrigin(string kind, string? sourceItemId = null)
    {
        if (string.IsNullOrWhiteSpace(kind))
            throw new ArgumentException("An invocation origin kind is required.", nameof(kind));
        Kind = kind;
        SourceItemId = sourceItemId;
    }

    /// <summary>Gets the stable origin kind.</summary>
    public string Kind { get; }

    /// <summary>Gets the optional safe Session item correlation identifier.</summary>
    public string? SourceItemId { get; }
}

/// <summary>Caller metadata supplied before the dispatcher resolves a tool.</summary>
public sealed record ToolInvocationRequest(
    string ThreadId,
    string? TurnId,
    string CallId,
    ToolInvocationAudience Audience,
    ToolInvocationOrigin? Origin = null,
    string? WorkspacePath = null);

/// <summary>Resolved immutable invocation metadata supplied to a tool runtime.</summary>
public sealed record ToolInvocationContext(
    string ThreadId,
    string? TurnId,
    string CallId,
    ToolInvocationAudience Audience,
    ToolName ToolName,
    ToolDefinitionId DefinitionId,
    RuntimeBindingId RuntimeBindingId,
    long SnapshotRevision,
    DateTimeOffset StartedAt,
    ToolInvocationOrigin? Origin = null,
    string? WorkspacePath = null);

/// <summary>A stable source-neutral tool execution error.</summary>
public sealed class ToolError
{
    /// <summary>Creates an error with an English fallback message.</summary>
    public ToolError(
        string code,
        string message,
        IReadOnlyDictionary<string, JsonElement>? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("A stable error code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("An English fallback error message is required.", nameof(message));
        Code = code;
        Message = message;
        Parameters = JsonCollections.Clone(parameters);
    }

    /// <summary>Gets the stable machine-readable error code.</summary>
    public string Code { get; }
    /// <summary>Gets the English fallback error message.</summary>
    public string Message { get; }
    /// <summary>Gets immutable structured error parameters.</summary>
    public IReadOnlyDictionary<string, JsonElement> Parameters { get; }
}

/// <summary>Stable error codes produced by the common dispatcher.</summary>
public static class ToolErrorCodes
{
    /// <summary>No matching registration exists in the snapshot.</summary>
    public const string NotFound = "tool_not_found";
    /// <summary>The runtime is absent, disconnected, revoked, or otherwise unavailable.</summary>
    public const string Unavailable = "tool_unavailable";
    /// <summary>The requested invocation audience is not authorized.</summary>
    public const string Unauthorized = "tool_unauthorized";
    /// <summary>The invocation arguments do not satisfy the definition schema.</summary>
    public const string InputInvalid = "tool_input_invalid";
    /// <summary>The required approval was declined or cancelled.</summary>
    public const string ApprovalRejected = "tool_approval_rejected";
    /// <summary>A server-authoritative workspace or blacklist guard denied the operation.</summary>
    public const string AccessDenied = "tool_access_denied";
    /// <summary>The invocation exceeded its configured deadline.</summary>
    public const string Timeout = "tool_timeout";
    /// <summary>The runtime failed while executing the tool.</summary>
    public const string ExecutionFailed = "tool_execution_failed";
    /// <summary>The caller cancelled the invocation.</summary>
    public const string Cancelled = "tool_cancelled";
    /// <summary>The runtime returned a result that violates the common contract.</summary>
    public const string ResultInvalid = "tool_result_invalid";
    /// <summary>A Runtime Dynamic client disconnected before completing the call.</summary>
    public const string DynamicDisconnected = "dynamic_tool_disconnected";
    /// <summary>A Runtime Dynamic client returned an invalid protocol response.</summary>
    public const string DynamicProtocolError = "dynamic_tool_protocol_error";
    /// <summary>An MCP server requires authentication or reauthentication.</summary>
    public const string McpReauthenticationRequired = "mcp_reauthentication_required";
    /// <summary>An MCP server returned an invalid protocol response.</summary>
    public const string McpProtocolError = "mcp_protocol_error";
}

/// <summary>A source-neutral result with explicit model, client, and host audiences.</summary>
public sealed class ToolExecutionResult
{
    /// <summary>Creates an execution result.</summary>
    public ToolExecutionResult(
        bool success,
        string? content,
        JsonElement? structuredContent = null,
        JsonElement? meta = null,
        JsonElement? rawSourceResult = null,
        ToolError? error = null,
        object? providerResult = null,
        IReadOnlyList<AIContent>? contentItems = null)
    {
        Success = success;
        Content = content;
        StructuredContent = structuredContent?.Clone();
        Meta = meta?.Clone();
        RawSourceResult = rawSourceResult?.Clone();
        Error = error;
        ProviderResult = providerResult;
        ContentItems = contentItems is { Count: > 0 } ? contentItems.ToArray() : null;
    }

    /// <summary>Gets whether execution succeeded.</summary>
    public bool Success { get; }
    /// <summary>Gets model-visible text content.</summary>
    public string? Content { get; }
    /// <summary>Gets client-only structured content.</summary>
    public JsonElement? StructuredContent { get; }
    /// <summary>Gets host-private metadata.</summary>
    public JsonElement? Meta { get; }
    /// <summary>Gets an optional raw source result retained for specialized projection.</summary>
    public JsonElement? RawSourceResult { get; }
    /// <summary>Gets the stable error when execution failed.</summary>
    public ToolError? Error { get; }
    /// <summary>Gets an optional transient provider-native result. It is never persisted or exposed to clients.</summary>
    public object? ProviderResult { get; }
    /// <summary>Gets optional model-safe rich content preserved for model history and client projection.</summary>
    public IReadOnlyList<AIContent>? ContentItems { get; }

    /// <summary>Creates a successful result.</summary>
    public static ToolExecutionResult Succeeded(
        string? content,
        JsonElement? structuredContent = null,
        JsonElement? meta = null,
        JsonElement? rawSourceResult = null,
        object? providerResult = null,
        IReadOnlyList<AIContent>? contentItems = null) =>
        new(true, content, structuredContent, meta, rawSourceResult, providerResult: providerResult, contentItems: contentItems);

    /// <summary>Creates a failed result.</summary>
    public static ToolExecutionResult Failed(
        ToolError error,
        string? content = null,
        IReadOnlyList<AIContent>? contentItems = null) =>
        new(
            false,
            content,
            error: error ?? throw new ArgumentNullException(nameof(error)),
            contentItems: contentItems);
}

/// <summary>The outcome of checking a binding's current live lease.</summary>
public sealed record ToolBindingLeaseResult(bool IsAvailable, ToolError? Error = null)
{
    /// <summary>A reusable available result.</summary>
    public static ToolBindingLeaseResult Available { get; } = new(true);

    /// <summary>Creates an unavailable result.</summary>
    public static ToolBindingLeaseResult Unavailable(string message) =>
        new(false, new ToolError(ToolErrorCodes.Unavailable, message));
}

/// <summary>Checks connection generation, revocation, expiry, or plugin state at dispatch time.</summary>
public interface IToolBindingLease
{
    /// <summary>Checks whether the binding may execute now.</summary>
    ValueTask<ToolBindingLeaseResult> CheckAsync(
        ToolInvocationContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>Executes source-specific behavior after common dispatch checks.</summary>
public interface IToolRuntime
{
    /// <summary>Invokes the source runtime with source-native arguments.</summary>
    ValueTask<ToolExecutionResult> InvokeAsync(
        ToolInvocationContext context,
        JsonObject arguments,
        CancellationToken cancellationToken = default);
}

/// <summary>Contributes separated definitions and runtime bindings.</summary>
public interface IToolSource
{
    /// <summary>Gets a stable identifier used for ordering and diagnostics.</summary>
    string SourceId { get; }

    /// <summary>Gets deterministic ordering priority. Lower values run first.</summary>
    int Priority => 100;

    /// <summary>Collects registrations for one immutable planning context.</summary>
    ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>Releases source-owned resources scoped to a thread.</summary>
public interface IThreadScopedToolSource
{
    /// <summary>Releases resources after a thread is archived, deleted, or disposed.</summary>
    ValueTask ReleaseThreadAsync(string threadId, CancellationToken cancellationToken = default);
}

/// <summary>A live or stub executor binding kept outside the durable definition.</summary>
public sealed class ToolRuntimeBinding
{
    /// <summary>Creates a runtime binding.</summary>
    public ToolRuntimeBinding(
        RuntimeBindingId id,
        ToolDefinitionId definitionId,
        IToolRuntime runtime,
        IToolBindingLease lease,
        string authorityReference,
        long revision,
        ToolBindingAvailability availability = ToolBindingAvailability.Available,
        TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
            throw new ArgumentException("A non-default runtime binding identifier is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(definitionId.SourceId)
            || string.IsNullOrWhiteSpace(definitionId.SourceToolId.Value))
            throw new ArgumentException("A non-default definition identifier is required.", nameof(definitionId));
        if (string.IsNullOrWhiteSpace(authorityReference))
            throw new ArgumentException("An authority reference is required.", nameof(authorityReference));
        Id = id;
        DefinitionId = definitionId;
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        Lease = lease ?? throw new ArgumentNullException(nameof(lease));
        AuthorityReference = authorityReference;
        Revision = revision;
        Availability = availability;
        Timeout = timeout;
    }

    /// <summary>Gets the binding identifier.</summary>
    public RuntimeBindingId Id { get; }
    /// <summary>Gets the referenced definition identifier.</summary>
    public ToolDefinitionId DefinitionId { get; }
    /// <summary>Gets the source-specific executor.</summary>
    public IToolRuntime Runtime { get; }
    /// <summary>Gets the live authority and lifecycle lease.</summary>
    public IToolBindingLease Lease { get; }
    /// <summary>Gets the opaque server-side authority reference.</summary>
    public string AuthorityReference { get; }
    /// <summary>Gets the source binding revision.</summary>
    public long Revision { get; }
    /// <summary>Gets the registration-time availability.</summary>
    public ToolBindingAvailability Availability { get; }
    /// <summary>Gets the source timeout applied by the common dispatcher.</summary>
    public TimeSpan? Timeout { get; }
}

/// <summary>Metadata used to make a deferred registration searchable.</summary>
/// <param name="Namespace">The deferred search namespace.</param>
/// <param name="SearchText">The source-provided searchable description.</param>
public sealed record DeferredToolDescriptor(
    string Namespace,
    string SearchText,
    string? NamespaceDescription = null);

/// <summary>Declares the Session lifecycle projection owned by a tool registration.</summary>
public enum ToolProjectionShape
{
    /// <summary>A standard ToolCall followed by one terminal ToolResult.</summary>
    StandardPair,
    /// <summary>A single MCP lifecycle item updated from started to terminal.</summary>
    McpLifecycle,
    /// <summary>A single Runtime Dynamic lifecycle item updated from started to terminal.</summary>
    DynamicLifecycle
}

/// <summary>The source-neutral planning join between a definition and runtime binding.</summary>
public sealed class ToolRegistration
{
    /// <summary>Creates a registration and verifies the definition/binding join.</summary>
    public ToolRegistration(
        ToolDefinition definition,
        ToolRuntimeBinding binding,
        ToolProjectionShape projectionShape,
        ToolExposure exposure = ToolExposure.Direct,
        ToolInvocationAudience invocationAudiences = ToolInvocationAudience.Model | ToolInvocationAudience.Host,
        DeferredToolDescriptor? deferred = null,
        string? providerFlatNameOverride = null)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Binding = binding ?? throw new ArgumentNullException(nameof(binding));
        if (definition.Id != binding.DefinitionId)
            throw new ArgumentException("The runtime binding references a different definition.", nameof(binding));
        if (exposure == ToolExposure.Deferred && deferred is null)
            throw new ArgumentException("Deferred exposure requires search metadata.", nameof(deferred));
        if (deferred is not null
            && !string.Equals(deferred.Namespace, definition.Name.Namespace, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Deferred search metadata must use the definition's canonical namespace.",
                nameof(deferred));
        }
        if (providerFlatNameOverride is not null && string.IsNullOrWhiteSpace(providerFlatNameOverride))
            throw new ArgumentException("A provider flat name override cannot be empty.", nameof(providerFlatNameOverride));
        if (providerFlatNameOverride is not null
            && (Encoding.UTF8.GetByteCount(providerFlatNameOverride) > ProviderToolProjector.MaximumNameBytes
                || providerFlatNameOverride.Any(static character => character is not (
                    >= 'a' and <= 'z'
                    or >= 'A' and <= 'Z'
                    or >= '0' and <= '9'
                    or '_'))))
        {
            throw new ArgumentException(
                "A provider flat name override must use provider-safe characters and fit the provider name limit.",
                nameof(providerFlatNameOverride));
        }

        Exposure = exposure;
        ProjectionShape = projectionShape;
        InvocationAudiences = invocationAudiences;
        Deferred = deferred;
        ProviderFlatNameOverride = providerFlatNameOverride;
    }

    /// <summary>Gets the immutable definition.</summary>
    public ToolDefinition Definition { get; }
    /// <summary>Gets the live runtime binding.</summary>
    public ToolRuntimeBinding Binding { get; }
    /// <summary>Gets the source-declared Session lifecycle projection.</summary>
    public ToolProjectionShape ProjectionShape { get; }
    /// <summary>Gets the default model exposure.</summary>
    public ToolExposure Exposure { get; }
    /// <summary>Gets permitted invocation audiences.</summary>
    public ToolInvocationAudience InvocationAudiences { get; }
    /// <summary>Gets deferred search metadata.</summary>
    public DeferredToolDescriptor? Deferred { get; }
    /// <summary>Gets an exact provider-visible name override for provider-native tool surfaces.</summary>
    public string? ProviderFlatNameOverride { get; }
}

/// <summary>Dispatches provider or host calls through a frozen effective snapshot.</summary>
public interface IToolDispatcher
{
    /// <summary>Dispatches an exact provider-visible call name.</summary>
    ValueTask<ToolExecutionResult> DispatchProviderFlatCallAsync(
        EffectiveToolSnapshot snapshot,
        string providerFlatName,
        JsonObject arguments,
        ToolInvocationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Dispatches an exact canonical tool name.</summary>
    ValueTask<ToolExecutionResult> DispatchAsync(
        EffectiveToolSnapshot snapshot,
        ToolName toolName,
        JsonObject arguments,
        ToolInvocationRequest request,
        CancellationToken cancellationToken = default);
}

internal static class JsonCollections
{
    public static IReadOnlyDictionary<string, JsonElement> Clone(
        IReadOnlyDictionary<string, JsonElement>? source) =>
        source is null || source.Count == 0
            ? FrozenDictionary<string, JsonElement>.Empty
            : source.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
}
