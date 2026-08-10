using System.Security.Cryptography;

namespace DotCraft.Sessions;

public sealed partial class ThreadStore
{
    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string NormalizeRecoveryPackagePath(string packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
            throw RecoveryFailure(ThreadRecoveryErrorCodes.PackageInvalid, "Recovery package path is required.");
        var stagingDirectory = GetRecoveryStagingDirectory();
        var normalized = Path.GetFullPath(packagePath);
        if (!string.Equals(Path.GetDirectoryName(normalized), stagingDirectory, PathComparison)
            || !File.Exists(normalized))
        {
            throw RecoveryFailure(
                ThreadRecoveryErrorCodes.PackageInvalid,
                "Recovery packages must be direct files under workspace recovery staging.");
        }
        EnsureNoReparsePoints(normalized, includeLeaf: true);
        return normalized;
    }

    private string GetRecoveryStagingDirectory() => Path.Combine(_botPath, "recovery-staging");

    private void EnsureCraftPathMatchesWorkspace(string normalizedWorkspace)
    {
        var expected = Path.GetFullPath(Path.Combine(normalizedWorkspace, ".craft"));
        if (!string.Equals(expected, _botPath, PathComparison))
            throw RecoveryFailure(ThreadRecoveryErrorCodes.WorkspaceMismatch, "Recovery workspace does not own this .craft directory.");
    }

    private void EnsureNoReparsePoints(string fullPath, bool includeLeaf)
    {
        var relative = Path.GetRelativePath(_botPath, fullPath);
        if (relative == ".")
            return;
        var segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        var current = _botPath;
        var count = includeLeaf ? segments.Length : Math.Max(0, segments.Length - 1);
        for (var index = 0; index < count; index++)
        {
            current = Path.Combine(current, segments[index]);
            if (!File.Exists(current) && !Directory.Exists(current))
                continue;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw RecoveryFailure(ThreadRecoveryErrorCodes.PackageInvalid, "Recovery path contains a symbolic link or reparse point.");
        }
    }

    private void CleanupStaleRecoveryStaging(string? exceptPath = null)
    {
        var staging = GetRecoveryStagingDirectory();
        if (!Directory.Exists(staging))
            return;

        var threshold = DateTimeOffset.UtcNow - RecoveryStagingRetention;
        foreach (var path in Directory.EnumerateFiles(staging, "*", SearchOption.TopDirectoryOnly))
        {
            if (exceptPath != null && string.Equals(Path.GetFullPath(path), exceptPath, PathComparison))
                continue;
            try
            {
                if (File.GetLastWriteTimeUtc(path) < threshold.UtcDateTime)
                    File.Delete(path);
            }
            catch
            {
                // Staging retention is best-effort and must not block recovery.
            }
        }

        foreach (var path in Directory.EnumerateDirectories(staging, ".restore-*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(path) < threshold.UtcDateTime)
                    Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Staging retention is best-effort and must not block recovery.
            }
        }
    }

    private static string NormalizeWorkspace(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
            throw RecoveryFailure(ThreadRecoveryErrorCodes.WorkspaceMismatch, "Recovery workspace path is required.");
        return Path.GetFullPath(workspacePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                PathComparison);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsTerminal(TurnStatus status) =>
        status is TurnStatus.Completed or TurnStatus.Failed or TurnStatus.Cancelled;

    private static ThreadRecoveryException RecoveryFailure(
        string code,
        string message,
        Exception? innerException = null) =>
        new(code, message, innerException);

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Rollback and staging cleanup are best-effort.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Staging cleanup is best-effort.
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed record ValidatedRecoveryPackage(
        SessionThread Thread,
        ThreadRecoverySnapshot Snapshot,
        string RolloutPath);

    private sealed class ThreadRecoverySnapshot
    {
        public int FormatVersion { get; init; }

        public ThreadRecoveryHeaderSnapshot Thread { get; init; } = new();

        public ThreadRecoveryTerminalTurnSnapshot TerminalTurn { get; init; } = new();

        public int TurnSequenceHighWatermark { get; init; }

        public List<ModelHistoryMessage> ModelHistory { get; init; } = [];

        public ThreadRecoveryProviderSnapshot ProviderHistory { get; init; } = new();
    }

    private sealed class ThreadRecoveryHeaderSnapshot
    {
        public string ThreadId { get; init; } = string.Empty;

        public string WorkspacePath { get; init; } = string.Empty;

        public string? UserId { get; init; }

        public string OriginChannel { get; init; } = string.Empty;

        public string? ChannelContext { get; init; }

        public PersistedThreadSource? Source { get; init; }

        public ThreadWorktreeInfo? Worktree { get; init; }

        public Dictionary<string, string> Metadata { get; init; } = [];

        public ThreadConfiguration? Configuration { get; init; }
    }

    private sealed class ThreadRecoveryTerminalTurnSnapshot
    {
        public string TurnId { get; init; } = string.Empty;

        public TurnStatus Status { get; init; }
    }

    private sealed class ThreadRecoveryProviderSnapshot
    {
        public int SchemaVersion { get; init; }

        public string GenerationId { get; init; } = string.Empty;

        public string ContextWindowId { get; init; } = string.Empty;

        public bool IsNativeCompacted { get; init; }

        public List<ProviderHistoryEntry> Entries { get; init; } = [];
    }

}
