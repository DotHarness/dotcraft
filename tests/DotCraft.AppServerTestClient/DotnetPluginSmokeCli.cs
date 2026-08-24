using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.AppServerTestClient;

internal static class DotnetPluginSmokeCli
{
    public static async Task<int> RunAsync(string dotcraftBin, IReadOnlyList<string> args)
    {
        if (!DotnetPluginSmokeCliOptions.TryParse(args, out var options, out var error))
        {
            Console.Error.WriteLine($"[dotnet-plugin-smoke] {error}");
            PrintUsage();
            return 1;
        }

        var report = await new DotnetPluginSmokeRunner(dotcraftBin, options).RunAsync();
        var reportDirectory = Path.GetDirectoryName(options.ReportPath);
        if (!string.IsNullOrWhiteSpace(reportDirectory))
            Directory.CreateDirectory(reportDirectory);
        await File.WriteAllTextAsync(
            options.ReportPath,
            JsonSerializer.Serialize(report, DotnetPluginSmokeJson.Options));

        Console.Error.WriteLine(
            $"[dotnet-plugin-smoke] status={report.Status} phase={report.Phase ?? "complete"} error={report.ErrorCode ?? "none"}");
        Console.Error.WriteLine($"[dotnet-plugin-smoke] report={Path.GetFileName(options.ReportPath)}");
        return report.ExitCode;
    }

    public static void PrintUsage() => Console.Error.WriteLine("""
        dotcraft-test-client --dotcraft-bin <path> dotnet-plugin-smoke --bundles <dir>
          [--provider-id <id>] [--model <model>] [--report <report.json>]
          [--work-root <dir>] [--timeout-minutes <n>] [--keep-workspace]
        """);
}

internal sealed record DotnetPluginSmokeCliOptions(
    string BundlesPath,
    string? ProviderId,
    string? Model,
    string ReportPath,
    string WorkRoot,
    TimeSpan TurnTimeout,
    bool KeepWorkspace)
{
    private const int DefaultTimeoutMinutes = 5;

    public static bool TryParse(
        IReadOnlyList<string> args,
        out DotnetPluginSmokeCliOptions options,
        out string? error)
    {
        string? bundlesPath = null;
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
                case "--bundles" when i + 1 < args.Count:
                    bundlesPath = args[++i];
                    break;
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
                case "--timeout-minutes" when i + 1 < args.Count
                                                   && int.TryParse(args[++i], out var parsed)
                                                   && parsed > 0:
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

        if (string.IsNullOrWhiteSpace(bundlesPath))
        {
            options = null!;
            error = "missing_bundles";
            return false;
        }

        workRoot ??= Path.Combine(
            Path.GetTempPath(),
            "dotcraft-plugin-smoke",
            $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..24]);
        reportPath ??= Path.Combine(workRoot, "report.json");
        options = new DotnetPluginSmokeCliOptions(
            Path.GetFullPath(bundlesPath),
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

internal static class DotnetPluginSmokeJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
