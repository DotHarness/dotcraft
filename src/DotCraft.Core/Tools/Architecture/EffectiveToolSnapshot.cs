using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;

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
}

/// <summary>An immutable per-Turn registry and its model/deferred projections.</summary>
public sealed class EffectiveToolSnapshot
{
    internal EffectiveToolSnapshot(
        long revision,
        FrozenDictionary<ToolName, ToolRegistration> registrations,
        FrozenDictionary<ToolName, string> providerCallNames,
        FrozenDictionary<string, ToolName> providerCallNameIndex,
        IReadOnlyList<ToolDefinition> modelVisibleDefinitions,
        FrozenDictionary<string, IReadOnlyList<ToolDefinition>> deferredDefinitions,
        IReadOnlyList<ToolSnapshotDiagnostic> diagnostics,
        ProviderHostedCapabilityPlan? providerHostedCapabilities = null)
    {
        Revision = revision;
        Registrations = registrations;
        ProviderCallNames = providerCallNames;
        ProviderCallNameIndex = providerCallNameIndex;
        ModelVisibleDefinitions = modelVisibleDefinitions;
        DeferredDefinitions = deferredDefinitions;
        Diagnostics = diagnostics;
        ProviderHostedCapabilities = providerHostedCapabilities ?? new ProviderHostedCapabilityPlan();
    }

    /// <summary>Gets the immutable snapshot revision.</summary>
    public long Revision { get; }

    /// <summary>Gets every non-conflicting authorized registration, including hidden entries.</summary>
    public IReadOnlyDictionary<ToolName, ToolRegistration> Registrations { get; }

    /// <summary>Gets the exact provider-visible name for each canonical name.</summary>
    public IReadOnlyDictionary<ToolName, string> ProviderCallNames { get; }

    /// <summary>Gets the exact canonical name for each provider-visible call name.</summary>
    public IReadOnlyDictionary<string, ToolName> ProviderCallNameIndex { get; }

    /// <summary>Gets definitions published directly to the model.</summary>
    public IReadOnlyList<ToolDefinition> ModelVisibleDefinitions { get; }

    /// <summary>Gets deferred definitions grouped by their search namespace.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ToolDefinition>> DeferredDefinitions { get; }

    /// <summary>Gets safe diagnostics for quarantined registrations.</summary>
    public IReadOnlyList<ToolSnapshotDiagnostic> Diagnostics { get; }

    /// <summary>Gets capabilities executed directly by the selected model provider.</summary>
    public ProviderHostedCapabilityPlan ProviderHostedCapabilities { get; }

    /// <summary>Resolves a provider-visible name using ordinal, case-sensitive comparison.</summary>
    public bool TryResolveProviderCallName(string providerCallName, out ToolName toolName) =>
        ProviderCallNameIndex.TryGetValue(providerCallName, out toolName);

    /// <summary>
    /// Returns a snapshot with the same canonical dispatch registry and diagnostics but a
    /// policy-filtered model/deferred exposure surface.
    /// </summary>
    public EffectiveToolSnapshot WithModelExposure(Func<ToolDefinition, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var direct = ModelVisibleDefinitions.Where(predicate).ToArray();
        var deferred = DeferredDefinitions
            .Select(pair => new KeyValuePair<string, IReadOnlyList<ToolDefinition>>(
                pair.Key,
                Array.AsReadOnly(pair.Value.Where(predicate).ToArray())))
            .Where(pair => pair.Value.Count > 0)
            .ToFrozenDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return new EffectiveToolSnapshot(
            Revision,
            (FrozenDictionary<ToolName, ToolRegistration>)Registrations,
            (FrozenDictionary<ToolName, string>)ProviderCallNames,
            (FrozenDictionary<string, ToolName>)ProviderCallNameIndex,
            Array.AsReadOnly(direct),
            deferred,
            Diagnostics,
            ProviderHostedCapabilities);
    }
}

/// <summary>Creates deterministic immutable effective snapshots from source registrations.</summary>
public sealed class EffectiveToolSnapshotBuilder
{
    /// <summary>Collects sources in deterministic order and builds one snapshot.</summary>
    public async ValueTask<EffectiveToolSnapshot> BuildAsync(
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

        return Build(registrations, context.Revision);
    }

    /// <summary>Collects sources and attaches a provider-hosted capability plan.</summary>
    public async ValueTask<EffectiveToolSnapshot> BuildAsync(
        IEnumerable<IToolSource> sources,
        ToolPlanningContext context,
        ProviderHostedCapabilityPlan providerHostedCapabilities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(providerHostedCapabilities);
        var snapshot = await BuildAsync(sources, context, cancellationToken).ConfigureAwait(false);
        return Build(snapshot.Registrations.Values, snapshot.Revision, providerHostedCapabilities);
    }

    /// <summary>
    /// Builds a snapshot. Every registration participating in a duplicate canonical name is
    /// quarantined; deterministic ordering is never used as a conflict winner.
    /// </summary>
    public EffectiveToolSnapshot Build(
        IEnumerable<ToolRegistration> registrations,
        long revision,
        ProviderHostedCapabilityPlan? providerHostedCapabilities = null)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        var materialized = registrations.ToArray();
        var groups = materialized
            .GroupBy(registration => registration.Definition.Name)
            .OrderBy(group => group.Key.Namespace, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Name, StringComparer.Ordinal)
            .ToArray();

        var accepted = new Dictionary<ToolName, ToolRegistration>();
        var diagnostics = new List<ToolSnapshotDiagnostic>();

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

        var canonicalNames = accepted.Keys
            .OrderBy(name => name.Namespace, StringComparer.Ordinal)
            .ThenBy(name => name.Name, StringComparer.Ordinal)
            .ToArray();
        var projectedNames = ProviderToolProjector.Project(canonicalNames);
        var reverseNames = projectedNames.ToFrozenDictionary(
            pair => pair.Value,
            pair => pair.Key,
            StringComparer.Ordinal);

        var modelVisible = canonicalNames
            .Select(name => accepted[name])
            .Where(registration => registration.Binding.Availability == ToolBindingAvailability.Available
                                   && (registration.Exposure is ToolExposure.Direct or ToolExposure.DirectModelOnly))
            .Select(registration => registration.Definition)
            .ToArray();

        var deferred = canonicalNames
            .Select(name => accepted[name])
            .Where(registration => registration.Binding.Availability == ToolBindingAvailability.Available
                                   && registration.Exposure == ToolExposure.Deferred)
            .GroupBy(registration => registration.Deferred!.Namespace, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToFrozenDictionary(
                group => group.Key,
                group => (IReadOnlyList<ToolDefinition>)Array.AsReadOnly(
                    group.Select(registration => registration.Definition)
                        .OrderBy(definition => definition.Name.Name, StringComparer.Ordinal)
                        .ToArray()),
                StringComparer.Ordinal);

        return new EffectiveToolSnapshot(
            revision,
            accepted.ToFrozenDictionary(),
            projectedNames.ToFrozenDictionary(),
            reverseNames,
            Array.AsReadOnly(modelVisible),
            deferred,
            Array.AsReadOnly(diagnostics.ToArray()),
            providerHostedCapabilities);
    }
}

/// <summary>Projects canonical identities to deterministic provider-safe flat call names.</summary>
public static class ProviderToolProjector
{
    /// <summary>The maximum UTF-8 byte length of a projected provider call name.</summary>
    public const int MaximumNameBytes = 64;

    /// <summary>
    /// Projects a complete name set. Sanitization collisions receive deterministic SHA-1 suffixes
    /// so that reverse lookup never depends on string parsing.
    /// </summary>
    public static IReadOnlyDictionary<ToolName, string> Project(IReadOnlyCollection<ToolName> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Count != names.Distinct().Count())
            throw new ArgumentException("Canonical names must be unique before provider projection.", nameof(names));

        var candidates = names.ToDictionary(name => name, CreateCandidate);
        var collisionNames = candidates
            .GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group.Select(pair => pair.Key))
            .ToHashSet();

        return names
            .OrderBy(name => name.Namespace, StringComparer.Ordinal)
            .ThenBy(name => name.Name, StringComparer.Ordinal)
            .ToFrozenDictionary(
                name => name,
                name => collisionNames.Contains(name) || Encoding.UTF8.GetByteCount(candidates[name]) > MaximumNameBytes
                    ? AppendIdentityHash(candidates[name], name)
                    : candidates[name]);
    }

    private static string CreateCandidate(ToolName name)
    {
        var raw = name.Namespace is null ? name.Name : $"{name.Namespace}__{name.Name}";
        var builder = new StringBuilder(raw.Length);
        foreach (var c in raw)
            builder.Append(IsProviderNameCharacter(c) ? c : '_');
        return builder.Length == 0 ? "_" : builder.ToString();
    }

    private static bool IsProviderNameCharacter(char value) =>
        value is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '_'
            or '-';

    private static string AppendIdentityHash(string candidate, ToolName name)
    {
        var identity = $"{name.Namespace}\0{name.Name}";
        var hash = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(identity)))[..12];
        const int suffixLength = 13;
        var prefixLength = Math.Min(candidate.Length, MaximumNameBytes - suffixLength);
        return $"{candidate[..prefixLength]}_{hash}";
    }
}
