using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DotCraft.Tools;

internal sealed record RipgrepSearchRequest(
    string SearchPath,
    string Pattern,
    string? IncludePattern,
    int MaxMatches,
    int MaxLineLength,
    int MaxFileSizeBytes,
    TimeSpan Timeout);

internal sealed class RipgrepFileSearcher(string? configuredPath)
{
    internal const string EnvironmentVariableName = "DOTCRAFT_RG_PATH";

    private readonly string? _rgPath = ResolveRipgrepPath(configuredPath);

    public async Task<string?> SearchAsync(
        RipgrepSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_rgPath))
            return null;

        var psi = new ProcessStartInfo
        {
            FileName = _rgPath,
            WorkingDirectory = request.SearchPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        AddArguments(psi, request);

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch
        {
            return null;
        }

        using (process)
        {
            var matches = new List<GrepMatch>();
            var matchesLock = new Lock();
            var truncated = false;
            var outputComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null)
                {
                    outputComplete.TrySetResult();
                    return;
                }

                if (!TryParseMatch(e.Data, request.SearchPath, request.MaxLineLength, out var match))
                    return;

                lock (matchesLock)
                {
                    if (matches.Count >= request.MaxMatches)
                    {
                        truncated = true;
                        TryKill(process);
                        return;
                    }

                    matches.Add(match);
                    if (matches.Count >= request.MaxMatches)
                    {
                        truncated = true;
                        TryKill(process);
                    }
                }
            };

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(NormalizeTimeout(request.Timeout));
            var searchCancellationToken = timeoutCts.Token;

            var stderrTask = process.StandardError.ReadToEndAsync(searchCancellationToken);
            process.BeginOutputReadLine();

            try
            {
                await process.WaitForExitAsync(searchCancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
            {
                await KillProcessTreeAsync(process).ConfigureAwait(false);
                return FormatTimedOutResult(request.SearchPath, matches, matchesLock, NormalizeTimeout(request.Timeout));
            }
            catch (OperationCanceledException)
            {
                await KillProcessTreeAsync(process).ConfigureAwait(false);
                throw;
            }

            try
            {
                await outputComplete.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // The process has exited; any missing output is treated as best-effort.
            }

            var stderr = await stderrTask.ConfigureAwait(false);
            List<GrepMatch> snapshot;
            lock (matchesLock)
            {
                snapshot = [.. matches];
            }

            if (snapshot.Count > 0)
                return FormatMatches(request.SearchPath, snapshot, truncated);

            return process.ExitCode switch
            {
                1 => "No matches found.",
                2 => FormatRipgrepError(stderr),
                _ => null
            };
        }
    }

    internal static string FormatTimeout(TimeSpan timeout)
        => $"Error: search timed out after {NormalizeTimeout(timeout).TotalSeconds:0} seconds. Narrow the path or include pattern and try again.";

    private static TimeSpan NormalizeTimeout(TimeSpan timeout)
        => timeout > TimeSpan.Zero ? timeout : TimeSpan.FromSeconds(30);

    internal static string? ResolveRipgrepPath(string? configuredPath)
    {
        if (TryResolveCandidate(configuredPath, out var configured))
            return configured;

        if (TryResolveCandidate(Environment.GetEnvironmentVariable(EnvironmentVariableName), out var env))
            return env;

        return FindOnPath("rg");
    }

    private static void AddArguments(ProcessStartInfo psi, RipgrepSearchRequest request)
    {
        psi.ArgumentList.Add("--json");
        psi.ArgumentList.Add("--color");
        psi.ArgumentList.Add("never");
        psi.ArgumentList.Add("--hidden");
        psi.ArgumentList.Add("--no-ignore");
        psi.ArgumentList.Add("--no-messages");
        psi.ArgumentList.Add("--no-require-git");
        psi.ArgumentList.Add("--pcre2");
        psi.ArgumentList.Add("--max-filesize");
        psi.ArgumentList.Add(request.MaxFileSizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));

        foreach (var include in SplitPatterns(request.IncludePattern))
        {
            psi.ArgumentList.Add("--glob");
            psi.ArgumentList.Add(include);
        }

        foreach (var exclude in new[]
        {
            "!.git",
            "!.git/**",
            "!**/.git/**",
            "!node_modules",
            "!node_modules/**",
            "!**/node_modules/**"
        })
        {
            psi.ArgumentList.Add("--glob");
            psi.ArgumentList.Add(exclude);
        }

        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(request.Pattern);
        psi.ArgumentList.Add(".");
    }

    private static IEnumerable<string> SplitPatterns(string? includePattern)
        => string.IsNullOrWhiteSpace(includePattern)
            ? []
            : includePattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TryParseMatch(string jsonLine, string searchPath, int maxLineLength, out GrepMatch match)
    {
        match = default;

        try
        {
            using var doc = JsonDocument.Parse(jsonLine);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type) ||
                !string.Equals(type.GetString(), "match", StringComparison.Ordinal))
            {
                return false;
            }

            var data = root.GetProperty("data");
            var pathText = data.GetProperty("path").GetProperty("text").GetString();
            var lineText = data.GetProperty("lines").GetProperty("text").GetString();
            var lineNumber = data.GetProperty("line_number").GetInt32();
            if (string.IsNullOrEmpty(pathText) || lineText == null)
                return false;

            lineText = lineText.TrimEnd('\r', '\n');
            if (lineText.Length > maxLineLength)
                lineText = lineText[..maxLineLength] + "...";

            match = new GrepMatch(Path.GetFullPath(Path.Combine(searchPath, pathText)), lineNumber, lineText);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string FormatMatches(string searchPath, IReadOnlyList<GrepMatch> matches, bool truncated)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Found {matches.Count} matches{(truncated ? $" (showing first {matches.Count}, there may be more)" : "")}:");

        var currentFile = "";
        foreach (var match in matches)
        {
            var relativePath = Path.GetRelativePath(searchPath, match.FilePath);
            if (currentFile != relativePath)
            {
                if (currentFile != "")
                    sb.AppendLine();
                currentFile = relativePath;
                sb.AppendLine($"{relativePath}:");
            }

            sb.AppendLine($"  Line {match.LineNumber}: {match.LineText}");
        }

        return sb.ToString();
    }

    private static string FormatTimedOutResult(
        string searchPath,
        List<GrepMatch> matches,
        Lock matchesLock,
        TimeSpan timeout)
    {
        List<GrepMatch> snapshot;
        lock (matchesLock)
        {
            snapshot = [.. matches];
        }

        return snapshot.Count > 0
            ? FormatMatches(searchPath, snapshot, truncated: true)
            : FormatTimeout(timeout);
    }

    private static string FormatRipgrepError(string stderr)
    {
        var message = stderr
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line =>
                line.Contains("regex parse error", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("PCRE2", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("error parsing regexp", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(message))
            return $"Error: Invalid regex pattern: {message}";

        message = stderr
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(message)
            ? "Error: Invalid regex pattern."
            : $"Error: ripgrep failed: {message}";
    }

    private static bool TryResolveCandidate(string? candidate, out string resolved)
    {
        resolved = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        candidate = Environment.ExpandEnvironmentVariables(candidate.Trim());
        if (Path.IsPathRooted(candidate) || candidate.Contains(Path.DirectorySeparatorChar) || candidate.Contains(Path.AltDirectorySeparatorChar))
        {
            if (!File.Exists(candidate))
                return false;

            resolved = Path.GetFullPath(candidate);
            return true;
        }

        var pathResolved = FindOnPath(candidate);
        if (pathResolved == null)
            return false;

        resolved = pathResolved;
        return true;
    }

    private static string? FindOnPath(string executable)
    {
        var names = OperatingSystem.IsWindows() && Path.GetExtension(executable).Length == 0
            ? new[] { $"{executable}.exe", $"{executable}.cmd", $"{executable}.bat", executable }
            : [executable];

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort limit/timeout cleanup.
        }
    }

    private static async Task KillProcessTreeAsync(Process process)
    {
        TryKill(process);
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort limit/timeout cleanup.
        }
    }

    private readonly record struct GrepMatch(string FilePath, int LineNumber, string LineText);
}
