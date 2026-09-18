using System.Diagnostics.CodeAnalysis;

namespace DotCraft.Security.ShellCommands;

public sealed record ShellIdentity(ShellKind Kind, string ExecutablePath)
{
    public ShellFamily Family => Kind.ToFamily();
}

public sealed record ShellExecutableProbe(
    bool IsWindows,
    string? SystemRoot,
    Func<string, bool> FileExists,
    Func<string, string?> FindOnPath)
{
    public static ShellExecutableProbe Host { get; } = new(
        OperatingSystem.IsWindows(),
        Environment.GetEnvironmentVariable("SystemRoot"),
        File.Exists,
        FindOnHostPath);

    private static string? FindOnHostPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return null;

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), fileName);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }
}

public sealed class ShellIdentityResolver(ShellExecutableProbe probe)
{
    public static ShellIdentityResolver Host { get; } = new(ShellExecutableProbe.Host);

    private static readonly string[] WindowsExecutableExtensions = [".exe", ".cmd", ".bat", ".com"];

    public bool TryResolve(
        string? selector,
        [NotNullWhen(true)] out ShellIdentity? identity,
        [NotNullWhen(false)] out string? reason)
    {
        var trimmed = selector?.Trim() ?? string.Empty;
        return probe.IsWindows
            ? TryResolveWindows(trimmed, out identity, out reason)
            : TryResolvePosix(trimmed, out identity, out reason);
    }

    private bool TryResolveWindows(
        string selector,
        [NotNullWhen(true)] out ShellIdentity? identity,
        [NotNullWhen(false)] out string? reason)
    {
        identity = null;
        reason = null;
        var name = ExecutableName(selector.Length == 0 ? "powershell" : selector, isWindows: true);
        var isPath = selector.Contains('\\') || selector.Contains('/');

        switch (name)
        {
            case "powershell":
                return ResolveKnownWindows(
                    ShellKind.PowerShell,
                    isPath ? selector : SystemPath(@"System32\WindowsPowerShell\v1.0\powershell.exe"),
                    out identity,
                    out reason);
            case "cmd":
                return ResolveKnownWindows(
                    ShellKind.Cmd,
                    isPath ? selector : SystemPath(@"System32\cmd.exe"),
                    out identity,
                    out reason);
            case "pwsh":
                return ResolveKnownWindows(
                    ShellKind.Pwsh,
                    isPath ? selector : probe.FindOnPath("pwsh.exe"),
                    out identity,
                    out reason);
            default:
                reason = $"Shell '{selector}' is not a supported shell. Use 'powershell', 'pwsh', or 'cmd'.";
                return false;
        }
    }

    private bool ResolveKnownWindows(
        ShellKind kind,
        string? candidate,
        [NotNullWhen(true)] out ShellIdentity? identity,
        [NotNullWhen(false)] out string? reason)
    {
        identity = null;
        reason = null;
        if (string.IsNullOrEmpty(candidate) || !probe.FileExists(candidate))
        {
            reason = $"Shell '{kind}' could not be located on this machine.";
            return false;
        }

        identity = new ShellIdentity(kind, Path.GetFullPath(candidate));
        return true;
    }

    private string? SystemPath(string relative) =>
        string.IsNullOrEmpty(probe.SystemRoot) ? null : Path.Combine(probe.SystemRoot, relative);

    private bool TryResolvePosix(
        string selector,
        [NotNullWhen(true)] out ShellIdentity? identity,
        [NotNullWhen(false)] out string? reason)
    {
        identity = null;
        reason = null;
        var candidate = selector.Length == 0 ? "/bin/bash" : selector;
        var name = ExecutableName(candidate, isWindows: false);
        var kind = name switch
        {
            "bash" => ShellKind.Bash,
            "sh" => ShellKind.Sh,
            "zsh" => ShellKind.Zsh,
            "pwsh" => ShellKind.Pwsh,
            _ => (ShellKind?)null
        };
        if (kind is null)
        {
            reason = $"Shell '{selector}' is not a supported shell. Use bash, sh, zsh, or pwsh.";
            return false;
        }

        var path = candidate.Contains('/') ? candidate : probe.FindOnPath(candidate);
        if (string.IsNullOrEmpty(path) || !probe.FileExists(path))
        {
            reason = $"Shell '{candidate}' could not be located on this machine.";
            return false;
        }

        identity = new ShellIdentity(kind.Value, Path.GetFullPath(path));
        return true;
    }

    public static string ExecutableName(string value, bool isWindows)
    {
        var name = value.AsSpan().TrimEnd('/');
        var separator = isWindows ? name.LastIndexOfAny('/', '\\') : name.LastIndexOf('/');
        if (separator >= 0)
            name = name[(separator + 1)..];
        if (!isWindows)
            return name.ToString();

        var lowered = name.ToString().ToLowerInvariant();
        foreach (var extension in WindowsExecutableExtensions)
        {
            if (lowered.EndsWith(extension, StringComparison.Ordinal))
                return lowered[..^extension.Length];
        }

        return lowered;
    }
}
