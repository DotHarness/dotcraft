using System.Text.RegularExpressions;

namespace DotCraft.Auth.OpenAI;

/// <summary>
/// Provides a stable per-installation UUID v4 persisted at <c>~/.craft/installation_id</c>.
/// The id is sent on subscription Responses requests, where the backend uses it as a sticky
/// cache-shard routing hint.
/// </summary>
public sealed class OpenAIInstallationIdProvider
{
    internal const string InstallationIdFileName = "installation_id";

    private static readonly Regex UuidPattern = new(
        @"^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _filePath;
    private readonly object _gate = new();
    private string? _cached;

    public OpenAIInstallationIdProvider(string? globalCraftDir = null)
    {
        var dir = string.IsNullOrWhiteSpace(globalCraftDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".craft")
            : globalCraftDir;
        _filePath = Path.Combine(dir, InstallationIdFileName);
    }

    /// <summary>Absolute path to the persisted installation id file.</summary>
    public string FilePath => _filePath;

    /// <summary>
    /// Returns the persisted installation id, generating and saving a fresh UUID v4 when the file
    /// is absent or its contents are not a valid lowercase UUID v4. Subsequent calls return the
    /// cached value without re-reading the file.
    /// </summary>
    public string GetInstallationId()
    {
        lock (_gate)
        {
            if (_cached is not null)
                return _cached;

            var existing = TryReadValid(_filePath);
            if (existing is not null)
            {
                _cached = existing;
                return _cached;
            }

            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var fresh = Guid.NewGuid().ToString("D").ToLowerInvariant();
            try
            {
                File.WriteAllText(_filePath, fresh);
            }
            catch (IOException)
            {
                // Cache the generated id even if persistence failed; the worst case is regenerating
                // on the next process start. We do not want to throw and break the auth pipeline.
            }
            catch (UnauthorizedAccessException)
            {
            }

            _cached = fresh;
            return _cached;
        }
    }

    private static string? TryReadValid(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var raw = File.ReadAllText(path).Trim().ToLowerInvariant();
            return UuidPattern.IsMatch(raw) ? raw : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
