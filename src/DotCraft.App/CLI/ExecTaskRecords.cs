using System.Text.Json;

namespace DotCraft.CLI;

internal static class ExecTaskRecords
{
    public sealed record Entry(string ThreadId, string TurnId);

    public static void Write(string workspacePath, string threadId, string turnId)
    {
        var directory = RecordDirectory(workspacePath);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, threadId + ".json"), JsonSerializer.Serialize(new { threadId, turnId }));
    }

    public static void Remove(string workspacePath, string threadId)
    {
        var path = Path.Combine(RecordDirectory(workspacePath), threadId + ".json");
        if (File.Exists(path))
            File.Delete(path);
    }

    public static IReadOnlyList<Entry> List(string workspacePath)
    {
        var directory = RecordDirectory(workspacePath);
        if (!Directory.Exists(directory))
            return [];

        var entries = new List<Entry>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var threadId = OneShotNotification.ReadString(doc.RootElement, "threadId");
                var turnId = OneShotNotification.ReadString(doc.RootElement, "turnId");
                if (!string.IsNullOrEmpty(threadId) && !string.IsNullOrEmpty(turnId))
                {
                    entries.Add(new Entry(threadId, turnId));
                    continue;
                }
            }
            catch (JsonException)
            {
            }

            File.Delete(path);
        }

        return entries;
    }

    private static string RecordDirectory(string workspacePath) => Path.Combine(workspacePath, ".craft", "tmp", "exec");
}
