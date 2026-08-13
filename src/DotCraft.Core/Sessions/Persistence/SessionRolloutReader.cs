namespace DotCraft.Sessions;

/// <summary>
/// Reads a persisted thread rollout without initializing or mutating workspace storage.
/// </summary>
public sealed class SessionRolloutReader
{
    /// <summary>
    /// Replays a rollout located directly under a workspace thread storage directory.
    /// </summary>
    /// <param name="rolloutPath">Path under <c>.craft/threads/active</c> or <c>.craft/threads/archived</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reconstructed thread, or <see langword="null"/> when the file does not exist.</returns>
    public Task<SessionThread?> ReadAsync(string rolloutPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rolloutPath);
        var fullPath = Path.GetFullPath(rolloutPath);
        ValidatePath(fullPath);
        return ThreadRolloutStore.ReadThreadFileAsync(fullPath, ct);
    }

    private static void ValidatePath(string path)
    {
        var file = new FileInfo(path);
        var statusDirectory = file.Directory;
        var threadsDirectory = statusDirectory?.Parent;
        var craftDirectory = threadsDirectory?.Parent;

        if (statusDirectory == null ||
            threadsDirectory == null ||
            craftDirectory == null ||
            !string.Equals(threadsDirectory.Name, "threads", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(craftDirectory.Name, ".craft", StringComparison.OrdinalIgnoreCase) ||
            !(string.Equals(statusDirectory.Name, "active", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(statusDirectory.Name, "archived", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "Thread path must resolve directly under .craft/threads/active or .craft/threads/archived.",
                nameof(path));
        }
    }
}
