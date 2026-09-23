using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DotCraft.Sessions;

namespace DotCraft.SessionImport;

internal static class SessionImportIdentity
{
    public const string LocalUserId = "local";
    public const string SourceMetadataKey = "dotcraft.import.source";
    public const string SessionIdMetadataKey = "dotcraft.import.sessionId";
    public const string ImportedAtMetadataKey = "dotcraft.import.importedAt";

    public static SessionIdentity Create(string workspacePath) => new()
    {
        ChannelName = ThreadImportConstants.ChannelName,
        UserId = LocalUserId,
        ChannelContext = $"workspace:{workspacePath}",
        WorkspacePath = workspacePath
    };

    public static string ThreadIdFor(string source, string sourceId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{source}:{sourceId}"));
        return $"thread_import_{source}_{Convert.ToHexStringLower(hash)[..16]}";
    }

    public static string NewImportId() => $"imp_{Guid.NewGuid():N}";

    public static IReadOnlyDictionary<string, string> Metadata(ImportedSession session, DateTimeOffset importedAt) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SourceMetadataKey] = session.Source,
            [SessionIdMetadataKey] = session.SourceId,
            [ImportedAtMetadataKey] = FormatTimestamp(importedAt)
        };

    public static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
