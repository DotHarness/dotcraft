using System.Text.Json;
using DotCraft.Contributions;
using Microsoft.Extensions.Logging;

namespace DotCraft.Tools;

/// <summary>Masks or rewrites tools a scope inherits, without owning a source.</summary>
public interface IToolRestriction : IContributionContract
{
    /// <summary>Gets the stable, kebab-case restriction name used in diagnostics.</summary>
    string Name { get; }

    /// <summary>Returns the edit for one registration, or <see langword="null"/> to leave it untouched.</summary>
    ToolRestrictionEdit? Restrict(ToolRestrictionContext context);
}

/// <summary>The registration a restriction is asked about, already carrying the edits of the restrictions before it.</summary>
public sealed record ToolRestrictionContext(
    ToolDefinition Definition,
    ToolExposure Exposure,
    ToolPlanningContext Planning);

/// <summary>
/// One restriction's edit of a registration. Name, identity, binding, audiences, policy hints and
/// policy scope are not editable.
/// </summary>
public sealed record ToolRestrictionEdit
{
    /// <summary>Gets whether the registration is removed from the snapshot entirely.</summary>
    public bool Mask { get; init; }

    /// <summary>Gets a replacement model-facing description.</summary>
    public string? Description { get; init; }

    /// <summary>Gets a replacement input JSON Schema; a non-object value is ignored.</summary>
    public JsonElement? InputSchema { get; init; }

    /// <summary>Gets a narrower model exposure; widening edits are ignored.</summary>
    public ToolExposure? Exposure { get; init; }
}

/// <summary>Folds the <see cref="IToolRestriction"/> contribution point over collected registrations, before snapshot assembly.</summary>
public static class ToolRestrictionApplier
{
    /// <summary>
    /// Applies the ordered restrictions. Masking removes the registration outright so the dispatcher
    /// answers <c>NotFound</c> for every audience; runtime-managed tools are exempt, and restrictions
    /// may never empty the surface.
    /// </summary>
    public static IReadOnlyList<ToolRegistration> Apply(
        IReadOnlyList<ToolRegistration> registrations,
        IReadOnlyList<IToolRestriction> restrictions,
        ToolPlanningContext planning,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(restrictions);
        ArgumentNullException.ThrowIfNull(planning);
        if (restrictions.Count == 0 || registrations.Count == 0)
            return registrations;

        var kept = new List<ToolRegistration>(registrations.Count);
        var maskedAny = false;
        var edited = false;
        foreach (var registration in registrations)
        {
            if (registration.Definition.PolicyScope == ToolPolicyScope.RuntimeManaged)
            {
                kept.Add(registration);
                continue;
            }

            var result = Restrict(registration, restrictions, planning, logger);
            if (result is null)
            {
                maskedAny = true;
                edited = true;
                continue;
            }

            edited |= !ReferenceEquals(result, registration);
            kept.Add(result);
        }

        if (!edited)
            return registrations;

        if (maskedAny && kept.Count == 0)
        {
            logger?.LogWarning(
                "Tool restrictions masked every tool of thread '{ThreadId}' and were discarded; an empty tool surface is fatal to the turn.",
                planning.ThreadId);
            return registrations;
        }

        return kept;
    }

    /// <summary>The registration as the restrictions ahead of the current one left it; a masked edit stops the fold without consulting the ones behind it.</summary>
    private readonly record struct Restricted(
        ToolDefinition Definition,
        ToolExposure Exposure,
        bool Changed,
        bool Masked);

    private static ToolRegistration? Restrict(
        ToolRegistration registration,
        IReadOnlyList<IToolRestriction> restrictions,
        ToolPlanningContext planning,
        ILogger? logger)
    {
        var folded = ContributionRead.Fold(
            restrictions,
            new Restricted(registration.Definition, registration.Exposure, false, false),
            (state, restriction) => Apply(state, restriction, registration, planning),
            (restriction, ex) => logger?.LogWarning(
                ex,
                "Tool restriction '{Restriction}' threw for '{Tool}' and was skipped.",
                SafeName(restriction),
                registration.Definition.Name));

        if (folded.Masked)
            return null;
        if (!folded.Changed)
            return registration;

        return new ToolRegistration(
            folded.Definition,
            registration.Binding,
            registration.ProjectionShape,
            folded.Exposure,
            registration.InvocationAudiences,
            registration.Deferred,
            registration.ProviderFlatNameOverride);
    }

    private static Restricted Apply(
        Restricted state,
        IToolRestriction restriction,
        ToolRegistration registration,
        ToolPlanningContext planning)
    {
        if (state.Masked)
            return state;

        var edit = restriction.Restrict(new ToolRestrictionContext(state.Definition, state.Exposure, planning));
        if (edit is null)
            return state;
        if (edit.Mask)
            return state with { Masked = true };

        var description = edit.Description is { Length: > 0 } replacement
                          && !string.Equals(replacement, state.Definition.Description, StringComparison.Ordinal)
            ? replacement
            : null;
        var schema = edit.InputSchema is { ValueKind: JsonValueKind.Object } value ? value : (JsonElement?)null;
        if (description is not null || schema is not null)
            state = state with { Definition = Rewrite(state.Definition, description, schema), Changed = true };

        if (edit.Exposure is { } requested && IsNarrower(requested, state.Exposure, registration))
            state = state with { Exposure = requested, Changed = true };

        return state;
    }

    private static ToolDefinition Rewrite(ToolDefinition definition, string? description, JsonElement? inputSchema) =>
        new(definition.Id,
            definition.Name,
            description ?? definition.Description,
            inputSchema ?? definition.InputSchema,
            definition.OutputSchema,
            definition.Annotations,
            definition.PolicyHints,
            definition.Presentation,
            definition.Provenance,
            definition.NamespaceDescription,
            definition.PolicyScope);

    private static bool IsNarrower(ToolExposure requested, ToolExposure current, ToolRegistration registration) =>
        Visibility(requested) < Visibility(current)
        && (requested != ToolExposure.Deferred || registration.Deferred is not null);

    private static int Visibility(ToolExposure exposure) => exposure switch
    {
        ToolExposure.Direct => 3,
        ToolExposure.DirectModelOnly => 2,
        ToolExposure.Deferred => 1,
        _ => 0
    };

    private static string SafeName(IToolRestriction restriction)
    {
        try
        {
            return restriction.Name;
        }
        catch
        {
            return restriction.GetType().Name;
        }
    }
}
