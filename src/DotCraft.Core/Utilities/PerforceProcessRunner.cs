using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace DotCraft.Utilities;

/// <summary>
/// Runs <c>p4</c> commands with non-interactive defaults and robust timeout handling.
/// Mirrors <see cref="GitProcessRunner"/>. Connection parameters are expected to be supplied
/// by the caller as P4 global options (<c>-p</c>/<c>-c</c>/<c>-u</c>/<c>-C</c>) ahead of the
/// subcommand, or via environment variables in <paramref name="extraEnv"/>.
/// </summary>
public static class PerforceProcessRunner
{
    /// <summary>Result of a p4 process execution containing exit code and captured output streams.</summary>
    public readonly record struct P4Result(int ExitCode, string StdOut, string StdErr);

    /// <summary>
    /// Runs a p4 command asynchronously with timeout, non-interactive defaults, and process-tree cleanup on failure.
    /// </summary>
    /// <param name="executable">Absolute path to the p4 executable, or <c>"p4"</c> to resolve from PATH.</param>
    /// <param name="stdinInput">Optional text written to stdin (used for a transient <c>p4 login</c> password) then closed.</param>
    public static async Task<P4Result> RunAsync(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> args,
        TimeSpan timeout,
        CancellationToken ct,
        IDictionary<string, string>? extraEnv = null,
        string? stdinInput = null,
        ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(args);

        var psi = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Keep p4 non-interactive: never spawn an editor or pager for spec/output commands.
        psi.Environment["P4EDITOR"] = OperatingSystem.IsWindows() ? "cmd /c" : "true";
        psi.Environment["P4PAGER"] = string.Empty;
        psi.Environment["PAGER"] = string.Empty;

        if (extraEnv != null)
        {
            foreach (var (key, value) in extraEnv)
                psi.Environment[key] = value;
        }

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start p4 process.");

        try
        {
            if (!string.IsNullOrEmpty(stdinInput))
            {
                await process.StandardInput.WriteLineAsync(stdinInput).ConfigureAwait(false);
            }
            process.StandardInput.Close();
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to write/close p4 stdin.");
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
            throw new PerforceProcessTimeoutException(
                $"p4 {BuildCommandPreview(args)} timed out after {timeout.TotalSeconds:F0} seconds.");
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process, logger);
            throw;
        }

        return new P4Result(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
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
            logger?.LogWarning(ex, "Failed to kill p4 process tree.");
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
/// Represents a timeout while waiting for a p4 process to finish.
/// </summary>
public sealed class PerforceProcessTimeoutException(string message) : TimeoutException(message);
