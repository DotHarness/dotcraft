using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace DotCraft.Utilities;

/// <summary>
/// Runs git commands with non-interactive defaults and robust timeout handling.
/// </summary>
public static class GitProcessRunner
{
    /// <summary>Result of a git process execution containing exit code and captured output streams.</summary>
    public readonly record struct GitResult(int ExitCode, string StdOut, string StdErr);

    /// <summary>
    /// Runs a git command asynchronously with timeout, non-interactive defaults, and process-tree cleanup on failure.
    /// </summary>
    public static async Task<GitResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        TimeSpan timeout,
        CancellationToken ct,
        IDictionary<string, string>? extraEnv = null,
        ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(args);

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GCM_INTERACTIVE"] = "never";
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["PAGER"] = "cat";

        if (extraEnv != null)
        {
            foreach (var (key, value) in extraEnv)
                psi.Environment[key] = value;
        }

        if (!args.Contains("--no-pager", StringComparer.Ordinal))
            psi.ArgumentList.Add("--no-pager");
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process.");

        try
        {
            process.StandardInput.Close();
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to close git stdin.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        try
        {
            await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(timeoutCts.Token))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            TryKillProcessTree(process, logger);
            throw new GitProcessTimeoutException(
                $"git {BuildCommandPreview(args)} timed out after {timeout.TotalSeconds:F0} seconds.");
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process, logger);
            throw;
        }

        return new GitResult(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }

    private static void TryKillProcessTree(Process process, ILogger? logger)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to kill git process tree.");
        }
    }

    private static string BuildCommandPreview(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            return string.Empty;

        const int maxArgsToShow = 3;
        var preview = string.Join(" ", args.Take(maxArgsToShow));
        if (args.Count > maxArgsToShow)
            preview += " ...";
        return preview;
    }
}

/// <summary>
/// Represents a timeout while waiting for a git process to finish.
/// </summary>
public sealed class GitProcessTimeoutException(string message) : TimeoutException(message);
