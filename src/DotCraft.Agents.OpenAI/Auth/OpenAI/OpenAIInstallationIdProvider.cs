using System.Text.RegularExpressions;

namespace DotCraft.Auth.OpenAI;

/// <summary>
/// Provides a stable per-installation UUID v4 in the host-provided user data directory.
/// The id is sent on subscription Responses requests, where the backend uses it as a sticky
/// cache-shard routing hint.
/// </summary>
public sealed class OpenAIInstallationIdProvider
{
    internal const string InstallationIdFileName = "installation_id";

    private static readonly Regex UuidPattern = new(
        @"^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string? _filePath;
    private readonly object _gate = new();
    private string? _cached;

    public OpenAIInstallationIdProvider(string? userDataPath = null)
    {
        _filePath = string.IsNullOrWhiteSpace(userDataPath)
            ? null
            : Path.Combine(userDataPath, InstallationIdFileName);
    }

    /// <summary>Absolute path to the persisted installation id file.</summary>
    public string? FilePath => _filePath;

    /// <summary>
    /// Returns the installation id, persisting it when user data is configured and otherwise
    /// retaining an ephemeral value for this provider instance.
    /// </summary>
    public string GetInstallationId()
    {
        lock (_gate)
        {
            if (_cached is not null)
                return _cached;

            var existing = _filePath is null ? null : TryReadValid(_filePath);
            if (existing is not null)
            {
                _cached = existing;
                return _cached;
            }

            var fresh = Guid.NewGuid().ToString("D").ToLowerInvariant();
            if (_filePath is null)
            {
                _cached = fresh;
                return fresh;
            }

            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
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
