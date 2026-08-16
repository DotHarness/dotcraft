using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DotCraft.TraceViewer.Analysis;

internal sealed class TraceReviewStore(string rootPath)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _rootPath = Path.GetFullPath(rootPath);

    public StoredTraceReview? Load(string workspacePath, string sessionKey)
    {
        var path = GetPath(workspacePath, sessionKey);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<StoredTraceReview>(File.ReadAllText(path), JsonOptions); }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(string workspacePath, StoredTraceReview record)
    {
        var path = GetPath(workspacePath, record.Review.SessionKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(record, JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }

    private string GetPath(string workspacePath, string sessionKey) => Path.Combine(
        _rootPath,
        Hash(Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspacePath)).ToUpperInvariant()),
        Hash(sessionKey) + ".json");

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..24];
}
