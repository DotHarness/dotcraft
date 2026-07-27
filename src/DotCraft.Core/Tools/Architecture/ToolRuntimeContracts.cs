using System.Text;
using System.Text.Json.Nodes;

namespace DotCraft.Tools;

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
