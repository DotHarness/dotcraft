using System.Security.Cryptography;
using System.Text;
using DotCraft.Persistence;
using Microsoft.Data.Sqlite;

namespace DotCraft.Sessions;

internal sealed class ThreadAttachmentStore(WorkspaceStateDatabase stateRuntime, string botPath)
{
    private readonly string _attachmentsDir = Path.Combine(botPath, "attachments");
    private readonly string _workspaceRoot = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(botPath)))!;

    public IReadOnlySet<string> ReplaceThreadAttachments(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SessionThread thread)
    {
        var refs = ExtractReferences(thread).ToList();
        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM thread_attachments WHERE thread_id = $thread_id";
            delete.Parameters.AddWithValue("$thread_id", thread.Id);
            delete.ExecuteNonQuery();
        }

        foreach (var reference in refs)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO thread_attachments(
                    ref_id,
                    path,
                    thread_id,
                    turn_id,
                    item_id,
                    kind,
                    bytes,
                    created_at,
                    last_seen_at
                ) VALUES (
                    $ref_id,
                    $path,
                    $thread_id,
                    $turn_id,
                    $item_id,
                    $kind,
                    $bytes,
                    $created_at,
                    $last_seen_at
                )
                ON CONFLICT(ref_id) DO UPDATE SET
                    bytes = excluded.bytes,
                    last_seen_at = excluded.last_seen_at
                """;
            insert.Parameters.AddWithValue("$ref_id", reference.RefId);
            insert.Parameters.AddWithValue("$path", reference.Path);
            insert.Parameters.AddWithValue("$thread_id", reference.ThreadId);
            insert.Parameters.AddWithValue("$turn_id", (object?)reference.TurnId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$item_id", (object?)reference.ItemId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$kind", reference.Kind);
            insert.Parameters.AddWithValue("$bytes", (object?)reference.Bytes ?? DBNull.Value);
            insert.Parameters.AddWithValue("$created_at", reference.CreatedAt.UtcDateTime.ToString("O"));
            insert.Parameters.AddWithValue("$last_seen_at", reference.LastSeenAt.UtcDateTime.ToString("O"));
            insert.ExecuteNonQuery();
        }

        return refs.Select(static reference => reference.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlySet<string> LoadCandidatePaths(
        string threadId,
        IEnumerable<string>? additionalCandidatePaths = null)
    {
        var candidatePaths = LoadPathsForThread(threadId);
        if (additionalCandidatePaths != null)
            candidatePaths.UnionWith(additionalCandidatePaths.Where(IsManagedPath));
        return candidatePaths;
    }

    public void CleanupCandidates(IEnumerable<string> candidatePaths) =>
        CleanupUnreferencedPaths(candidatePaths);

    public void CleanupUnreferencedAttachments(TimeSpan minAge)
    {
        if (!Directory.Exists(_attachmentsDir))
            return;

        var threshold = DateTimeOffset.UtcNow - minAge;
        var candidates = Directory.EnumerateFiles(_attachmentsDir, "*", SearchOption.AllDirectories)
            .Where(path =>
            {
                try
                {
                    return File.GetLastWriteTimeUtc(path) <= threshold.UtcDateTime;
                }
                catch
                {
                    return false;
                }
            });
        CleanupUnreferencedPaths(candidates);
    }

    public IReadOnlySet<string> ExtractManagedPaths(SessionThread thread) =>
        ExtractReferences(thread)
            .Select(r => r.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private HashSet<string> LoadPathsForThread(string threadId)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT path FROM thread_attachments WHERE thread_id = $thread_id";
        command.Parameters.AddWithValue("$thread_id", threadId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            paths.Add(reader.GetString(0));
        return paths;
    }

    private void CleanupUnreferencedPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!IsManagedPath(path) || HasAnyReference(path))
                continue;

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                RemoveEmptyFolder(Path.GetDirectoryName(path));
            }
            catch
            {
                // Best-effort attachment cleanup must not block thread lifecycle.
            }
        }
    }

    private void RemoveEmptyFolder(string? folder)
    {
        if (folder == null || !IsManagedPath(folder) || Directory.EnumerateFileSystemEntries(folder).Any())
            return;
        Directory.Delete(folder);
    }

    private bool HasAnyReference(string path)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM thread_attachments WHERE path = $path LIMIT 1";
        command.Parameters.AddWithValue("$path", path);
        return command.ExecuteScalar() != null;
    }

    private IEnumerable<ThreadAttachmentReference> ExtractReferences(SessionThread thread)
    {
        foreach (var turn in thread.Turns)
        {
            foreach (var item in turn.Items)
            {
                if (item.Type != ItemType.UserMessage || item.AsUserMessage is not { } user)
                    continue;

                foreach (var path in ExtractManagedPaths(user.NativeInputParts))
                    yield return CreateReference(thread.Id, turn.Id, item.Id, path, item.CreatedAt);
                foreach (var path in ExtractManagedPaths(user.MaterializedInputParts))
                    yield return CreateReference(thread.Id, turn.Id, item.Id, path, item.CreatedAt);
            }
        }

        foreach (var queued in thread.QueuedInputs)
        {
            foreach (var path in ExtractManagedPaths(queued.NativeInputParts))
                yield return CreateReference(thread.Id, queued.Id, null, path, queued.CreatedAt);
            foreach (var path in ExtractManagedPaths(queued.MaterializedInputParts))
                yield return CreateReference(thread.Id, queued.Id, null, path, queued.CreatedAt);
        }
    }

    private IEnumerable<string> ExtractManagedPaths(IEnumerable<SessionInputPart>? parts)
    {
        if (parts == null)
            yield break;

        foreach (var part in parts)
        {
            string?[] paths = part.Type switch
            {
                "contextRef" => [part.Context?.Image?.TempPath, part.Context?.Path],
                "localImage" or "fileRef" => [part.Path],
                _ => []
            };
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                var fullPath = Path.GetFullPath(path, _workspaceRoot);
                if (IsManagedPath(fullPath))
                    yield return fullPath;
            }
        }
    }

    private ThreadAttachmentReference CreateReference(
        string threadId,
        string? turnId,
        string? itemId,
        string path,
        DateTimeOffset createdAt)
    {
        var bytes = TryGetLength(path);
        var source = $"{threadId}\n{turnId}\n{itemId}\n{path}";
        var refId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        return new ThreadAttachmentReference(
            refId,
            path,
            threadId,
            turnId,
            itemId,
            "attachment",
            bytes,
            createdAt == default ? now : createdAt,
            now);
    }

    private bool IsManagedPath(string path)
    {
        try
        {
            var fullRoot = Path.GetFullPath(_attachmentsDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = Path.GetFullPath(path);
            return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static long? TryGetLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : null;
        }
        catch
        {
            return null;
        }
    }

    private sealed record ThreadAttachmentReference(
        string RefId,
        string Path,
        string ThreadId,
        string? TurnId,
        string? ItemId,
        string Kind,
        long? Bytes,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastSeenAt);
}
