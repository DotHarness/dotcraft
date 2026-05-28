using System.Security.Cryptography;
using System.Text;
using DotCraft.State;

namespace DotCraft.Protocol;

internal sealed class ThreadAttachmentStore(StateRuntime stateRuntime, string botPath)
{
    private static readonly TimeSpan DraftAttachmentTtl = TimeSpan.FromHours(24);
    private readonly string _attachmentsImageDir = Path.Combine(botPath, "attachments", "images");

    public void RebuildFromThreads(IEnumerable<SessionThread> threads)
    {
        using var connection = stateRuntime.OpenConnection();
        using (var delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM thread_attachments";
            delete.ExecuteNonQuery();
        }

        foreach (var thread in threads)
            ReplaceThreadAttachments(thread, cleanupRemoved: false);

        CleanupUnreferencedAttachments(DraftAttachmentTtl);
    }

    public void ReplaceThreadAttachments(SessionThread thread, bool cleanupRemoved = true)
    {
        var previousPaths = cleanupRemoved
            ? LoadPathsForThread(thread.Id)
            : [];
        var refs = ExtractReferences(thread).ToList();

        using var connection = stateRuntime.OpenConnection();
        using (var delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM thread_attachments WHERE thread_id = $thread_id";
            delete.Parameters.AddWithValue("$thread_id", thread.Id);
            delete.ExecuteNonQuery();
        }

        foreach (var reference in refs)
        {
            using var insert = connection.CreateCommand();
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

        if (cleanupRemoved)
            CleanupUnreferencedPaths(previousPaths.Except(refs.Select(r => r.Path), StringComparer.OrdinalIgnoreCase));
    }

    public void DeleteThreadReferencesAndCleanup(string threadId, IEnumerable<string>? additionalCandidatePaths = null)
    {
        var candidatePaths = LoadPathsForThread(threadId);
        if (additionalCandidatePaths != null)
            candidatePaths.UnionWith(additionalCandidatePaths.Where(IsManagedImagePath));

        using var connection = stateRuntime.OpenConnection();
        using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM thread_attachments WHERE thread_id = $thread_id";
        delete.Parameters.AddWithValue("$thread_id", threadId);
        delete.ExecuteNonQuery();

        CleanupUnreferencedPaths(candidatePaths);
    }

    public void CleanupUnreferencedAttachments(TimeSpan minAge)
    {
        if (!Directory.Exists(_attachmentsImageDir))
            return;

        var threshold = DateTimeOffset.UtcNow - minAge;
        var candidates = Directory.EnumerateFiles(_attachmentsImageDir, "*", SearchOption.TopDirectoryOnly)
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

    public IReadOnlySet<string> ExtractManagedImagePaths(SessionThread thread) =>
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
            if (!IsManagedImagePath(path) || HasAnyReference(path))
                continue;

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best-effort attachment cleanup must not block thread lifecycle.
            }
        }
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

                foreach (var path in ExtractManagedImagePaths(user.NativeInputParts))
                    yield return CreateReference(thread.Id, turn.Id, item.Id, path, item.CreatedAt);
                foreach (var path in ExtractManagedImagePaths(user.MaterializedInputParts))
                    yield return CreateReference(thread.Id, turn.Id, item.Id, path, item.CreatedAt);
            }
        }

        foreach (var queued in thread.QueuedInputs)
        {
            foreach (var path in ExtractManagedImagePaths(queued.NativeInputParts))
                yield return CreateReference(thread.Id, queued.Id, null, path, queued.CreatedAt);
            foreach (var path in ExtractManagedImagePaths(queued.MaterializedInputParts))
                yield return CreateReference(thread.Id, queued.Id, null, path, queued.CreatedAt);
        }
    }

    private IEnumerable<string> ExtractManagedImagePaths(IEnumerable<SessionWireInputPart>? parts)
    {
        if (parts == null)
            yield break;

        foreach (var part in parts)
        {
            if (!string.Equals(part.Type, "localImage", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(part.Path))
                continue;

            var fullPath = Path.GetFullPath(part.Path);
            if (IsManagedImagePath(fullPath))
                yield return fullPath;
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
            "localImage",
            bytes,
            createdAt == default ? now : createdAt,
            now);
    }

    private bool IsManagedImagePath(string path)
    {
        try
        {
            var fullRoot = Path.GetFullPath(_attachmentsImageDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
