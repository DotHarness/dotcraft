using System.Text.Json;

namespace DotCraft.AppServerTestClient;

internal static class DotNetPluginAuthoringSmokeCli
{
    public static async Task<int> RunAsync(string dotcraftBin, IReadOnlyList<string> args)
    {
        if (!DotNetPluginAuthoringSmokeCliOptions.TryParse(args, out var options, out var error))
        {
            Console.Error.WriteLine($"[dotnet-plugin-authoring-smoke] {error}");
            PrintUsage();
            return 1;
        }

        var report = await new DotNetPluginAuthoringSmokeRunner(dotcraftBin, options).RunAsync();
        var reportDirectory = Path.GetDirectoryName(options.ReportPath);
        if (!string.IsNullOrWhiteSpace(reportDirectory))
            Directory.CreateDirectory(reportDirectory);
        await File.WriteAllTextAsync(
            options.ReportPath,
            JsonSerializer.Serialize(report, DotNetPluginSmokeJson.Options));

        Console.Error.WriteLine(
            $"[dotnet-plugin-authoring-smoke] status={report.Status} phase={report.Phase ?? "complete"} error={report.ErrorCode ?? "none"}");
        Console.Error.WriteLine(
            $"[dotnet-plugin-authoring-smoke] report={Path.GetFileName(options.ReportPath)}");
        return report.ExitCode;
    }

    public static void PrintUsage() => Console.Error.WriteLine("""
        dotcraft-test-client --dotcraft-bin <path> dotnet-plugin-authoring-smoke
          [--provider-id <id>] [--model <model>] [--report <report.json>]
          [--work-root <dir>] [--timeout-minutes <n>] [--keep-workspace]
        """);
}

internal sealed record DotNetPluginAuthoringSmokeCliOptions(
    string? ProviderId,
    string? Model,
    string ReportPath,
    string WorkRoot,
    TimeSpan TurnTimeout,
    bool KeepWorkspace)
{
    private const int DefaultTimeoutMinutes = 8;

    public static bool TryParse(
        IReadOnlyList<string> args,
        out DotNetPluginAuthoringSmokeCliOptions options,
        out string? error)
    {
        string? providerId = null;
        string? model = null;
        string? reportPath = null;
        string? workRoot = null;
        var timeoutMinutes = DefaultTimeoutMinutes;
        var keepWorkspace = false;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--provider-id" when i + 1 < args.Count:
                    providerId = args[++i];
                    break;
                case "--model" when i + 1 < args.Count:
                    model = args[++i];
                    break;
                case "--report" when i + 1 < args.Count:
                    reportPath = args[++i];
                    break;
                case "--work-root" when i + 1 < args.Count:
                    workRoot = args[++i];
                    break;
                case "--timeout-minutes":
                    if (++i >= args.Count
                        || !int.TryParse(args[i], out var parsed)
                        || parsed <= 0)
                    {
                        options = null!;
                        error = "invalid_timeout_minutes";
                        return false;
                    }
                    timeoutMinutes = parsed;
                    break;
                case "--keep-workspace":
                    keepWorkspace = true;
                    break;
                default:
                    options = null!;
                    error = $"invalid_argument:{args[i]}";
                    return false;
            }
        }

        workRoot ??= Path.Combine(
            Path.GetTempPath(),
            "dotcraft-plugin-authoring-smoke",
            $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..24]);
        reportPath ??= Path.Combine(workRoot, "report.json");
        options = new DotNetPluginAuthoringSmokeCliOptions(
            NullIfWhiteSpace(providerId),
            NullIfWhiteSpace(model),
            Path.GetFullPath(reportPath),
            Path.GetFullPath(workRoot),
            TimeSpan.FromMinutes(timeoutMinutes),
            keepWorkspace);
        error = null;
        return true;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
