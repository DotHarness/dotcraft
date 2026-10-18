using DotCraft.AppServer;
using DotCraft.Common;
using DotCraft.Hub;

namespace DotCraft.CLI;

internal static class ExecConnection
{
    public static async Task<WebSocketClientConnection> ConnectAsync(
        CliConfig cliConfig,
        string workspacePath,
        CancellationToken ct)
    {
        WebSocketClientConnection connection;
        if (!string.IsNullOrWhiteSpace(cliConfig.AppServerUrl))
        {
            var wsUri = new Uri(cliConfig.AppServerUrl);
            await Console.Error.WriteLineAsync($"[CLI] Connecting to AppServer at {wsUri}...").ConfigureAwait(false);
            connection = await WebSocketClientConnection.ConnectAsync(wsUri, cliConfig.AppServerToken, ct).ConfigureAwait(false);
        }
        else
        {
            await Console.Error.WriteLineAsync("[CLI] Ensuring local AppServer through Hub...").ConfigureAwait(false);
            var ensured = await new HubClient(cliConfig.AppServerBin)
                .EnsureAppServerAsync(workspacePath, "dotcraft-cli", ct)
                .ConfigureAwait(false);
            if (!ensured.Endpoints.TryGetValue("appServerWebSocket", out var wsUrl)
                || string.IsNullOrWhiteSpace(wsUrl))
            {
                throw new HubClientException(
                    "endpointUnavailable",
                    "Hub did not return an AppServer WebSocket endpoint.");
            }

            connection = await WebSocketClientConnection.ConnectAsync(new Uri(wsUrl), token: null, ct).ConfigureAwait(false);
        }

        try
        {
            using var _ = await connection.Wire.InitializeAsync(
                clientName: "dotcraft-cli",
                clientVersion: AppVersion.Informational,
                approvalSupport: false,
                requestUserInputSupport: false,
                streamingSupport: true,
                commandExecutionStreaming: true,
                toolExecutionLifecycle: true,
                ct: ct).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
