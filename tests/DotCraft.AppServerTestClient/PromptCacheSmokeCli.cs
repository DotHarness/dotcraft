using System.Text.Json;

namespace DotCraft.AppServerTestClient;

internal static class PromptCacheSmokeCli
{
    public static async Task<int> RunAsync(string dotcraftBin, IReadOnlyList<string> args)
    {
        if (!PromptCacheSmokeCliOptions.TryParse(args, out var options, out var error))
        {
            Console.Error.WriteLine($"Error: {error}");
            PrintUsage();
            return 1;
        }

        Directory.CreateDirectory(options.WorkRoot);
        CopyMatrixForRepro(options.MatrixPath, options.WorkRoot);

        var matrix = PromptCacheSmokeMatrix.Load(options.MatrixPath);
        var runner = new PromptCacheSmokeRunner(dotcraftBin, options);
        var report = await runner.RunAsync(matrix);

        var reportDirectory = Path.GetDirectoryName(Path.GetFullPath(options.ReportPath));
        if (!string.IsNullOrWhiteSpace(reportDirectory))
            Directory.CreateDirectory(reportDirectory);

        await File.WriteAllTextAsync(
            options.ReportPath,
            JsonSerializer.Serialize(report, PromptCacheSmokeJson.Options));

        Console.Error.WriteLine(
            $"[prompt-cache-smoke] total={report.Summary.Total} passed={report.Summary.Passed} failed={report.Summary.Failed} skipped={report.Summary.Skipped}");
        Console.Error.WriteLine($"[prompt-cache-smoke] run-root={Path.GetFullPath(options.WorkRoot)}");
        Console.Error.WriteLine($"[prompt-cache-smoke] report={Path.GetFullPath(options.ReportPath)}");
        return report.ExitCode;
    }

    private static void CopyMatrixForRepro(string matrixPath, string workRoot)
    {
        var destination = Path.Combine(workRoot, "matrix.json");
        if (string.Equals(
                Path.GetFullPath(matrixPath),
                Path.GetFullPath(destination),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            return;
        }

        File.Copy(matrixPath, destination, overwrite: true);
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("""
            Usage:
              dotcraft-test-client --dotcraft-bin <path> prompt-cache-smoke --matrix <prompt-cache-smoke.json> [--report <report.json>] [--work-root <dir>] [--timeout-minutes <n>]

            Scenario:
              prompt-cache-baseline     Fast cache observability: 3 agent turns, compaction disabled.
                                        Reports aggregate prompt-cache hit ratio per provider.

            Matrix:
              {
                "providers": [
                  { "protocol": "openai-chat-completions", "providerId": "openai-chat", "model": "...", "minimumCacheHitRate": 0.5 },
                  { "protocol": "openai-responses", "providerId": "openai-responses", "model": "...", "minimumCacheHitRate": 0.3 },
                  { "protocol": "anthropic", "providerId": "anthropic", "model": "...", "minimumCacheHitRate": 0.5 }
                ]
              }
            """);
    }
}

internal sealed record PromptCacheSmokeCliOptions(
    string MatrixPath,
    string ReportPath,
    string WorkRoot,
    TimeSpan TurnTimeout)
{
    public static bool TryParse(
        IReadOnlyList<string> args,
        out PromptCacheSmokeCliOptions options,
        out string error)
    {
        string? matrixPath = null;
        string? reportPath = null;
        string? workRoot = null;
        var turnTimeout = TimeSpan.FromMinutes(10);

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--matrix" when i + 1 < args.Count:
                    matrixPath = args[++i];
                    break;
                case "--report" when i + 1 < args.Count:
                    reportPath = args[++i];
                    break;
                case "--work-root" when i + 1 < args.Count:
                    workRoot = args[++i];
                    break;
                case "--timeout-minutes" when i + 1 < args.Count:
                    if (!double.TryParse(args[++i], out var minutes) || minutes <= 0)
                    {
                        options = default!;
                        error = "--timeout-minutes must be a positive number.";
                        return false;
                    }
                    turnTimeout = TimeSpan.FromMinutes(minutes);
                    break;
                default:
                    options = default!;
                    error = $"unknown prompt-cache-smoke argument '{args[i]}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(matrixPath))
        {
            options = default!;
            error = "--matrix is required.";
            return false;
        }

        if (!File.Exists(matrixPath))
        {
            options = default!;
            error = $"matrix file not found: {matrixPath}";
            return false;
        }

        workRoot ??= CreateDefaultRunRoot();
        reportPath ??= Path.Combine(workRoot, "report.json");

        options = new PromptCacheSmokeCliOptions(
            Path.GetFullPath(matrixPath),
            Path.GetFullPath(reportPath),
            Path.GetFullPath(workRoot),
            turnTimeout);
        error = string.Empty;
        return true;
    }

    private static string CreateDefaultRunRoot()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
            userProfile = Environment.GetEnvironmentVariable("USERPROFILE")
                          ?? Environment.GetEnvironmentVariable("HOME")
                          ?? Directory.GetCurrentDirectory();

        var runId = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)
                    + "-"
                    + Guid.NewGuid().ToString("N")[..8];
        return Path.Combine(userProfile, ".craft", "smoke-tests", "runs", runId);
    }
}
