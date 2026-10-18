using DotCraft.Workspaces;
using System.Text;
using System.Text.Json;
using DotCraft.Common;
using DotCraft.Configuration;
using DotCraft.Hub;
using DotCraft.Hosting;
using DotCraft.AppServer;

namespace DotCraft.CLI;

/// <summary>
/// One-shot command-line host used by <c>dotcraft exec</c>.
/// </summary>
public sealed class CliHost(
    CommandLineArgs cliArgs,
    AppConfig config,
    DotCraftPaths paths) : IDotCraftHost
{
    private WebSocketClientConnection? _wsConnection;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = await ResolvePromptAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(prompt))
            {
                await Console.Error.WriteLineAsync("Prompt cannot be empty.").ConfigureAwait(false);
                Environment.ExitCode = 1;
                return;
            }

            var cliConfig = config.GetSection<CliConfig>("CLI");
            var wire = await ConnectAsync(cliConfig, cancellationToken).ConfigureAwait(false);
            var result = await RunOneShotAsync(wire, prompt, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(result.Text))
            {
                await using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
                await stdout.WriteLineAsync(result.Text).ConfigureAwait(false);
            }

            if (!result.Success)
            {
                if (!string.IsNullOrWhiteSpace(result.Error))
                    await Console.Error.WriteLineAsync(result.Error).ConfigureAwait(false);
                Environment.ExitCode = 1;
            }
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("DotCraft exec cancelled.").ConfigureAwait(false);
            Environment.ExitCode = 1;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            Environment.ExitCode = 1;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_wsConnection != null)
            await _wsConnection.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<string> ResolvePromptAsync(CancellationToken ct)
    {
        if (cliArgs.ExecReadStdin)
        {
            var buffer = new StringBuilder();
            var chunk = new char[4096];
            while (true)
            {
                var read = await Console.In.ReadAsync(chunk.AsMemory(0, chunk.Length), ct).ConfigureAwait(false);
                if (read == 0)
                    break;
                buffer.Append(chunk, 0, read);
            }

            return buffer.ToString().Trim();
        }

        return cliArgs.ExecPrompt?.Trim() ?? string.Empty;
    }

    private async Task<AppServerWireClient> ConnectAsync(CliConfig cliConfig, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(cliConfig.AppServerUrl))
        {
            var wsUri = new Uri(cliConfig.AppServerUrl);
            await Console.Error.WriteLineAsync($"[CLI] Connecting to AppServer at {wsUri}...").ConfigureAwait(false);

            _wsConnection = await WebSocketClientConnection.ConnectAsync(
                wsUri,
                cliConfig.AppServerToken,
                ct).ConfigureAwait(false);
        }
        else
        {
            await Console.Error.WriteLineAsync("[CLI] Ensuring local AppServer through Hub...").ConfigureAwait(false);

            var hub = new HubClient(cliConfig.AppServerBin);
            var ensured = await hub.EnsureAppServerAsync(
                paths.WorkspacePath,
                "dotcraft-cli",
                ct).ConfigureAwait(false);

            if (!ensured.Endpoints.TryGetValue("appServerWebSocket", out var wsUrl)
                || string.IsNullOrWhiteSpace(wsUrl))
            {
                throw new HubClientException(
                    "endpointUnavailable",
                    "Hub did not return an AppServer WebSocket endpoint.");
            }

            _wsConnection = await WebSocketClientConnection.ConnectAsync(
                new Uri(wsUrl),
                token: null,
                ct).ConfigureAwait(false);
        }

        await _wsConnection.Wire.InitializeAsync(
            clientName: "dotcraft-cli",
            clientVersion: AppVersion.Informational,
            approvalSupport: true,
            streamingSupport: true,
            toolExecutionLifecycle: true).ConfigureAwait(false);

        return _wsConnection.Wire;
    }

    private async Task<OneShotResult> RunOneShotAsync(
        AppServerWireClient wire,
        string prompt,
        CancellationToken ct)
    {
        var threadId = await CreateThreadAsync(wire, ct).ConfigureAwait(false);
        wire.RegisterThreadChannel(threadId);

        var output = new StringBuilder();
        string? completedText = null;
        var approvalRequested = false;
        var terminal = false;
        string? terminalError = null;

        wire.ServerRequestHandler = async request =>
        {
            await Task.CompletedTask.ConfigureAwait(false);
            if (request.RootElement.TryGetProperty("method", out var method)
                && method.GetString() == DotCraft.Protocol.AppServer.AppServerMethodNames.ApprovalRequest)
            {
                approvalRequested = true;
                return new { decision = "decline" };
            }

            return null;
        };

        try
        {
            var startResult = await wire.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
            {
                threadId,
                input = new[] { new { type = "text", text = prompt } }
            }, timeout: TimeSpan.FromSeconds(30), ct: ct).ConfigureAwait(false);

            var turnId = startResult.RootElement
                .GetProperty("result").GetProperty("turn").GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(turnId))
                throw new InvalidOperationException("AppServer returned a turn with no id.");

            var notifications = wire.ReadThreadTurnNotificationsAsync(
                threadId,
                timeout: TimeSpan.FromMinutes(30),
                ct: ct);

            await foreach (var doc in notifications.WithCancellation(ct).ConfigureAwait(false))
            {
                var notification = OneShotNotification.From(doc);
                switch (notification.Kind)
                {
                    case OneShotNotificationKind.AgentDelta:
                        output.Append(notification.Text);
                        break;
                    case OneShotNotificationKind.AgentCompleted:
                        if (output.Length == 0)
                            output.Append(notification.Text);
                        break;
                    case OneShotNotificationKind.Progress:
                        if (!string.IsNullOrWhiteSpace(notification.Text))
                            await Console.Error.WriteLineAsync(notification.Text).ConfigureAwait(false);
                        break;
                    case OneShotNotificationKind.Completed:
                        terminal = true;
                        completedText = notification.Text;
                        break;
                    case OneShotNotificationKind.Failed:
                        terminal = true;
                        terminalError = notification.Text ?? "Turn failed.";
                        break;
                    case OneShotNotificationKind.Cancelled:
                        terminal = true;
                        terminalError = "Turn cancelled.";
                        break;
                }
            }

            ct.ThrowIfCancellationRequested();
            if (!terminal)
                await InterruptTurnAsync(wire, threadId, turnId).ConfigureAwait(false);
        }
        finally
        {
            wire.ServerRequestHandler = null;
            wire.UnregisterThreadChannel(threadId);
        }

        var text = string.IsNullOrEmpty(completedText) ? output.ToString() : completedText;

        if (!terminal)
            return new OneShotResult(false, text, "Timed out waiting for the turn to finish.");

        if (approvalRequested)
            return new OneShotResult(false, text, "Approval required; dotcraft exec declined the request.");

        if (!string.IsNullOrWhiteSpace(terminalError))
            return new OneShotResult(false, text, terminalError);

        return new OneShotResult(true, text, null);
    }

    private static async Task InterruptTurnAsync(AppServerWireClient wire, string threadId, string turnId)
    {
        try
        {
            await wire.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnInterrupt, new
            {
                threadId,
                turnId
            }, timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[CLI] Failed to interrupt the timed-out turn: {ex.Message}").ConfigureAwait(false);
        }
    }

    private async Task<string> CreateThreadAsync(AppServerWireClient wire, CancellationToken ct)
    {
        var result = await wire.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new
            {
                channelName = "cli",
                userId = "local",
                channelContext = (string?)null,
                workspacePath = paths.WorkspacePath
            },
            historyMode = "server"
        }, ct: ct).ConfigureAwait(false);

        return result.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()
            ?? throw new InvalidOperationException("AppServer returned a thread with no id.");
    }
}

internal sealed record OneShotResult(bool Success, string Text, string? Error);

internal enum OneShotNotificationKind
{
    Ignored,
    AgentDelta,
    AgentCompleted,
    Progress,
    Completed,
    Failed,
    Cancelled
}

internal sealed record OneShotNotification(OneShotNotificationKind Kind, string? Text)
{
    public static OneShotNotification From(JsonDocument doc)
    {
        var root = doc.RootElement;
        if (!root.TryGetProperty("method", out var methodEl))
            return new OneShotNotification(OneShotNotificationKind.Ignored, null);

        var method = methodEl.GetString();
        var hasParams = root.TryGetProperty("params", out var @params);

        return method switch
        {
            DotCraft.Protocol.AppServer.AppServerMethodNames.AgentMessageDelta => new OneShotNotification(
                OneShotNotificationKind.AgentDelta,
                hasParams && @params.TryGetProperty("delta", out var delta) ? delta.GetString() : null),

            DotCraft.Protocol.AppServer.AppServerMethodNames.TurnCompleted => new OneShotNotification(
                OneShotNotificationKind.Completed,
                hasParams ? ReadFinalText(@params) : null),

            DotCraft.Protocol.AppServer.AppServerMethodNames.TurnFailed => new OneShotNotification(
                OneShotNotificationKind.Failed,
                hasParams && @params.TryGetProperty("error", out var error) ? error.GetString() : null),

            DotCraft.Protocol.AppServer.AppServerMethodNames.TurnCancelled => new OneShotNotification(OneShotNotificationKind.Cancelled, null),

            DotCraft.Protocol.AppServer.AppServerMethodNames.ItemStarted => ReadItemStarted(@params, hasParams),
            DotCraft.Protocol.AppServer.AppServerMethodNames.ItemCompleted => ReadItemCompleted(@params, hasParams),
            DotCraft.Protocol.AppServer.AppServerMethodNames.SystemEvent => ReadSystemEvent(@params, hasParams),

            _ => new OneShotNotification(OneShotNotificationKind.Ignored, null)
        };
    }

    private static OneShotNotification ReadItemStarted(JsonElement @params, bool hasParams)
    {
        if (!hasParams || !@params.TryGetProperty("item", out var item))
            return new OneShotNotification(OneShotNotificationKind.Ignored, null);

        var type = item.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
        if (type is not ("toolCall" or "commandExecution"))
            return new OneShotNotification(OneShotNotificationKind.Ignored, null);

        var payload = item.TryGetProperty("payload", out var p) ? p : default;
        var name = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("name", out var n)
            ? n.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(name)
            && payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("command", out var command))
        {
            name = command.GetString();
        }

        return new OneShotNotification(
            OneShotNotificationKind.Progress,
            string.IsNullOrWhiteSpace(name) ? $"[CLI] Started {type}." : $"[CLI] Started {type}: {name}");
    }

    private static OneShotNotification ReadItemCompleted(JsonElement @params, bool hasParams)
    {
        if (!hasParams || !@params.TryGetProperty("item", out var item))
            return new OneShotNotification(OneShotNotificationKind.Ignored, null);

        var type = item.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
        if (type == "agentMessage")
            return new OneShotNotification(OneShotNotificationKind.AgentCompleted, ReadAgentMessageText(item));

        if (type is not ("toolExecution" or "toolResult" or "commandExecution"))
            return new OneShotNotification(OneShotNotificationKind.Ignored, null);

        return new OneShotNotification(OneShotNotificationKind.Progress, $"[CLI] Completed {type}.");
    }

    private static string? ReadFinalText(JsonElement @params)
    {
        if (!@params.TryGetProperty("turn", out var turn)
            || !turn.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
            return null;

        string? last = null;
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var type) || type.GetString() != "agentMessage")
                continue;
            var text = ReadAgentMessageText(item);
            if (!string.IsNullOrEmpty(text))
                last = text;
        }

        return last;
    }

    private static string? ReadAgentMessageText(JsonElement item)
    {
        if (!item.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            return null;
        if (payload.TryGetProperty("deliveryMode", out var mode)
            && mode.ValueKind == JsonValueKind.String
            && mode.GetString() == "async")
            return null;

        return payload.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString()
            : null;
    }

    private static OneShotNotification ReadSystemEvent(JsonElement @params, bool hasParams)
    {
        if (!hasParams)
            return new OneShotNotification(OneShotNotificationKind.Ignored, null);

        var message = @params.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString()
            : null;
        return string.IsNullOrWhiteSpace(message)
            ? new OneShotNotification(OneShotNotificationKind.Ignored, null)
            : new OneShotNotification(OneShotNotificationKind.Progress, $"[CLI] {message}");
    }
}
