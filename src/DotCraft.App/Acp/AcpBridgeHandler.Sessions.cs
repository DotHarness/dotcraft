using System.Text.Json;
using DotCraft.Hooks;
using DotCraft.Sessions.Wire;
using Spectre.Console;
using Contract = DotCraft.Protocol.AppServer;
using ThreadConfiguration = DotCraft.Sessions.ThreadConfiguration;

namespace DotCraft.Acp;

public sealed partial class AcpBridgeHandler
{
    private async Task HandleSessionNewAsync(JsonRpcRequest request, CancellationToken ct)
    {
        if (!await EnsureInitialized(request)) return;

        var p = Deserialize<SessionNewParams>(request.Params);
        ThreadConfiguration? config = null;
        if (p?.McpServers is { Count: > 0 })
            config = new ThreadConfiguration { McpServers = ConvertToMcpConfigs(p.McpServers).ToArray() };

        using var startDoc = await wire.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new
            {
                channelName = "acp",
                userId = "local",
                channelContext = (string?)null,
                workspacePath
            },
            config,
            dynamicTools = RuntimeDynamicToolsOrNull(),
            historyMode = "server"
        }, ct: ct);

        ThrowIfWireError(startDoc, "thread/start");
        var threadEl = startDoc.RootElement.GetProperty("result").GetProperty("thread");
        var threadId = threadEl.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("thread/start returned no thread id");
        await ObserveSessionAsync(threadId, ct);
        try
        {
            var (mode, model) = ExtractThreadModeAndModel(threadEl);
            if (hookRunner != null)
                await hookRunner.RunAsync(HookEvent.SessionStart, new HookInput { SessionId = threadId }, ct);

            await acpTransport.SendResponseAsync(request.Id, new SessionNewResult
            {
                SessionId = threadId,
                ConfigOptions = await BuildConfigOptionsAsync(mode, model, ct)
            });

            await BroadcastSlashCommands(threadId);
            logger?.LogEvent($"Session created: {threadId}");
            AnsiConsole.MarkupLine($"[green][[ACP]][/] Session created: {Markup.Escape(threadId)}");
        }
        finally
        {
            await ReleaseSessionObservationAsync(threadId);
        }
    }

    private async Task HandleSessionLoadAsync(JsonRpcRequest request, CancellationToken ct)
    {
        if (!await EnsureInitialized(request)) return;

        var p = Deserialize<SessionLoadParams>(request.Params);
        if (p == null || string.IsNullOrWhiteSpace(p.SessionId))
        {
            await acpTransport.SendErrorAsync(request.Id, -32602, "Invalid params");
            return;
        }

        var sessionId = p.SessionId;

        await ObserveSessionAsync(sessionId, ct);
        try
        {
            if (p.McpServers is { Count: > 0 })
            {
                var config = new ThreadConfiguration { McpServers = ConvertToMcpConfigs(p.McpServers).ToArray() };
                using var configDoc = await wire.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadConfigUpdate,
                    new { threadId = sessionId, config }, ct: ct);
                ThrowIfWireError(configDoc, "thread/configUpdate");
            }

            using var resumeDoc = await wire.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadResume, new
            {
                threadId = sessionId,
                dynamicTools = RuntimeDynamicToolsOrNull()
            }, ct: ct);
            ThrowIfWireError(resumeDoc, "thread/resume");

            var threadEl = resumeDoc.RootElement.GetProperty("result").GetProperty("thread");
            var thread = JsonSerializer.Deserialize<SessionWireThread>(threadEl.GetRawText(), WireReadOptions);
            string? itemCursor = null;
            do
            {
                using var itemsDoc = await wire.SendRequestAsync(
                    DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadItemsList,
                    new { threadId = sessionId, cursor = itemCursor, limit = 100, sortDirection = "ascending" },
                    ct: ct);
                ThrowIfWireError(itemsDoc, "thread/items/list");
                var result = itemsDoc.RootElement.GetProperty("result");
                foreach (var entry in result.GetProperty("data").EnumerateArray())
                {
                    var item = JsonSerializer.Deserialize<SessionWireItem>(
                        entry.GetProperty("item").GetRawText(), WireReadOptions);
                    if (item != null) await ReplayItemAsAcpUpdate(sessionId, item);
                }
                itemCursor = result.TryGetProperty("nextCursor", out var cursorEl)
                    && cursorEl.ValueKind == JsonValueKind.String
                        ? cursorEl.GetString()
                        : null;
            } while (!string.IsNullOrWhiteSpace(itemCursor));

            var loadedMode = thread?.Configuration?.Mode ?? "agent";
            var loadedModel = thread?.Configuration?.Model;
            await acpTransport.SendResponseAsync(request.Id, new SessionLoadResult
            {
                SessionId = sessionId,
                ConfigOptions = await BuildConfigOptionsAsync(loadedMode, loadedModel, ct)
            });

            if (hookRunner != null)
                await hookRunner.RunAsync(HookEvent.SessionStart, new HookInput { SessionId = sessionId }, ct);

            await BroadcastSlashCommands(sessionId);
            logger?.LogEvent($"Session loaded: {sessionId}");
            AnsiConsole.MarkupLine($"[green][[ACP]][/] Session loaded: {Markup.Escape(sessionId)}");
        }
        finally
        {
            await ReleaseSessionObservationAsync(sessionId);
        }
    }

}
