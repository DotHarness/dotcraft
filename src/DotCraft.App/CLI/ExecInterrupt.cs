using DotCraft.AppServer;
using DotCraft.Configuration;

namespace DotCraft.CLI;

internal static class ExecInterrupt
{
    public static async Task<int> RunAsync(
        string workspacePath,
        string? threadId,
        CommandLineArgs connectionArgs,
        CancellationToken ct)
    {
        var craftPath = Path.Combine(workspacePath, ".craft");
        if (!Directory.Exists(craftPath))
        {
            await Console.Error.WriteLineAsync($"DotCraft workspace not found: {craftPath}").ConfigureAwait(false);
            return 1;
        }

        try
        {
            var config = AppConfig.LoadWithGlobalFallback(Path.Combine(craftPath, "config.json"));
            connectionArgs.ApplyTo(config);
            await using var connection = await ExecConnection.ConnectAsync(
                config.GetSection<CliConfig>("CLI"),
                workspacePath,
                ct).ConfigureAwait(false);
            var wire = connection.Wire;

            if (threadId is null)
            {
                var running = await ExecThreads.FindRunningAsync(wire, ct).ConfigureAwait(false);
                if (running.Count == 0)
                {
                    await Console.Error.WriteLineAsync("[CLI] No exec task is running in this workspace.").ConfigureAwait(false);
                    return 0;
                }

                if (running.Count > 1)
                {
                    await Console.Error.WriteLineAsync(
                        $"More than one exec task is running. Pass one of these thread ids: {string.Join(", ", running)}").ConfigureAwait(false);
                    return 1;
                }

                threadId = running[0];
            }

            var thread = await ExecThreads.ReadAsync(wire, threadId, ct).ConfigureAwait(false);
            var turnId = thread.TryGetProperty("runtime", out var runtime)
                ? OneShotNotification.ReadString(runtime, "activeTurnId")
                : null;
            if (string.IsNullOrEmpty(turnId))
            {
                await Console.Error.WriteLineAsync($"[CLI] Thread {threadId} has no running turn.").ConfigureAwait(false);
                return 0;
            }

            try
            {
                await ExecThreads.InterruptTurnAsync(wire, threadId, turnId, ct).ConfigureAwait(false);
            }
            catch (ExecRequestException ex) when (ex.Code == AppServerErrors.TurnNotRunningCode)
            {
                await Console.Error.WriteLineAsync($"[CLI] Thread {threadId} has no running turn.").ConfigureAwait(false);
                return 0;
            }

            await Console.Error.WriteLineAsync($"[CLI] Interrupted turn {turnId} on thread {threadId}.").ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }
}
