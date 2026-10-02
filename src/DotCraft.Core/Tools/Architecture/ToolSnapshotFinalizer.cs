namespace DotCraft.Tools;

/// <summary>Changes a thread's otherwise final effective snapshot.</summary>
public interface IToolSnapshotFinalizer
{
    ValueTask<ToolSnapshotFinalization> FinalizeAsync(
        EffectiveToolSnapshot snapshot,
        ToolPlanningContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>Registrations to add or replace by canonical name, and names to withhold from the model tool list.</summary>
public sealed record ToolSnapshotFinalization(
    IReadOnlyList<ToolRegistration> Registrations,
    IReadOnlySet<ToolName> ModelHidden)
{
    public static ToolSnapshotFinalization None { get; } = new([], new HashSet<ToolName>());

    public bool IsEmpty => Registrations.Count == 0 && ModelHidden.Count == 0;
}
