using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Hooks;
using DotCraft.Sessions.Wire;
using DotHarness.Acp.Rpc;
using DotHarness.Acp.V1;
using Spectre.Console;
using Contract = DotCraft.Protocol.AppServer;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using ThreadConfiguration = DotCraft.Sessions.ThreadConfiguration;
using ThreadSummary = DotCraft.Sessions.ThreadSummary;

namespace DotCraft.Acp;

public sealed partial class AcpBridgeHandler
{
    public async ValueTask<NewSessionResponse> SessionNewAsync(NewSessionRequest parameters, CancellationToken ct)
    {
        ThreadConfiguration? config = null;
        if (parameters.McpServers is { Length: > 0 })
        {
            var converted = ConvertToMcpConfigs(parameters.McpServers);
            if (converted.Count > 0)
                config = new ThreadConfiguration { McpServers = [.. converted] };
        }

        using var startDoc = await wire.SendRequestAsync(Contract.AppServerMethodNames.ThreadStart, new
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
        }, ct: ct).ConfigureAwait(false);

        ThrowIfWireError(startDoc, "thread/start");
        var threadEl = startDoc.RootElement.GetProperty("result").GetProperty("thread");
        var threadId = threadEl.GetProperty("id").GetString()
            ?? throw new AcpRpcException(-32603, "thread/start returned no thread id");

        NewSessionResponse response;
        await ObserveSessionAsync(threadId, _bridgeLifetime).ConfigureAwait(false);
        try
        {
            var (mode, model) = ExtractThreadModeAndModel(threadEl);
            if (hookRunner != null)
                await hookRunner.RunAsync(HookEvent.SessionStart, new HookInput { SessionId = threadId }, ct).ConfigureAwait(false);

            var configOptions = await BuildConfigOptionsAsync(mode, model, ct).ConfigureAwait(false);
            logger?.LogEvent($"Session created: {threadId}");
            AnsiConsole.MarkupLine($"[green][[ACP]][/] Session created: {Markup.Escape(threadId)}");

            response = new NewSessionResponse
            {
                SessionId = new(threadId),
                Modes = AcpConfigOptions.BuildModeState(mode),
                ConfigOptions = configOptions
            };
        }
        finally
        {
            await ReleaseSessionObservationAsync(threadId).ConfigureAwait(false);
        }

        QueueSlashCommandsBroadcast(threadId);
        return response;
    }

    public async ValueTask<LoadSessionResponse> SessionLoadAsync(LoadSessionRequest parameters, CancellationToken ct)
    {
        var sessionId = parameters.SessionId.Value;
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new AcpRpcException(-32602, "Invalid params: sessionId is required.");

        LoadSessionResponse response;
        await ObserveSessionAsync(sessionId, _bridgeLifetime).ConfigureAwait(false);
        try
        {
            if (parameters.McpServers is { Length: > 0 })
            {
                var converted = ConvertToMcpConfigs(parameters.McpServers);
                if (converted.Count > 0)
                {
                    var config = new ThreadConfiguration { McpServers = [.. converted] };
                    using var configDoc = await wire.SendRequestAsync(Contract.AppServerMethodNames.ThreadConfigUpdate,
                        new { threadId = sessionId, config }, ct: ct).ConfigureAwait(false);
                    ThrowIfWireError(configDoc, "thread/configUpdate");
                }
            }

            using var resumeDoc = await wire.SendRequestAsync(Contract.AppServerMethodNames.ThreadResume, new
            {
                threadId = sessionId,
                dynamicTools = RuntimeDynamicToolsOrNull()
            }, ct: ct).ConfigureAwait(false);
            ThrowIfWireError(resumeDoc, "thread/resume");

            var threadEl = resumeDoc.RootElement.GetProperty("result").GetProperty("thread");
            var thread = JsonSerializer.Deserialize<SessionWireThread>(threadEl.GetRawText(), WireReadOptions);
            string? itemCursor = null;
            do
            {
                using var itemsDoc = await wire.SendRequestAsync(
                    Contract.AppServerMethodNames.ThreadItemsList,
                    new { threadId = sessionId, cursor = itemCursor, limit = 100, sortDirection = "ascending" },
                    ct: ct).ConfigureAwait(false);
                ThrowIfWireError(itemsDoc, "thread/items/list");
                var result = itemsDoc.RootElement.GetProperty("result");
                foreach (var entry in result.GetProperty("data").EnumerateArray())
                {
                    var item = JsonSerializer.Deserialize<SessionWireItem>(
                        entry.GetProperty("item").GetRawText(), WireReadOptions);
                    if (item != null)
                        await ReplayItemAsAcpUpdateAsync(sessionId, item, ct).ConfigureAwait(false);
                }

                itemCursor = result.TryGetProperty("nextCursor", out var cursorEl)
                    && cursorEl.ValueKind == JsonValueKind.String
                        ? cursorEl.GetString()
                        : null;
            } while (!string.IsNullOrWhiteSpace(itemCursor));

            var loadedMode = thread?.Configuration?.Mode ?? "agent";
            var loadedModel = thread?.Configuration?.Model;
            var configOptions = await BuildConfigOptionsAsync(loadedMode, loadedModel, ct).ConfigureAwait(false);

            if (hookRunner != null)
                await hookRunner.RunAsync(HookEvent.SessionStart, new HookInput { SessionId = sessionId }, ct).ConfigureAwait(false);

            logger?.LogEvent($"Session loaded: {sessionId}");
            AnsiConsole.MarkupLine($"[green][[ACP]][/] Session loaded: {Markup.Escape(sessionId)}");

            response = new LoadSessionResponse
            {
                Modes = AcpConfigOptions.BuildModeState(loadedMode),
                ConfigOptions = configOptions
            };
        }
        finally
        {
            await ReleaseSessionObservationAsync(sessionId).ConfigureAwait(false);
        }

        QueueSlashCommandsBroadcast(sessionId);
        return response;
    }

    public async ValueTask<ListSessionsResponse> SessionListAsync(ListSessionsRequest parameters, CancellationToken ct)
    {
        using var listDoc = await wire.SendRequestAsync(Contract.AppServerMethodNames.ThreadList, new
        {
            identity = new
            {
                channelName = "acp",
                userId = "local",
                channelContext = (string?)null,
                workspacePath
            }
        }, ct: ct).ConfigureAwait(false);

        ThrowIfWireError(listDoc, "thread/list");
        var dataEl = listDoc.RootElement.GetProperty("result").GetProperty("data");
        var summaries = JsonSerializer.Deserialize<List<ThreadSummary>>(dataEl.GetRawText(), WireReadOptions) ?? [];

        var sessions = summaries
            .Select(t => new SessionInfo
            {
                SessionId = new(t.Id),
                Cwd = workspacePath,
                UpdatedAt = t.LastActiveAt.ToString("O")
            })
            .ToArray();

        return new ListSessionsResponse { Sessions = sessions };
    }

    public async ValueTask<DeleteSessionResponse> SessionDeleteAsync(DeleteSessionRequest parameters, CancellationToken ct)
    {
        var sessionId = parameters.SessionId.Value;
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new AcpRpcException(-32602, "Invalid params: sessionId is required.");

        if (_activePrompts.TryRemove(sessionId, out var cts))
        {
            try { await cts.CancelAsync().ConfigureAwait(false); }
            catch (ObjectDisposedException) { }
        }

        _activeTurnIds.TryRemove(sessionId, out _);

        using var archiveDoc = await wire.SendRequestAsync(
            Contract.AppServerMethodNames.ThreadArchive,
            new { threadId = sessionId },
            ct: ct).ConfigureAwait(false);
        ThrowIfWireError(archiveDoc, "thread/archive");

        _knownSessions.TryRemove(sessionId, out _);
        return new DeleteSessionResponse();
    }

    public async ValueTask<SetSessionModeResponse> SessionSetModeAsync(SetSessionModeRequest parameters, CancellationToken ct)
    {
        var sessionId = parameters.SessionId.Value;
        var modeName = string.IsNullOrWhiteSpace(parameters.ModeId.Value)
            ? "agent"
            : parameters.ModeId.Value.Trim().ToLowerInvariant();

        using var modeDoc = await wire.SendRequestAsync(
            Contract.AppServerMethodNames.ThreadModeSet,
            new { threadId = sessionId, mode = modeName },
            ct: ct).ConfigureAwait(false);
        ThrowIfWireError(modeDoc, "thread/mode/set");

        var resolvedMode = modeName == "plan" ? AgentMode.Plan : AgentMode.Agent;
        await SendModeUpdateAsync(sessionId, resolvedMode, ct).ConfigureAwait(false);
        logger?.LogEvent($"Mode changed [session={sessionId}]: {modeName}");
        return new SetSessionModeResponse();
    }

    public async ValueTask<SetSessionConfigOptionResponse> SessionSetConfigOptionAsync(
        SetSessionConfigOptionRequest parameters,
        CancellationToken ct)
    {
        if (parameters is not SetSessionConfigOptionRequest.Case2 select)
            throw new AcpRpcException(-32602, "Only select config options are supported.");

        var sessionId = select.Value.SessionId.Value;
        var configId = select.Value.ConfigId.Value;
        var value = select.Value.Value.Value;

        if (configId == "mode")
        {
            var modeName = value.ToLowerInvariant();
            using var modeDoc = await wire.SendRequestAsync(
                Contract.AppServerMethodNames.ThreadModeSet,
                new { threadId = sessionId, mode = modeName },
                ct: ct).ConfigureAwait(false);
            ThrowIfWireError(modeDoc, "thread/mode/set");

            var current = await ReadThreadConfigurationAsync(sessionId, ct).ConfigureAwait(false);
            var updatedOptions = await BuildConfigOptionsAsync(modeName, current.Model, ct).ConfigureAwait(false);
            await SendConfigOptionsUpdateAsync(sessionId, updatedOptions, ct).ConfigureAwait(false);
            return new SetSessionConfigOptionResponse { ConfigOptions = updatedOptions };
        }

        if (configId == "model")
        {
            var current = await ReadThreadConfigurationAsync(sessionId, ct).ConfigureAwait(false);
            var availableOptions = await BuildConfigOptionsAsync(current.Mode, current.Model, ct).ConfigureAwait(false);
            var modelOption = availableOptions.FirstOrDefault(AcpConfigOptions.IsModelOption);
            if (modelOption == null)
                throw new AcpRpcException(-32602, "Model switching is unavailable for this session.");

            if (!AcpConfigOptions.ContainsSelectValue(modelOption, value))
                throw new AcpRpcException(-32602, $"Unknown model option: {value}");

            var nextModel = string.Equals(value, AcpConfigOptions.DefaultModelValue, StringComparison.Ordinal)
                ? null
                : value.Trim();

            current = await ReadThreadConfigurationAsync(sessionId, ct).ConfigureAwait(false);
            var config = current.Configuration ?? new JsonObject();
            SetCaseInsensitiveStringProperty(config, "model", nextModel);

            using var updateDoc = await wire.SendRequestAsync(
                Contract.AppServerMethodNames.ThreadConfigUpdate,
                new { threadId = sessionId, config },
                ct: ct).ConfigureAwait(false);
            ThrowIfWireError(updateDoc, "thread/config/update");

            var updatedOptions = await BuildConfigOptionsAsync(current.Mode, nextModel, ct).ConfigureAwait(false);
            await SendConfigOptionsUpdateAsync(sessionId, updatedOptions, ct).ConfigureAwait(false);
            logger?.LogEvent($"Model changed [session={sessionId}]: {nextModel ?? "Default"}");
            return new SetSessionConfigOptionResponse { ConfigOptions = updatedOptions };
        }

        throw new AcpRpcException(-32602, $"Unknown configId: {configId}");
    }

    private async Task<SessionConfigOption[]> BuildConfigOptionsAsync(
        string? currentMode = "agent",
        string? currentModel = null,
        CancellationToken ct = default)
    {
        Contract.ModelListResult? modelList = null;
        try
        {
            modelList = await wire.ModelListAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError("ACP model list unavailable", ex);
        }

        return AcpConfigOptions.Build(currentMode, currentModel, modelList);
    }

    private async Task<ThreadConfigurationState> ReadThreadConfigurationAsync(
        string threadId,
        CancellationToken ct)
    {
        using var readDoc = await wire.SendRequestAsync(
            Contract.AppServerMethodNames.ThreadRead,
            new { threadId },
            ct: ct).ConfigureAwait(false);

        ThrowIfWireError(readDoc, "thread/read");
        var threadEl = readDoc.RootElement.GetProperty("result").GetProperty("thread");
        var (mode, model) = ExtractThreadModeAndModel(threadEl);
        JsonObject? config = null;
        if (threadEl.TryGetProperty("configuration", out var configEl)
            && configEl.ValueKind == JsonValueKind.Object)
        {
            config = JsonNode.Parse(configEl.GetRawText()) as JsonObject;
        }

        return new ThreadConfigurationState(mode, model, config);
    }

    private static (string Mode, string? Model) ExtractThreadModeAndModel(JsonElement threadEl)
    {
        if (!threadEl.TryGetProperty("configuration", out var configEl)
            || configEl.ValueKind != JsonValueKind.Object)
        {
            return ("agent", null);
        }

        var mode = TryGetCaseInsensitiveString(configEl, "mode");
        var model = TryGetCaseInsensitiveString(configEl, "model");
        return (string.IsNullOrWhiteSpace(mode) ? "agent" : mode.Trim(), AcpConfigOptions.NormalizeOptionalModel(model));
    }

    private static List<McpServerConfig> ConvertToMcpConfigs(McpServer[] mcpServers)
    {
        var configs = new List<McpServerConfig>();
        foreach (var server in mcpServers)
        {
            switch (server)
            {
                case McpServer.Case3 stdio when !string.IsNullOrWhiteSpace(stdio.Value.Name):
                    configs.Add(new McpServerConfig
                    {
                        Name = stdio.Value.Name,
                        Enabled = true,
                        Transport = "stdio",
                        Command = stdio.Value.Command,
                        Arguments = [.. stdio.Value.Args],
                        EnvironmentVariables = stdio.Value.Env.ToDictionary(e => e.Name, e => e.Value),
                        Url = "",
                        Headers = new Dictionary<string, string>()
                    });
                    break;
                case McpServer.Case1 http when !string.IsNullOrWhiteSpace(http.Value.Name):
                    configs.Add(new McpServerConfig
                    {
                        Name = http.Value.Name,
                        Enabled = true,
                        Transport = "http",
                        Command = "",
                        Arguments = [],
                        EnvironmentVariables = new Dictionary<string, string>(),
                        Url = http.Value.Url,
                        Headers = http.Value.Headers.ToDictionary(h => h.Name, h => h.Value)
                    });
                    break;
            }
        }

        return configs;
    }
}
