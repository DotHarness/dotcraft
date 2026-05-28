using System.Text.Json;
using DotCraft.Protocol;

namespace DotCraft.Agents;

public interface IExternalCliSessionStore
{
    bool TryGetResumeSession(
        string profileName,
        string? label,
        string workingDirectory,
        out ExternalCliStoredSession session);

    void RecordSuccessfulRun(
        string profileName,
        string? label,
        string workingDirectory,
        string sessionId);
}

public sealed record ExternalCliStoredSession(
    string ProfileName,
    string? Label,
    string WorkingDirectory,
    string SessionId);

internal sealed class ThreadExternalCliSessionStore(SessionThread thread) : IExternalCliSessionStore
{
    private const string MetadataKey = "dotcraft.externalCliSessions";
    private const int MaxSessions = 32;

    public bool TryGetResumeSession(
        string profileName,
        string? label,
        string workingDirectory,
        out ExternalCliStoredSession session)
    {
        var sessions = LoadSessions();
        var normalizedLabel = NormalizeLabel(label);
        var normalizedWorkingDirectory = NormalizeWorkingDirectory(workingDirectory);

        if (!string.IsNullOrEmpty(normalizedLabel))
        {
            var exact = sessions
                .Where(entry =>
                    string.Equals(entry.ProfileName, profileName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(entry.WorkingDirectory, normalizedWorkingDirectory, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(entry.Label, normalizedLabel, StringComparison.Ordinal))
                .OrderByDescending(entry => entry.LastUpdatedAt)
                .FirstOrDefault();
            if (exact != null)
            {
                session = new ExternalCliStoredSession(exact.ProfileName, exact.Label, exact.WorkingDirectory, exact.SessionId);
                return true;
            }
        }
        else
        {
            var candidates = sessions
                .Where(entry =>
                    string.Equals(entry.ProfileName, profileName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(entry.WorkingDirectory, normalizedWorkingDirectory, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(entry => entry.LastUpdatedAt)
                .Take(2)
                .ToArray();
            if (candidates.Length == 1)
            {
                var match = candidates[0];
                session = new ExternalCliStoredSession(match.ProfileName, match.Label, match.WorkingDirectory, match.SessionId);
                return true;
            }
        }

        session = new ExternalCliStoredSession(string.Empty, null, string.Empty, string.Empty);
        return false;
    }

    public void RecordSuccessfulRun(
        string profileName,
        string? label,
        string workingDirectory,
        string sessionId)
    {
        if (string.IsNullOrWhiteSpace(profileName)
            || string.IsNullOrWhiteSpace(workingDirectory)
            || string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var sessions = LoadSessions();
        var normalizedLabel = NormalizeLabel(label);
        var normalizedWorkingDirectory = NormalizeWorkingDirectory(workingDirectory);
        var existing = sessions.FirstOrDefault(entry =>
            string.Equals(entry.ProfileName, profileName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.WorkingDirectory, normalizedWorkingDirectory, StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.Label, normalizedLabel, StringComparison.Ordinal));

        if (existing != null)
        {
            existing.SessionId = sessionId.Trim();
            existing.LastUpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            sessions.Add(new ExternalCliSessionRecord
            {
                ProfileName = profileName.Trim(),
                Label = normalizedLabel,
                WorkingDirectory = normalizedWorkingDirectory,
                SessionId = sessionId.Trim(),
                LastUpdatedAt = DateTimeOffset.UtcNow
            });
        }

        var trimmed = sessions
            .OrderByDescending(entry => entry.LastUpdatedAt)
            .Take(MaxSessions)
            .ToArray();
        thread.Metadata[MetadataKey] = JsonSerializer.Serialize(trimmed, SessionWireJsonOptions.Default);
    }

    private List<ExternalCliSessionRecord> LoadSessions()
    {
        if (!thread.Metadata.TryGetValue(MetadataKey, out var raw) || string.IsNullOrWhiteSpace(raw))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<ExternalCliSessionRecord>>(raw, SessionWireJsonOptions.Default) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string NormalizeWorkingDirectory(string workingDirectory)
        => Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string? NormalizeLabel(string? label)
    {
        var trimmed = label?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private sealed class ExternalCliSessionRecord
    {
        public string ProfileName { get; set; } = string.Empty;

        public string? Label { get; set; }

        public string WorkingDirectory { get; set; } = string.Empty;

        public string SessionId { get; set; } = string.Empty;

        public DateTimeOffset LastUpdatedAt { get; set; }
    }
}
