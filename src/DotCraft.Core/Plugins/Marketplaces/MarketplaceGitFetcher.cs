using System.ComponentModel;
using DotCraft.Utilities;
using Microsoft.Extensions.Logging;
using DotCraft.Sessions;

namespace DotCraft.Plugins.Marketplaces;

/// <summary>
/// Fetches repository marketplace sources into a staging directory.
/// </summary>
internal interface IMarketplaceGitFetcher
{
    /// <summary>
    /// Checks the source out into <paramref name="destination"/> and returns the resolved revision.
    /// </summary>
    Task<string?> FetchAsync(
        MarketplaceSource source,
        string destination,
        CancellationToken ct);
}

/// <summary>
/// Runs version control commands to materialize a repository marketplace source.
/// Every command runs non-interactively so a source needing credentials fails instead of blocking.
/// </summary>
internal sealed class MarketplaceGitFetcher(ILogger? logger = null, TimeSpan? timeout = null) : IMarketplaceGitFetcher
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    // Remote helper transports can execute an arbitrary command named by the remote address.
    // Source parsing already rejects those addresses; disabling the transport keeps the
    // guarantee even if a configured entry was hand-edited.
    private static readonly string[] HardeningArgs =
    [
        "-c", "protocol.ext.allow=never",
        "-c", "credential.interactive=never"
    ];

    private readonly TimeSpan _timeout = timeout ?? DefaultTimeout;

    public async Task<string?> FetchAsync(MarketplaceSource source, string destination, CancellationToken ct)
    {
        if (source.Kind != MarketplaceSourceKind.Git)
            throw new InvalidOperationException("Only repository marketplace sources can be fetched.");

        Directory.CreateDirectory(destination);
        var workingDirectory = Path.GetDirectoryName(Path.GetFullPath(destination))!;
        var sparsePaths = source.SparsePathList;

        if (sparsePaths.Count == 0)
        {
            await RunAsync(workingDirectory, ["clone", source.Value, destination], ct).ConfigureAwait(false);
            if (source.Ref != null)
                await RunAsync(destination, ["checkout", source.Ref], ct).ConfigureAwait(false);
        }
        else
        {
            await RunAsync(
                workingDirectory,
                ["clone", "--filter=blob:none", "--no-checkout", source.Value, destination],
                ct).ConfigureAwait(false);

            List<string> sparseArgs = ["sparse-checkout", "set"];
            sparseArgs.AddRange(sparsePaths);
            await RunAsync(destination, sparseArgs, ct).ConfigureAwait(false);
            await RunAsync(destination, ["checkout", source.Ref ?? "HEAD"], ct).ConfigureAwait(false);
        }

        return await TryReadRevisionAsync(destination, ct).ConfigureAwait(false);
    }

    private async Task<string?> TryReadRevisionAsync(string destination, CancellationToken ct)
    {
        try
        {
            var result = await RunCoreAsync(destination, ["rev-parse", "HEAD"], ct).ConfigureAwait(false);
            return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StdOut) ? result.StdOut.Trim() : null;
        }
        catch (MarketplaceException)
        {
            return null;
        }
    }

    private async Task RunAsync(string workingDirectory, IReadOnlyList<string> args, CancellationToken ct)
    {
        var result = await RunCoreAsync(workingDirectory, args, ct).ConfigureAwait(false);
        if (result.ExitCode == 0)
            return;

        var detail = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        throw new MarketplaceException(ClassifyFailure(detail), BuildFailureMessage(args, detail));
    }

    private async Task<GitProcessRunner.GitResult> RunCoreAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        CancellationToken ct)
    {
        Directory.CreateDirectory(workingDirectory);
        try
        {
            return await GitProcessRunner.RunAsync(
                workingDirectory,
                [.. HardeningArgs, .. args],
                _timeout,
                ct,
                BuildNonInteractiveEnvironment(),
                logger).ConfigureAwait(false);
        }
        catch (GitProcessTimeoutException ex)
        {
            throw new MarketplaceException(MarketplaceErrorCodes.FetchTimeout, ex.Message, ex);
        }
        catch (Win32Exception ex)
        {
            throw new MarketplaceException(
                MarketplaceErrorCodes.VersionControlUnavailable,
                "Adding a repository marketplace requires git to be available on this machine.",
                ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new MarketplaceException(
                MarketplaceErrorCodes.VersionControlUnavailable,
                "Adding a repository marketplace requires git to be available on this machine.",
                ex);
        }
    }

    // Only fill in a batch-mode ssh command when the host has not configured its own, so a
    // custom transport keeps working while a default setup still refuses to prompt.
    private static Dictionary<string, string> BuildNonInteractiveEnvironment()
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GIT_ASKPASS"] = string.Empty,
            ["SSH_ASKPASS"] = string.Empty
        };

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GIT_SSH_COMMAND"))
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GIT_SSH")))
        {
            env["GIT_SSH_COMMAND"] = "ssh -o BatchMode=yes";
        }

        return env;
    }

    private static string ClassifyFailure(string detail)
    {
        if (Contains(detail, "could not read Username")
            || Contains(detail, "could not read Password")
            || Contains(detail, "Authentication failed")
            || Contains(detail, "terminal prompts disabled")
            || Contains(detail, "Permission denied (publickey")
            || Contains(detail, "Host key verification failed")
            || Contains(detail, "403 Forbidden")
            || Contains(detail, "401 Unauthorized"))
        {
            return MarketplaceErrorCodes.AuthenticationFailed;
        }

        if (Contains(detail, "Remote branch")
            || Contains(detail, "couldn't find remote ref")
            || Contains(detail, "did not match any file(s) known to git")
            || Contains(detail, "invalid reference")
            || Contains(detail, "pathspec"))
        {
            return MarketplaceErrorCodes.RefNotFound;
        }

        return MarketplaceErrorCodes.FetchFailed;
    }

    private static bool Contains(string value, string token) =>
        value.Contains(token, StringComparison.OrdinalIgnoreCase);

    // Keep the command shape in the message but not its full argument list, so a source
    // string is not duplicated into every log line and error surface.
    private static string BuildFailureMessage(IReadOnlyList<string> args, string detail)
    {
        var command = args.Count > 0 ? args[0] : "command";
        var trimmed = detail.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? $"Marketplace fetch failed during git {command}."
            : $"Marketplace fetch failed during git {command}: {trimmed}";
    }
}
