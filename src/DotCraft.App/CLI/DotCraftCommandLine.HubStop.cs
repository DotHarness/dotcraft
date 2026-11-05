using System.CommandLine;
using System.Globalization;
using DotCraft.Hub;

namespace DotCraft.CLI;

public static partial class DotCraftCommandLine
{
    private static StreamWriter? OpenHubDiagnosticLog(string? logPath)
    {
        if (string.IsNullOrWhiteSpace(logPath)) return null;
        var fullPath = Path.GetFullPath(logPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        return new StreamWriter(new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
    }

    private static Command CreateHubStopCommand()
    {
        var pid = new Option<int>("--expected-pid") { Required = true };
        var startedAt = StringOption("--expected-started-at", "Captured Hub process start timestamp.");
        startedAt.Required = true;
        var executable = StringOption("--expected-executable", "Captured Hub executable path.");
        executable.Required = true;
        var grace = new Option<int>("--grace-ms") { DefaultValueFactory = _ => 10_000 };
        var stop = new Command("stop", "Stop a captured Hub instance, with bounded forced-exit fallback.")
        {
            pid, startedAt, executable, grace
        };
        stop.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                await HubProcessStopper.StopAsync(
                    HubPaths.ForCurrentUser(),
                    parseResult.GetValue(pid),
                    DateTimeOffset.Parse(parseResult.GetRequiredValue(startedAt)!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    parseResult.GetRequiredValue(executable)!,
                    TimeSpan.FromMilliseconds(parseResult.GetValue(grace)),
                    cancellationToken);
                return 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                await parseResult.InvocationConfiguration.Error.WriteLineAsync($"Hub stop failed: {ex.Message}");
                return 1;
            }
        });
        return stop;
    }
}
