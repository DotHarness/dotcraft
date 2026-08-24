using System.Collections.Concurrent;
using System.Text;

namespace DotCraft.Agents;

/// <summary>
/// Resolves a configured binary name to a launchable executable path, with a short-lived probe cache.
/// Host-owned so profile diagnostics and prompt visibility never take a static dependency on a runtime type.
/// </summary>
public static class SubAgentBinaryProbe
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.Ordinal);
    private static readonly string[] WindowsLaunchableExtensions = [".exe", ".cmd", ".bat", ".com"];

    /// <summary>Probes for <paramref name="bin"/> without throwing; the result is cached for a minute.</summary>
    public static bool TryResolve(string bin, out string? resolvedBinary)
    {
        resolvedBinary = null;
        if (string.IsNullOrWhiteSpace(bin))
            return false;

        var normalizedBin = bin.Trim();
        var now = DateTimeOffset.UtcNow;
        if (Cache.TryGetValue(normalizedBin, out var cached) && cached.ExpiresAt > now)
        {
            resolvedBinary = cached.ResolvedBinary;
            return cached.IsResolved;
        }

        bool isResolved;
        try
        {
            resolvedBinary = ResolveCore(normalizedBin);
            isResolved = true;
        }
        catch
        {
            resolvedBinary = null;
            isResolved = false;
        }

        Cache[normalizedBin] = new CacheEntry(now.Add(CacheTtl), isResolved, resolvedBinary);
        return isResolved;
    }

    /// <summary>Resolves <paramref name="bin"/> or throws with the reason it is not launchable.</summary>
    public static string Resolve(string bin)
    {
        if (string.IsNullOrWhiteSpace(bin))
            throw new InvalidOperationException("External subagent binary was not configured.");

        return ResolveCore(bin.Trim());
    }

    private static string ResolveCore(string bin)
    {
        if (Path.IsPathRooted(bin))
            return Validate(Path.GetFullPath(bin), bin);

        if (bin.Contains(Path.DirectorySeparatorChar) || bin.Contains(Path.AltDirectorySeparatorChar))
            return Validate(Path.GetFullPath(bin), bin);

        if (OperatingSystem.IsWindows())
        {
            var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var extensions = GetWindowsExecutableExtensions(bin);
            foreach (var directory in pathEntries)
            {
                foreach (var extension in extensions)
                {
                    var candidate = Path.Combine(directory, bin + extension);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            throw new InvalidOperationException(
                $"External subagent binary '{bin}' was not found on PATH as a launchable executable.");
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, bin);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new InvalidOperationException($"External subagent binary '{bin}' was not found on PATH.");
    }

    private static string Validate(string candidatePath, string originalValue)
    {
        if (!File.Exists(candidatePath))
            throw new InvalidOperationException($"External subagent binary '{originalValue}' does not exist.");

        if (OperatingSystem.IsWindows())
        {
            var extension = Path.GetExtension(candidatePath);
            if (!WindowsLaunchableExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"External subagent binary '{candidatePath}' is not directly launchable on Windows. Use a .cmd or .exe wrapper instead.");
            }
        }

        return candidatePath;
    }

    private static IReadOnlyList<string> GetWindowsExecutableExtensions(string bin)
    {
        var configuredExtensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(ext => WindowsLaunchableExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (configuredExtensions.Count == 0)
            configuredExtensions.AddRange(WindowsLaunchableExtensions);

        if (!string.IsNullOrEmpty(Path.GetExtension(bin)))
            return [string.Empty];

        return configuredExtensions;
    }

    private readonly record struct CacheEntry(DateTimeOffset ExpiresAt, bool IsResolved, string? ResolvedBinary);
}

/// <summary>Splits configured command-line fragments into arguments, honoring double quotes.</summary>
public static class SubAgentArgumentSyntax
{
    /// <summary>Splits <paramref name="commandLine"/> on unquoted whitespace; quotes delimit and are dropped.</summary>
    public static IReadOnlyList<string> Split(string commandLine)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine))
            return result;

        var current = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < commandLine.Length; i++)
        {
            var ch = commandLine[i];
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }
}
