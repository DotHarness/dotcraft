using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DotCraft.SessionImport.Tests;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), "dotcraft-session-import-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Combine(params string[] parts) => Path.Combine([Root, .. parts]);

    public string CreateDirectory(params string[] parts) => Directory.CreateDirectory(Combine(parts)).FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

internal static class Jsonl
{
    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Line(object record) => JsonSerializer.Serialize(record, Options);

    public static string Lines(params object[] records) => string.Concat(records.Select(static record => Line(record) + "\n"));

    public static string Write(string path, params object[] records) => WriteText(path, Lines(records));

    public static string WriteText(string path, string text, DateTimeOffset? modifiedAt = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(path, (modifiedAt ?? DateTimeOffset.UtcNow.AddMinutes(-5)).UtcDateTime);
        return path;
    }

    public static void Touch(string path, DateTimeOffset modifiedAt) => File.SetLastWriteTimeUtc(path, modifiedAt.UtcDateTime);
}

internal static class Scopes
{
    public static SessionImportScope For(
        string workspaceRoot,
        int maxSessions = 50,
        TimeSpan? maxAge = null,
        params string[] externalCliSessionIds) => new()
    {
        WorkspaceRoot = workspaceRoot,
        Now = DateTimeOffset.UtcNow,
        MaxAge = maxAge ?? TimeSpan.FromDays(30),
        MaxSessions = maxSessions,
        ExternalCliSessionIds = externalCliSessionIds.ToHashSet(StringComparer.OrdinalIgnoreCase)
    };
}
