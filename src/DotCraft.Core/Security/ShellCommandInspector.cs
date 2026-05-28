using System.Text.RegularExpressions;

namespace DotCraft.Security;

/// <summary>
/// Analyzes shell commands to detect references to paths outside the workspace boundary.
/// </summary>
public sealed partial class ShellCommandInspector(string workspaceRoot)
{
    private readonly string _workspaceRoot = Path.GetFullPath(workspaceRoot);

    private static readonly HashSet<string> SafePaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/dev/null",
        "/dev/stdin",
        "/dev/stdout",
        "/dev/stderr",
        "/dev/zero",
        "/dev/random",
        "/dev/urandom",
        "/dev/tty",
        "NUL",
        "CON",
        "PRN",
        "AUX",
    };

    /// <summary>
    /// Detects paths referenced in the command that resolve to locations outside the workspace.
    /// Returns a list of the original path strings found in the command.
    /// </summary>
    public List<string> DetectOutsideWorkspacePaths(string command)
    {
        var outsidePaths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in ExtractAbsolutePaths(command))
        {
            if (SafePaths.Contains(raw) || !seen.Add(raw))
                continue;
            if (IsOutsideWorkspace(raw))
                outsidePaths.Add(raw);
        }

        foreach (var raw in ExtractHomePaths(command))
        {
            if (!seen.Add(raw))
                continue;
            var resolved = ResolveHomePath(raw);
            if (resolved != null && IsOutsideWorkspace(resolved))
                outsidePaths.Add(raw);
        }

        foreach (var raw in ExtractHomeVarPaths(command))
        {
            if (!seen.Add(raw))
                continue;
            var resolved = ResolveHomeVarPath(raw);
            if (resolved != null && IsOutsideWorkspace(resolved))
                outsidePaths.Add(raw);
        }

        foreach (var raw in ExtractWindowsDrivePaths(command))
        {
            if (!seen.Add(raw))
                continue;
            if (IsOutsideWorkspace(raw))
                outsidePaths.Add(raw);
        }

        foreach (var raw in ExtractWindowsEnvVarPaths(command))
        {
            if (!seen.Add(raw))
                continue;
            var resolved = ResolveWindowsEnvVarPath(raw);
            if (resolved != null && IsOutsideWorkspace(resolved))
                outsidePaths.Add(raw);
        }

        foreach (var raw in ExtractPowerShellEnvVarPaths(command))
        {
            if (!seen.Add(raw))
                continue;
            var resolved = ResolvePowerShellEnvVarPath(raw);
            if (resolved != null && IsOutsideWorkspace(resolved))
                outsidePaths.Add(raw);
        }

        foreach (var raw in ExtractUncPaths(command))
        {
            if (!seen.Add(raw))
                continue;
            outsidePaths.Add(raw);
        }

        return outsidePaths;
    }

    private static IEnumerable<string> ExtractAbsolutePaths(string command)
    {
        foreach (Match m in AbsolutePathRegex().Matches(command))
            yield return m.Groups[1].Value;
    }

    private static IEnumerable<string> ExtractHomePaths(string command)
    {
        foreach (Match m in HomePathRegex().Matches(command))
            yield return m.Groups[1].Value;
    }

    private static IEnumerable<string> ExtractHomeVarPaths(string command)
    {
        foreach (Match m in HomeVarRegex().Matches(command))
            yield return m.Value;
    }

    private static IEnumerable<string> ExtractWindowsDrivePaths(string command)
    {
        foreach (Match m in WindowsDrivePathRegex().Matches(command))
            yield return m.Groups[1].Value;
    }

    private static IEnumerable<string> ExtractWindowsEnvVarPaths(string command)
    {
        foreach (Match m in WindowsEnvVarPathRegex().Matches(command))
            yield return m.Value;
    }

    private static IEnumerable<string> ExtractPowerShellEnvVarPaths(string command)
    {
        foreach (Match m in PowerShellEnvVarPathRegex().Matches(command))
            yield return m.Value;
    }

    private static IEnumerable<string> ExtractUncPaths(string command)
    {
        foreach (Match m in UncPathRegex().Matches(command))
            yield return m.Groups[1].Value;
    }

    private bool IsOutsideWorkspace(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            return !fullPath.StartsWith(_workspaceRoot, StringComparison.OrdinalIgnoreCase) &&
                   !fullPath.Equals(_workspaceRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    private static string? ResolveHomePath(string path)
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
                return null;

            var rest = path.Length > 1
                ? path[1..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : string.Empty;

            return string.IsNullOrEmpty(rest) ? home : Path.Combine(home, rest);
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveHomeVarPath(string path)
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
                return null;

            var replaced = path.Replace("${HOME}", home).Replace("$HOME", home);
            return Path.GetFullPath(replaced);
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveWindowsEnvVarPath(string path)
    {
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(path);
            if (expanded == path)
                return null;
            return Path.GetFullPath(expanded);
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolvePowerShellEnvVarPath(string path)
    {
        try
        {
            var match = PowerShellEnvVarPathRegex().Match(path);
            if (!match.Success)
                return null;
            var varName = match.Groups[1].Value;
            var envValue = Environment.GetEnvironmentVariable(varName);
            if (string.IsNullOrEmpty(envValue))
                return null;
            var prefix = "$env:" + varName;
            var rest = path[prefix.Length..].TrimStart('\\', '/');
            return string.IsNullOrEmpty(rest)
                ? Path.GetFullPath(envValue)
                : Path.GetFullPath(Path.Combine(envValue, rest));
        }
        catch
        {
            return null;
        }
    }

    [GeneratedRegex(@"(?:^|[\s;|&><(=""'])(\/[a-zA-Z0-9._+@-]+(?:\/[a-zA-Z0-9._+@~-]*)*)", RegexOptions.Compiled)]
    private static partial Regex AbsolutePathRegex();

    [GeneratedRegex(@"(?:^|[\s;|&><(=""'])(~(?:\/[a-zA-Z0-9._+@-]*)+)", RegexOptions.Compiled)]
    private static partial Regex HomePathRegex();

    [GeneratedRegex(@"(?:\$HOME|\$\{HOME\})(?:\/[a-zA-Z0-9._+@/-]*)?", RegexOptions.Compiled)]
    private static partial Regex HomeVarRegex();

    [GeneratedRegex(@"(?:^|[\s;|&><(=""'])([A-Za-z]:\\[^\s;|&><""']*)", RegexOptions.Compiled)]
    private static partial Regex WindowsDrivePathRegex();

    [GeneratedRegex(@"%[A-Za-z_][A-Za-z0-9_]*%(?:[\\/_][^\s;|&><""']*)?", RegexOptions.Compiled)]
    private static partial Regex WindowsEnvVarPathRegex();

    [GeneratedRegex(@"\$env:([A-Za-z_][A-Za-z0-9_]*)(?:[\\\/][^\s;|&><""']*)?", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex PowerShellEnvVarPathRegex();

    [GeneratedRegex(@"(?:^|[\s;|&><(=""'])(\\\\[^\s;|&><""']+)", RegexOptions.Compiled)]
    private static partial Regex UncPathRegex();
}
