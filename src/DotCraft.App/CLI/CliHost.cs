using System.Text;
using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Hosting;
using DotCraft.Workspaces;

namespace DotCraft.CLI;

/// <summary>
/// One-shot command-line host used by <c>dotcraft exec</c>.
/// </summary>
public sealed class CliHost(
    CommandLineArgs cliArgs,
    AppConfig config,
    DotCraftPaths paths) : IDotCraftHost
{
    private WebSocketClientConnection? _connection;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var request = cliArgs.Exec
            ?? throw new InvalidOperationException("dotcraft exec started without a request.");
        using var signal = new ExecInterruptSignal();
        await using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        var json = request.Json ? new ExecJsonEvents(stdout) : null;

        try
        {
            _connection = await ExecConnection.ConnectAsync(
                config.GetSection<CliConfig>("CLI"),
                paths.WorkspacePath,
                signal.Token).ConfigureAwait(false);
            var wire = _connection.Wire;
            var threadId = await ResolveThreadAsync(wire, request, signal.Token).ConfigureAwait(false);
            json?.ThreadStarted(threadId);

            var outcome = await RunTurnAsync(wire, threadId, request, signal, json).ConfigureAwait(false);
            await ReportAsync(outcome, request, json is null ? stdout : null).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (signal.IsRequested)
        {
            await FailAsync("DotCraft exec cancelled.", json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await FailAsync(ex.Message, json).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
            await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task<string> ResolveThreadAsync(
        AppServerWireClient wire,
        ExecRequest request,
        CancellationToken ct)
    {
        var resuming = request.ResumeThreadId != null || request.ResumeLast;
        var threadId = resuming
            ? request.ResumeThreadId ?? await ExecThreads.FindLatestAsync(wire, request.WorkspacePath, ct).ConfigureAwait(false)
            : await ExecThreads.StartAsync(wire, request, ct).ConfigureAwait(false);
        await Console.Error.WriteLineAsync($"[CLI] Thread: {threadId}").ConfigureAwait(false);

        if (resuming && (request.Model != null || request.AutoApprove))
            await ExecThreads.UpdateConfigAsync(wire, threadId, request.Model, request.AutoApprove, ct).ConfigureAwait(false);
        return threadId;
    }

    private static async Task<OneShotNotification> RunTurnAsync(
        AppServerWireClient wire,
        string threadId,
        ExecRequest request,
        ExecInterruptSignal signal,
        ExecJsonEvents? json)
    {
        wire.RegisterThreadChannel(threadId);
        try
        {
            // A Ctrl+C that arrives while turn/start is in flight interrupts the Turn once its id is known.
            var turnId = await ExecThreads.StartTurnAsync(wire, threadId, request, CancellationToken.None).ConfigureAwait(false);
            await using var interrupt = signal.Token.Register(
                () => _ = Task.Run(() => InterruptAsync(wire, threadId, turnId)));

            await foreach (var doc in wire.ReadThreadNotificationsAsync(threadId).ConfigureAwait(false))
            {
                using (doc)
                {
                    if (!BelongsToTurn(doc.RootElement, turnId))
                        continue;

                    json?.Project(doc.RootElement, turnId);
                    var notification = OneShotNotification.From(doc);
                    switch (notification.Kind)
                    {
                        case OneShotNotificationKind.Progress:
                            await Console.Error.WriteLineAsync(notification.Text).ConfigureAwait(false);
                            break;
                        case OneShotNotificationKind.Completed:
                        case OneShotNotificationKind.Failed:
                        case OneShotNotificationKind.Cancelled:
                            return notification;
                    }
                }
            }

            throw new IOException("Lost the connection to the AppServer before the turn finished.");
        }
        finally
        {
            wire.UnregisterThreadChannel(threadId);
        }
    }

    private static bool BelongsToTurn(JsonElement notification, string turnId)
    {
        if (!notification.TryGetProperty("params", out var @params) || @params.ValueKind != JsonValueKind.Object)
            return true;
        var id = OneShotNotification.ReadString(@params, "turnId")
                 ?? (@params.TryGetProperty("turn", out var turn) ? OneShotNotification.ReadString(turn, "id") : null);
        return id is null || id == turnId;
    }

    private static async Task InterruptAsync(AppServerWireClient wire, string threadId, string turnId)
    {
        await Console.Error.WriteLineAsync("[CLI] Interrupting the turn...").ConfigureAwait(false);
        try
        {
            await ExecThreads.InterruptTurnAsync(wire, threadId, turnId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[CLI] Failed to interrupt the turn: {ex.Message}").ConfigureAwait(false);
        }
    }

    private static async Task ReportAsync(OneShotNotification outcome, ExecRequest request, TextWriter? finalMessageOutput)
    {
        if (outcome.Kind != OneShotNotificationKind.Completed)
        {
            await Console.Error.WriteLineAsync(outcome.Text).ConfigureAwait(false);
            Environment.ExitCode = 1;
            return;
        }

        if (request.OutputLastMessagePath is { } path)
            await WriteLastMessageAsync(path, outcome.Text).ConfigureAwait(false);

        if (finalMessageOutput != null && !string.IsNullOrEmpty(outcome.Text))
            await finalMessageOutput.WriteLineAsync(outcome.Text).ConfigureAwait(false);
    }

    private static async Task WriteLastMessageAsync(string path, string? message)
    {
        try
        {
            await File.WriteAllTextAsync(path, message ?? string.Empty, new UTF8Encoding(false)).ConfigureAwait(false);
            if (message is null)
                await Console.Error.WriteLineAsync($"Warning: no last agent message; wrote empty content to {path}").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Console.Error.WriteLineAsync($"Failed to write last message file {path}: {ex.Message}").ConfigureAwait(false);
        }
    }

    private static async Task FailAsync(string message, ExecJsonEvents? json)
    {
        json?.Error(message);
        await Console.Error.WriteLineAsync(message).ConfigureAwait(false);
        Environment.ExitCode = 1;
    }
}
