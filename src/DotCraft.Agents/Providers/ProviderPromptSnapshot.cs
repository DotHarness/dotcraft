namespace DotCraft.Agents;

public enum ProviderPromptSourceKind
{
    Harness,
    CallerInstructions,
    ProjectInstructions,
    Skill,
    Memory,
    Extension,
    Opaque
}

public sealed record ProviderPromptFragment(
    string SourceId, ProviderPromptSourceKind Kind, string Text, string Placement, int? MessageIndex = null);

public sealed record ProviderPromptSnapshot(IReadOnlyList<ProviderPromptFragment> Fragments);

/// <summary>Request-owned provenance collected during composition and matched against final sampling text.</summary>
public sealed class ProviderPromptSources
{
    private readonly object _gate = new();
    private readonly Dictionary<string, IReadOnlyList<ProviderPromptFragment>> _documents = new(StringComparer.Ordinal);

    public void Register(string text, IReadOnlyList<ProviderPromptFragment> fragments)
    {
        if (string.Concat(fragments.Select(fragment => fragment.Text)) != text)
            throw new ArgumentException("Prompt provenance does not reconstruct the composed text.");
        lock (_gate) _documents[text] = fragments.ToArray();
    }

    public IReadOnlyList<ProviderPromptFragment>? Find(string text)
    {
        lock (_gate) return _documents.GetValueOrDefault(text);
    }
}

public static class ProviderPromptSnapshotScope
{
    private static readonly AsyncLocal<ProviderPromptSnapshot?> Snapshot = new();
    public static ProviderPromptSnapshot? Current => Snapshot.Value;
    public static IDisposable Push(ProviderPromptSnapshot snapshot)
    {
        var previous = Snapshot.Value;
        Snapshot.Value = snapshot;
        return new Restore(previous);
    }
    private sealed class Restore(ProviderPromptSnapshot? previous) : IDisposable
    {
        public void Dispose() => Snapshot.Value = previous;
    }
}
