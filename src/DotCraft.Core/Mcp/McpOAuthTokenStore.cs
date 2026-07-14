using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Authentication;

namespace DotCraft.Mcp;

/// <summary>
/// Persists MCP OAuth tokens outside workspace configuration and Session history. Entries are
/// partitioned by safe origin, runtime server name, and endpoint hash.
/// </summary>
internal sealed class McpOAuthTokenStore : ITokenCache
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;
    private readonly string _key;

    private McpOAuthTokenStore(string path, string key)
    {
        _path = path;
        _key = key;
    }

    /// <summary>Creates the token cache partition for a server.</summary>
    public static McpOAuthTokenStore Create(McpServerConfig server)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".craft");
        var endpointHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(server.Url ?? string.Empty)))[..16].ToLowerInvariant();
        var key = string.Join(
            "/",
            string.IsNullOrWhiteSpace(server.Origin.Kind) ? "workspace" : server.Origin.Kind,
            server.Name,
            endpointHash);
        return new McpOAuthTokenStore(Path.Combine(root, "mcp-auth.json"), key);
    }

    /// <summary>Returns whether this partition currently contains tokens.</summary>
    public async ValueTask<bool> HasTokensAsync(CancellationToken cancellationToken = default) =>
        await GetTokensAsync(cancellationToken) != null;

    /// <inheritdoc />
    public async ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadDocumentUnsafeAsync(cancellationToken);
            document[_key] = tokens;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tempPath = _path + ".tmp";
            await File.WriteAllTextAsync(
                tempPath,
                JsonSerializer.Serialize(document, SerializerOptions),
                Encoding.UTF8,
                cancellationToken);
            RestrictFilePermissions(tempPath);
            File.Move(tempPath, _path, overwrite: true);
            RestrictFilePermissions(_path);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadDocumentUnsafeAsync(cancellationToken);
            return document.TryGetValue(_key, out var tokens) ? tokens : null;
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<Dictionary<string, TokenContainer>> ReadDocumentUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
            return new Dictionary<string, TokenContainer>(StringComparer.Ordinal);

        try
        {
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<Dictionary<string, TokenContainer>>(
                       stream,
                       SerializerOptions,
                       cancellationToken)
                   ?? new Dictionary<string, TokenContainer>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            // A corrupt credential file is never copied into config or logs. Treat it as empty;
            // a subsequent successful login atomically replaces it.
            return new Dictionary<string, TokenContainer>(StringComparer.Ordinal);
        }
    }

    private static void RestrictFilePermissions(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
