using System.Collections.Frozen;

namespace DotCraft.Tools;

/// <summary>A stable, safe diagnostic produced while building an effective snapshot.</summary>
public sealed record ToolSnapshotDiagnostic(
    string Code,
    ToolName ToolName,
    IReadOnlyList<ToolProvenance> Provenances,
    string Message);

/// <summary>Stable diagnostic codes emitted by <see cref="EffectiveToolSnapshotBuilder"/>.</summary>
public static class ToolSnapshotDiagnosticCodes
{
    /// <summary>Two or more registrations declared the same canonical tool name.</summary>
    public const string DuplicateCanonicalName = "duplicate_canonical_tool_name";
    /// <summary>Two or more registrations projected to the same provider flat name.</summary>
    public const string DuplicateProviderFlatName = "duplicate_provider_flat_name";
    /// <summary>One canonical namespace declared multiple distinct model-visible descriptions.</summary>
    public const string ConflictingNamespaceDescription = "conflicting_namespace_description";
}

/// <summary>An immutable per-Turn registry and its model/deferred projections.</summary>
public sealed class EffectiveToolSnapshot
{
    internal EffectiveToolSnapshot(
        long revision,
        FrozenDictionary<ToolName, ToolRegistration> registrations,
        FrozenDictionary<ToolName, string> providerFlatNames,
        FrozenDictionary<string, ToolName> providerFlatNameIndex,
        IReadOnlyList<ToolDefinition> modelVisibleDefinitions,
        FrozenDictionary<string, IReadOnlyList<ToolDefinition>> deferredDefinitions,
        FrozenDictionary<string, string> namespaceDescriptions,
        IReadOnlyList<ToolSnapshotDiagnostic> diagnostics,
        ProviderHostedCapabilityPlan? providerHostedCapabilities = null,
        IReadOnlyList<ToolRegistration>? sourceRegistrations = null)
    {
        Revision = revision;
        Registrations = registrations;
        ProviderFlatNames = providerFlatNames;
        ProviderFlatNameIndex = providerFlatNameIndex;
        ModelVisibleDefinitions = modelVisibleDefinitions;
        DeferredDefinitions = deferredDefinitions;
        NamespaceDescriptions = namespaceDescriptions;
        Diagnostics = diagnostics;
        ProviderHostedCapabilities = providerHostedCapabilities ?? new ProviderHostedCapabilityPlan();
        SourceRegistrations = sourceRegistrations ?? registrations.Values.ToArray();
    }

    /// <summary>Gets the immutable snapshot revision.</summary>
    public long Revision { get; }

    /// <summary>Gets every non-conflicting authorized registration, including hidden entries.</summary>
    public IReadOnlyDictionary<ToolName, ToolRegistration> Registrations { get; }

    /// <summary>Gets the exact provider-visible name for each canonical name.</summary>
    public IReadOnlyDictionary<ToolName, string> ProviderFlatNames { get; }

    /// <summary>Gets the exact canonical name for each provider-visible call name.</summary>
    public IReadOnlyDictionary<string, ToolName> ProviderFlatNameIndex { get; }

    /// <summary>Gets definitions published directly to the model.</summary>
    public IReadOnlyList<ToolDefinition> ModelVisibleDefinitions { get; }

    /// <summary>Gets deferred definitions grouped by their search namespace.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ToolDefinition>> DeferredDefinitions { get; }

    /// <summary>Gets the resolved model-visible description for each canonical namespace.</summary>
    internal IReadOnlyDictionary<string, string> NamespaceDescriptions { get; }

    /// <summary>Gets safe diagnostics for quarantined registrations.</summary>
    public IReadOnlyList<ToolSnapshotDiagnostic> Diagnostics { get; }

    /// <summary>Gets capabilities executed directly by the selected model provider.</summary>
    public ProviderHostedCapabilityPlan ProviderHostedCapabilities { get; }

    internal IReadOnlyList<ToolRegistration> SourceRegistrations { get; }

    /// <summary>Resolves a provider-visible name using ordinal, case-sensitive comparison.</summary>
    public bool TryResolveProviderFlatName(string providerFlatName, out ToolName toolName) =>
        ProviderFlatNameIndex.TryGetValue(providerFlatName, out toolName);

    /// <summary>Resolves untrusted namespace-capable provider callback identity without throwing.</summary>
    internal bool TryResolveProviderNamespacedName(string? toolNamespace, string? localName, out ToolName toolName)
    {
        if (!ToolName.TryCreate(toolNamespace, localName, out toolName))
            return false;

        return Registrations.ContainsKey(toolName);
    }

    /// <summary>
    /// Returns a snapshot with the same canonical dispatch registry and diagnostics but a
    /// policy-filtered model/deferred exposure surface.
    /// </summary>
    public EffectiveToolSnapshot WithModelExposure(Func<ToolDefinition, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new EffectiveToolSnapshotBuilder().BuildFiltered(
            SourceRegistrations,
            Revision,
            ProviderHostedCapabilities,
            predicate,
            []);
    }
}

/// <summary>Creates deterministic immutable effective snapshots from source registrations.</summary>
public sealed class EffectiveToolSnapshotBuilder
{
    /// <summary>Collects source registrations in deterministic order, before any snapshot-assembly edit.</summary>
    public async ValueTask<IReadOnlyList<ToolRegistration>> CollectAsync(
        IEnumerable<IToolSource> sources,
        ToolPlanningContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(context);

        var registrations = new List<ToolRegistration>();
        foreach (var source in sources
                     .OrderBy(source => source.Priority)
                     .ThenBy(source => source.SourceId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var contributed = await source.GetRegistrationsAsync(context, cancellationToken).ConfigureAwait(false);
            if (contributed is not null)
                registrations.AddRange(contributed);
        }

        return registrations;
    }

    /// <summary>Collects sources in deterministic order and builds one snapshot.</summary>
    public async ValueTask<EffectiveToolSnapshot> BuildAsync(
        IEnumerable<IToolSource> sources,
        ToolPlanningContext context,
        CancellationToken cancellationToken = default) =>
        Build(
            await CollectAsync(sources, context, cancellationToken).ConfigureAwait(false),
            context.Revision);

    /// <summary>Collects sources and attaches a provider-hosted capability plan.</summary>
    public async ValueTask<EffectiveToolSnapshot> BuildAsync(
        IEnumerable<IToolSource> sources,
        ToolPlanningContext context,
        ProviderHostedCapabilityPlan providerHostedCapabilities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(providerHostedCapabilities);
        return Build(
            await CollectAsync(sources, context, cancellationToken).ConfigureAwait(false),
            context.Revision,
            providerHostedCapabilities);
    }

    /// <summary>
    /// Builds a snapshot. Every registration participating in a duplicate canonical name is
    /// quarantined; deterministic ordering is never used as a conflict winner.
    /// </summary>
    public EffectiveToolSnapshot Build(
        IEnumerable<ToolRegistration> registrations,
        long revision,
        ProviderHostedCapabilityPlan? providerHostedCapabilities = null)
        => BuildFiltered(
            registrations,
            revision,
            providerHostedCapabilities,
            static _ => true,
            []);

    internal EffectiveToolSnapshot BuildFiltered(
        IEnumerable<ToolRegistration> registrations,
        long revision,
        ProviderHostedCapabilityPlan? providerHostedCapabilities,
        Func<ToolDefinition, bool> modelExposurePredicate,
        IReadOnlyList<ToolSnapshotDiagnostic> inheritedDiagnostics)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(modelExposurePredicate);

        var materialized = registrations
            .Where(static registration => !DeferredToolSearchRuntime.IsRegistration(registration))
            .ToArray();
        var groups = materialized
            .GroupBy(registration => registration.Definition.Name)
            .OrderBy(group => group.Key.Namespace, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Name, StringComparer.Ordinal)
            .ToArray();

        var accepted = new Dictionary<ToolName, ToolRegistration>();
        var diagnostics = new List<ToolSnapshotDiagnostic>(inheritedDiagnostics);

        foreach (var group in groups)
        {
            var conflicts = group.ToArray();
            if (conflicts.Length == 1)
            {
                accepted.Add(group.Key, conflicts[0]);
                continue;
            }

            var provenances = conflicts
                .Select(registration => registration.Definition.Provenance)
                .OrderBy(provenance => provenance.Kind)
                .ThenBy(provenance => provenance.SourceId, StringComparer.Ordinal)
                .ThenBy(provenance => provenance.Origin, StringComparer.Ordinal)
                .ToArray();
            diagnostics.Add(new ToolSnapshotDiagnostic(
                ToolSnapshotDiagnosticCodes.DuplicateCanonicalName,
                group.Key,
                Array.AsReadOnly(provenances),
                $"Canonical tool name '{group.Key}' is declared by multiple sources; all conflicting registrations were quarantined."));
        }

        var exposedByNamespace = accepted.Values
            .Where(registration => registration.Definition.Name.Namespace != null
                                   && registration.Binding.Availability == ToolBindingAvailability.Available
                                   && registration.Exposure is ToolExposure.Direct
                                       or ToolExposure.DirectModelOnly
                                       or ToolExposure.Deferred
                                   && modelExposurePredicate(registration.Definition))
            .GroupBy(registration => registration.Definition.Name.Namespace!, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        var namespaceDescriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var namespaceGroup in exposedByNamespace)
        {
            var description = ToolNamespaceDescriptionResolver.Resolve(
                namespaceGroup.Key,
                namespaceGroup.Select(registration => registration.Definition.NamespaceDescription),
                out var hasConflict);
            namespaceDescriptions[namespaceGroup.Key] = description;
            if (!hasConflict)
                continue;

            var registrationsInNamespace = namespaceGroup
                .OrderBy(registration => registration.Definition.Name.Name, StringComparer.Ordinal)
                .ToArray();
            diagnostics.Add(new ToolSnapshotDiagnostic(
                ToolSnapshotDiagnosticCodes.ConflictingNamespaceDescription,
                registrationsInNamespace[0].Definition.Name,
                Array.AsReadOnly(registrationsInNamespace
                    .Select(registration => registration.Definition.Provenance)
                    .ToArray()),
                $"Canonical namespace '{namespaceGroup.Key}' declares conflicting descriptions; provider projection uses the generic namespace description."));
        }

        var deferred = accepted.Values
            .Where(registration => registration.Binding.Availability == ToolBindingAvailability.Available
                                   && registration.Exposure == ToolExposure.Deferred
                                   && modelExposurePredicate(registration.Definition))
            .GroupBy(registration => registration.Definition.Name.Namespace!, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToFrozenDictionary(
                group => group.Key,
                group => (IReadOnlyList<ToolDefinition>)Array.AsReadOnly(
                    group.Select(registration => registration.Definition)
                        .OrderBy(definition => definition.Name.Name, StringComparer.Ordinal)
                        .ToArray()),
                StringComparer.Ordinal);

        if (deferred.Count > 0 && providerHostedCapabilities?.DeferredToolSearch is { } searchPlan)
        {
            var search = DeferredToolSearchRuntime.CreateRegistration(
                deferred,
                accepted,
                namespaceDescriptions,
                revision,
                searchPlan);
            if (accepted.TryGetValue(search.Definition.Name, out var conflict))
            {
                accepted.Remove(search.Definition.Name);
                diagnostics.Add(new ToolSnapshotDiagnostic(
                    ToolSnapshotDiagnosticCodes.DuplicateCanonicalName,
                    search.Definition.Name,
                    Array.AsReadOnly(new[] { conflict.Definition.Provenance, search.Definition.Provenance }),
                    $"Canonical tool name '{search.Definition.Name}' is declared by multiple sources; all conflicting registrations were quarantined."));
            }
            else
            {
                accepted.Add(search.Definition.Name, search);
            }
        }

        var canonicalNames = accepted.Keys
            .OrderBy(name => name.Namespace, StringComparer.Ordinal)
            .ThenBy(name => name.Name, StringComparer.Ordinal)
            .ToArray();
        var projectedNames = ProviderToolProjector.Project(canonicalNames).ToDictionary();
        foreach (var name in canonicalNames)
        {
            if (accepted[name].ProviderFlatNameOverride is { } providerFlatName)
                projectedNames[name] = providerFlatName;
        }

        var providerConflicts = projectedNames
            .GroupBy(static pair => pair.Value, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .ToArray();
        foreach (var conflict in providerConflicts)
        {
            var names = conflict.Select(static pair => pair.Key).ToArray();
            var provenances = names.Select(name => accepted[name].Definition.Provenance).ToArray();
            foreach (var name in names)
            {
                accepted.Remove(name);
                projectedNames.Remove(name);
            }
            diagnostics.Add(new ToolSnapshotDiagnostic(
                ToolSnapshotDiagnosticCodes.DuplicateProviderFlatName,
                names[0],
                Array.AsReadOnly(provenances),
                $"Provider call name '{conflict.Key}' is declared by multiple tools; all conflicting registrations were quarantined."));
        }

        var reverseNames = projectedNames.ToFrozenDictionary(
            pair => pair.Value,
            pair => pair.Key,
            StringComparer.Ordinal);

        var finalCanonicalNames = accepted.Keys
            .OrderBy(name => name.Namespace, StringComparer.Ordinal)
            .ThenBy(name => name.Name, StringComparer.Ordinal)
            .ToArray();
        var modelVisible = finalCanonicalNames
            .Select(name => accepted[name])
            .Where(registration => registration.Binding.Availability == ToolBindingAvailability.Available
                                   && (registration.Exposure is ToolExposure.Direct or ToolExposure.DirectModelOnly)
                                   && modelExposurePredicate(registration.Definition))
            .Select(registration => registration.Definition)
            .ToArray();

        return new EffectiveToolSnapshot(
            revision,
            accepted.ToFrozenDictionary(),
            projectedNames.ToFrozenDictionary(),
            reverseNames,
            Array.AsReadOnly(modelVisible),
            deferred,
            namespaceDescriptions.ToFrozenDictionary(StringComparer.Ordinal),
            Array.AsReadOnly(diagnostics.ToArray()),
            providerHostedCapabilities,
            Array.AsReadOnly(materialized));
    }
}
