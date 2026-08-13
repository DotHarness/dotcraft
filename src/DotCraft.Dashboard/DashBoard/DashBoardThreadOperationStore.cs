using System.Text.Json;

namespace DotCraft.DashBoard;

internal sealed record DashBoardThreadOperation(
    string Id,
    string Type,
    string ThreadId,
    DateTimeOffset Timestamp,
    int NumTurns,
    string Source);

internal sealed class DashBoardThreadOperationStore(string craftPath)
{
    private const string RollbackType = "rollback";
    private const string RolloutSource = "rollout";

    public IReadOnlyList<DashBoardThreadOperation> GetThreadOperations(string? threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            return [];

        var operations = new List<DashBoardThreadOperation>();
        foreach (var path in EnumerateCandidatePaths(threadId))
        {
            if (!File.Exists(path))
                continue;

            var lineNumber = 0;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                if (!line.Contains("\"thread_rolled_back\"", StringComparison.Ordinal))
                    continue;

                if (TryReadRollbackOperation(line, threadId, lineNumber, out var operation))
                    operations.Add(operation);
            }
        }

        return operations
            .OrderBy(static operation => operation.Timestamp)
            .ThenBy(static operation => operation.Id, StringComparer.Ordinal)
            .ToList();
    }

    public int CountThreadRollbacks(string? threadId)
        => GetThreadOperations(threadId).Count(static operation =>
            string.Equals(operation.Type, RollbackType, StringComparison.Ordinal));

    private bool TryReadRollbackOperation(
        string line,
        string requestedThreadId,
        int lineNumber,
        out DashBoardThreadOperation operation)
    {
        operation = default!;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!TryGetString(root, "kind", out var kind) ||
                !string.Equals(kind, "thread_rolled_back", StringComparison.Ordinal))
            {
                return false;
            }

            if (!root.TryGetProperty("threadRolledBack", out var payload))
                return false;

            if (!TryGetString(payload, "threadId", out var threadId) ||
                !string.Equals(threadId, requestedThreadId, StringComparison.Ordinal))
            {
                return false;
            }

            if (!payload.TryGetProperty("numTurns", out var numTurnsElement) ||
                !numTurnsElement.TryGetInt32(out var numTurns) ||
                numTurns <= 0)
            {
                return false;
            }

            if (!TryGetDateTimeOffset(root, "timestamp", out var timestamp) &&
                !TryGetDateTimeOffset(payload, "lastActiveAt", out timestamp))
            {
                return false;
            }

            operation = new DashBoardThreadOperation(
                BuildRollbackId(threadId, timestamp, numTurns, lineNumber),
                RollbackType,
                threadId,
                timestamp,
                numTurns,
                RolloutSource);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private IEnumerable<string> EnumerateCandidatePaths(string threadId)
    {
        var safe = MakeSafe(threadId);
        yield return Path.Combine(craftPath, "threads", "active", $"{safe}.jsonl");
        yield return Path.Combine(craftPath, "threads", "archived", $"{safe}.jsonl");
    }

    private static string BuildRollbackId(
        string threadId,
        DateTimeOffset timestamp,
        int numTurns,
        int lineNumber)
        => $"rollback:{threadId}:{timestamp.UtcTicks}:{numTurns}:{lineNumber}";

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetDateTimeOffset(JsonElement element, string propertyName, out DateTimeOffset value)
    {
        value = default;
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return property.TryGetDateTimeOffset(out value);
    }

    private static string MakeSafe(string key)
        => string.Concat(key.Split(Path.GetInvalidFileNameChars()));
}
