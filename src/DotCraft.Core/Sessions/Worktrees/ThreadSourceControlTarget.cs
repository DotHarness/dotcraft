namespace DotCraft.Sessions;

/// <summary>
/// Thread-scoped source-control write target selected by a client.
/// </summary>
public sealed record ThreadSourceControlTarget
{
    public string Provider { get; init; } = string.Empty;

    public string Changelist { get; init; } = "default";
}

/// <summary>
/// Stable metadata keys used to persist thread-scoped source-control target state.
/// </summary>
public static class ThreadSourceControlMetadata
{
    public const string ProviderKey = "sourceControl.provider";
    public const string PerforceChangelistKey = "sourceControl.perforce.changelist";

    public static ThreadSourceControlTarget DefaultPerforceTarget { get; } = new()
    {
        Provider = "perforce",
        Changelist = "default"
    };

    public static ThreadSourceControlTarget GetPerforceTarget(IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.TryGetValue(ProviderKey, out var provider)
            && !string.Equals(provider, "perforce", StringComparison.OrdinalIgnoreCase))
        {
            return DefaultPerforceTarget;
        }

        var changelist = metadata.TryGetValue(PerforceChangelistKey, out var value)
            ? NormalizePerforceChangelist(value)
            : "default";
        return new ThreadSourceControlTarget
        {
            Provider = "perforce",
            Changelist = changelist
        };
    }

    public static void ApplyPerforceTarget(IDictionary<string, string> metadata, string? changelist)
    {
        metadata[ProviderKey] = "perforce";
        metadata[PerforceChangelistKey] = NormalizePerforceChangelist(changelist);
    }

    public static void ClearSourceControlTarget(IDictionary<string, string> metadata)
    {
        foreach (var key in metadata.Keys
            .Where(key => key.StartsWith("sourceControl.", StringComparison.OrdinalIgnoreCase))
            .ToList())
        {
            metadata.Remove(key);
        }
    }

    public static string NormalizePerforceChangelist(string? changelist)
    {
        var trimmed = changelist?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)
            || string.Equals(trimmed, "default", StringComparison.OrdinalIgnoreCase))
        {
            return "default";
        }

        if (!trimmed.All(char.IsDigit))
            throw new ArgumentException("Perforce changelist must be 'default' or a numbered changelist id.");
        return trimmed;
    }
}
