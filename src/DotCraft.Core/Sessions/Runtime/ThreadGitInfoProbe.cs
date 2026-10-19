using DotCraft.Utilities;

namespace DotCraft.Sessions;

internal static class ThreadGitInfoProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    // Git's "not a git repository" message is matched below, so keep it untranslated.
    private static readonly Dictionary<string, string> Environment = new() { ["LC_ALL"] = "C" };

    /// <summary>
    /// Returns null outside any Git working tree and throws when Git cannot be read.
    /// </summary>
    internal static async Task<ThreadGitInfo?> ReadAsync(string workspace, CancellationToken ct)
    {
        var inside = await RunAsync(workspace, ["rev-parse", "--is-inside-work-tree"], ct);
        if (inside.ExitCode != 0)
        {
            return inside.StdErr.Contains("not a git repository", StringComparison.OrdinalIgnoreCase)
                ? null
                : throw new InvalidOperationException(inside.StdErr);
        }
        if (inside.StdOut.Trim() != "true")
            return null;

        var branch = await RunAsync(workspace, ["symbolic-ref", "--quiet", "--short", "HEAD"], ct);
        var origin = await RunAsync(workspace, ["config", "--get", "remote.origin.url"], ct);
        var info = new ThreadGitInfo
        {
            Branch = branch.ExitCode == 0 ? NullIfEmpty(branch.StdOut) : null,
            OriginUrl = origin.ExitCode == 0 ? SanitizeRemoteUrl(origin.StdOut) : null
        };
        return info.Branch == null && info.OriginUrl == null ? null : info;
    }

    internal static string? SanitizeRemoteUrl(string raw)
    {
        var url = NullIfEmpty(raw);
        if (url == null)
            return null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && url.Contains("://", StringComparison.Ordinal))
            return uri.GetComponents(UriComponents.AbsoluteUri & ~UriComponents.UserInfo, UriFormat.UriEscaped);

        // scp-like syntax: [user@]host:path
        var at = url.IndexOf('@');
        return at >= 0 && url.IndexOf(':') > at ? url[(at + 1)..] : url;
    }

    private static Task<GitProcessRunner.GitResult> RunAsync(string workspace, string[] args, CancellationToken ct)
        => GitProcessRunner.RunAsync(workspace, args, Timeout, ct, Environment);

    private static string? NullIfEmpty(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
