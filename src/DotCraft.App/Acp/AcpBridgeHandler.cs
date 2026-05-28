using System.Collections.Concurrent;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Commands.Custom;
using DotCraft.Common;
using DotCraft.Hooks;
using DotCraft.Mcp;
using DotCraft.Memory;
using DotCraft.CLI;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using Spectre.Console;

namespace DotCraft.Acp;

/// <summary>
/// Translates between the IDE ACP protocol (stdio) and the AppServer Session Wire protocol.
/// </summary>
public sealed class AcpBridgeHandler(
    AcpTransport acpTransport,
    AppServerWireClient wire,
    string workspacePath,
    CustomCommandLoader? customCommandLoader = null,
    HookRunner? hookRunner = null,
    PlanStore? planStore = null,
    AcpLogger? logger = null,
    AppServerProcess? appServerProcess = null,
    int extForwardTimeoutSeconds = 120,
    int permissionRequestTimeoutSeconds = 120)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions WireReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if the JSON-RPC document contains an <c>error</c> field.
    /// </summary>
    private static void ThrowIfWireError(JsonDocument doc, string context)
    {
        if (!doc.RootElement.TryGetProperty("error", out var error))
            return;
        var message = error.TryGetProperty("message", out var msg)
            ? msg.GetString() ?? "Unknown error"
            : "Unknown error";
        throw new InvalidOperationException(string.IsNullOrEmpty(context) ? message : $"{context}: {message}");
    }

    private ClientCapabilities? _clientCapabilities;
    private bool _initialized;
    private readonly ConcurrentDictionary<string, string?> _activeTurnIds = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activePrompts = new();
    private readonly ConcurrentDictionary<string, string> _activeToolNames = new();
    private readonly ConcurrentDictionary<string, AcpRuntimeToolDescriptor> _runtimeToolDescriptors = new();
    private IReadOnlyList<DynamicToolSpec> _runtimeDynamicTools = [];

    private readonly AppServerProcess? _appServerProcess = appServerProcess;
    private readonly TimeSpan _extForwardTimeout = TimeSpan.FromSeconds(extForwardTimeoutSeconds);
    private readonly TimeSpan _permissionRequestTimeout = TimeSpan.FromSeconds(permissionRequestTimeoutSeconds);

    public const int ProtocolVersion = 1;
    internal const string DefaultModelValue = "__dotcraft_default__";

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var request = await acpTransport.ReadRequestAsync(ct);
            if (request == null) break;

            try
            {
                await DispatchAsync(request, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError($"Error handling {request.Method}", ex);
                AnsiConsole.MarkupLine($"[red][[ACP]] Error handling {Markup.Escape(request.Method)}: {Markup.Escape(ex.Message)}[/]");
                if (!request.IsNotification)
                    await acpTransport.SendErrorAsync(request.Id, -32603, ex.Message);
            }
        }
    }

    private async Task DispatchAsync(JsonRpcRequest request, CancellationToken ct)
    {
        switch (request.Method)
        {
            case AcpMethods.Initialize:
                await HandleInitializeAsync(request, ct).ConfigureAwait(false);
                break;
            case AcpMethods.SessionNew:
                await HandleSessionNewAsync(request, ct);
                break;
            case AcpMethods.SessionLoad:
                await HandleSessionLoadAsync(request, ct);
                break;
            case AcpMethods.SessionList:
                await HandleSessionListAsync(request, ct);
                break;
            case AcpMethods.DotCraftSessionDelete:
                await HandleDotCraftSessionDeleteAsync(request, ct);
                break;
            case AcpMethods.SessionPrompt:
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await HandleSessionPromptAsync(request, ct);
                    }
                    catch (Exception ex)
                    {
                        logger?.LogError("Unhandled prompt exception", ex);
                        await acpTransport.SendErrorAsync(request.Id, -32603, ex.Message);
                    }
                }, ct);
                break;
            case AcpMethods.SessionCancel:
                await HandleSessionCancelAsync(request, ct);
                break;
            case AcpMethods.SessionMode:
                await HandleSessionModeAsync(request, ct);
                break;
            case AcpMethods.SessionSetConfigOption:
                await HandleSessionSetConfigOptionAsync(request, ct);
                break;
            default:
                if (!request.IsNotification)
                    await acpTransport.SendErrorAsync(request.Id, -32601, $"Method not found: {request.Method}");
                break;
        }
    }

    private async Task HandleInitializeAsync(JsonRpcRequest request, CancellationToken ct)
    {
        var p = Deserialize<InitializeParams>(request.Params);
        _clientCapabilities = p?.ClientCapabilities;

        if (!TryBuildRuntimeTools(_clientCapabilities, out var dynamicTools, out var runtimeDescriptors, out var dynamicToolError))
        {
            await acpTransport.SendErrorAsync(request.Id, -32602, dynamicToolError);
            return;
        }

        _runtimeDynamicTools = dynamicTools;
        _runtimeToolDescriptors.Clear();
        foreach (var descriptor in runtimeDescriptors)
            _runtimeToolDescriptors[RuntimeToolKey(descriptor.Namespace, descriptor.Name)] = descriptor;

        var acpExt = BuildAcpExtensionCapability(_clientCapabilities);

        try
        {
            await wire.InitializeAsync(
                clientName: "dotcraft-acp",
                clientVersion: AppVersion.Informational,
                approvalSupport: true,
                streamingSupport: true,
                optOutMethods: null,
                acpExtensions: acpExt).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var detail = ex.Message;
            if (_appServerProcess is { IsRunning: false })
            {
                var stderr = _appServerProcess.RecentStderr.Trim();
                detail = $"AppServer subprocess exited (code {_appServerProcess.ExitCode}). " +
                         (string.IsNullOrEmpty(stderr) ? "No stderr captured." : stderr);
            }
            else if (ex is TaskCanceledException)
            {
                detail = "Wire request was canceled (AppServer connection closed or timed out).";
            }

            logger?.LogError("Wire initialize failed", ex);
            logger?.LogError(detail);
            await acpTransport.SendErrorAsync(request.Id, -32603, detail);
            return;
        }

        var version = AppVersion.Short;
        var result = new InitializeResult
        {
            ProtocolVersion = ProtocolVersion,
            AgentCapabilities = new AgentCapabilities
            {
                LoadSession = true,
                ListSessions = true,
                PromptCapabilities = new PromptCapabilities
                {
                    Text = true,
                    Image = true,
                    EmbeddedContext = true
                },
                McpCapabilities = new McpCapabilities { Http = true },
                Meta = new AgentCapabilitiesMeta
                {
                    DotCraft = new DotCraftAgentCapabilities { SessionDelete = true }
                }
            },
            AgentInfo = new AgentInfo
            {
                Name = "DotCraft",
                Title = "DotCraft AI Agent",
                Version = version
            }
        };

        _initialized = true;
        wire.ServerRequestHandler = OnWireServerRequestAsync;
        await acpTransport.SendResponseAsync(request.Id, result);

        var caps = new List<string>();
        if (_clientCapabilities?.Fs?.ReadTextFile == true) caps.Add("fs.read");
        if (_clientCapabilities?.Fs?.WriteTextFile == true) caps.Add("fs.write");
        if (_clientCapabilities?.Terminal?.Create == true) caps.Add("terminal");
        if (_clientCapabilities?.Extensions is { Count: > 0 } exts)
            caps.AddRange(exts.Select(e => $"ext:{e}"));
        if (_runtimeDynamicTools.Count > 0)
            caps.Add($"runtimeTools:{_runtimeDynamicTools.Count}");
        var capsStr = caps.Count > 0 ? string.Join(", ", caps) : "none";
        logger?.LogEvent($"Initialized (client capabilities: {capsStr})");
        AnsiConsole.MarkupLine($"[green][[ACP]][/] Initialized via AppServer (client capabilities: {Markup.Escape(capsStr)})");
    }

    internal static AcpExtensionCapability? BuildAcpExtensionCapability(ClientCapabilities? caps)
    {
        if (caps == null)
            return null;
        if (caps.Fs == null && caps.Terminal == null && caps.Extensions is not { Count: > 0 })
            return null;

        return new AcpExtensionCapability
        {
            FsReadTextFile = caps.Fs?.ReadTextFile == true ? true : null,
            FsWriteTextFile = caps.Fs?.WriteTextFile == true ? true : null,
            TerminalCreate = caps.Terminal?.Create == true ? true : null,
            Extensions = caps.Extensions is { Count: > 0 } ? [.. caps.Extensions] : null
        };
    }

    internal static bool TryBuildRuntimeTools(
        ClientCapabilities? caps,
        out IReadOnlyList<DynamicToolSpec> dynamicTools,
        out IReadOnlyList<AcpRuntimeToolDescriptor> descriptors,
        out string message)
    {
        dynamicTools = [];
        descriptors = [];
        message = string.Empty;

        var requested = caps?.Meta?.DotCraft?.RuntimeTools;
        if (requested is not { Count: > 0 })
            return true;

        var specs = new List<DynamicToolSpec>();
        var normalizedDescriptors = new List<AcpRuntimeToolDescriptor>();
        foreach (var descriptor in requested)
        {
            var name = descriptor.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                message = "runtimeTools[].name is required.";
                return false;
            }

            var description = descriptor.Description?.Trim();
            if (string.IsNullOrWhiteSpace(description))
            {
                message = $"runtime tool '{name}' must declare a description.";
                return false;
            }

            if (!TryNormalizeAcpRuntimeMethod(caps, descriptor.AcpMethod, out var acpMethod, out message))
                return false;

            var normalized = new AcpRuntimeToolDescriptor
            {
                Namespace = NormalizeOptionalString(descriptor.Namespace),
                Name = name,
                Description = description,
                InputSchema = descriptor.InputSchema?.DeepClone() as JsonObject,
                AcpMethod = acpMethod,
                Kind = NormalizeOptionalString(descriptor.Kind),
                DeferLoading = descriptor.DeferLoading,
                Approval = descriptor.Approval == null
                    ? null
                    : new AcpRuntimeToolApprovalDescriptor
                    {
                        Kind = descriptor.Approval.Kind,
                        TargetArgument = descriptor.Approval.TargetArgument,
                        Operation = descriptor.Approval.Operation,
                        OperationArgument = descriptor.Approval.OperationArgument
                    }
            };
            normalizedDescriptors.Add(normalized);
            specs.Add(new DynamicToolSpec
            {
                Namespace = normalized.Namespace,
                Name = normalized.Name,
                Description = normalized.Description,
                InputSchema = normalized.InputSchema?.DeepClone() as JsonObject,
                DeferLoading = normalized.DeferLoading,
                Approval = normalized.Approval == null
                    ? null
                    : new ChannelToolApprovalDescriptor
                    {
                        Kind = normalized.Approval.Kind,
                        TargetArgument = normalized.Approval.TargetArgument,
                        Operation = normalized.Approval.Operation,
                        OperationArgument = normalized.Approval.OperationArgument
                    }
            });
        }

        if (!WireDynamicToolProxy.TryValidateSpecs(specs, out message))
            return false;

        dynamicTools = specs;
        descriptors = normalizedDescriptors;
        return true;
    }

    private IReadOnlyList<DynamicToolSpec>? RuntimeDynamicToolsOrNull()
        => _runtimeDynamicTools.Count == 0 ? null : _runtimeDynamicTools;

    private static bool TryNormalizeAcpRuntimeMethod(
        ClientCapabilities? caps,
        string? rawMethod,
        out string method,
        out string message)
    {
        method = string.Empty;
        message = string.Empty;

        var trimmed = rawMethod?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            message = "runtimeTools[].acpMethod is required.";
            return false;
        }

        if (trimmed.StartsWith("ext/acp/", StringComparison.Ordinal))
            trimmed = trimmed["ext/acp/".Length..];

        var parts = trimmed
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            message = $"runtime tool acpMethod '{rawMethod}' must include a capability prefix and method name.";
            return false;
        }

        if (parts[0].Equals("fs", StringComparison.OrdinalIgnoreCase))
        {
            var supported = parts[1] switch
            {
                "readTextFile" => caps?.Fs?.ReadTextFile == true,
                "writeTextFile" => caps?.Fs?.WriteTextFile == true,
                _ => false
            };
            if (!supported)
            {
                message = $"runtime tool acpMethod '{rawMethod}' requires an advertised fs capability.";
                return false;
            }
        }
        else if (parts[0].Equals("terminal", StringComparison.OrdinalIgnoreCase))
        {
            if (caps?.Terminal?.Create != true)
            {
                message = $"runtime tool acpMethod '{rawMethod}' requires an advertised terminal capability.";
                return false;
            }
        }
        else if (caps?.Extensions?.Any(ext => ext.Equals(parts[0], StringComparison.OrdinalIgnoreCase)) != true)
        {
            message = $"runtime tool acpMethod '{rawMethod}' requires advertised extension '{parts[0]}'.";
            return false;
        }

        method = string.Join('/', parts);
        return true;
    }

    private static string RuntimeToolKey(string? @namespace, string name)
        => $"{NormalizeOptionalString(@namespace) ?? string.Empty}\u001F{name.Trim()}";

    private static string? NormalizeOptionalString(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private async Task HandleSessionNewAsync(JsonRpcRequest request, CancellationToken ct)
    {
        if (!await EnsureInitialized(request)) return;

        var p = Deserialize<SessionNewParams>(request.Params);
        ThreadConfiguration? config = null;
        if (p?.McpServers is { Count: > 0 })
            config = new ThreadConfiguration { McpServers = ConvertToMcpConfigs(p.McpServers).ToArray() };

        var startDoc = await wire.SendRequestAsync(AppServerMethods.ThreadStart, new
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

        var resumeDoc = await wire.SendRequestAsync(AppServerMethods.ThreadResume, new
        {
            threadId = sessionId,
            dynamicTools = RuntimeDynamicToolsOrNull()
        }, ct: ct);
        ThrowIfWireError(resumeDoc, "thread/resume");

        if (p.McpServers is { Count: > 0 })
        {
            var config = new ThreadConfiguration { McpServers = ConvertToMcpConfigs(p.McpServers).ToArray() };
            var configDoc = await wire.SendRequestAsync(AppServerMethods.ThreadConfigUpdate,
                new { threadId = sessionId, config }, ct: ct);
            ThrowIfWireError(configDoc, "thread/configUpdate");
        }

        var readDoc = await wire.SendRequestAsync(AppServerMethods.ThreadRead, new
        {
            threadId = sessionId,
            includeTurns = true
        }, ct: ct);

        ThrowIfWireError(readDoc, "thread/read");
        var threadEl = readDoc.RootElement.GetProperty("result").GetProperty("thread");
        var thread = JsonSerializer.Deserialize<SessionWireThread>(threadEl.GetRawText(), WireReadOptions);
        if (thread?.Turns != null)
        {
            foreach (var turn in thread.Turns)
            {
                if (turn.Items == null) continue;
                foreach (var item in turn.Items)
                {
                    await ReplayItemAsAcpUpdate(sessionId, item);
                }
            }
        }

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

    private async Task ReplayItemAsAcpUpdate(string sessionId, SessionWireItem item)
    {
        string? updateKind = null;
        if (item.Type == ItemType.UserMessage)
            updateKind = AcpUpdateKind.UserMessageChunk;
        else if (item.Type == ItemType.AgentMessage)
            updateKind = AcpUpdateKind.AgentMessageChunk;
        else if (item.Type == ItemType.ReasoningContent)
            updateKind = AcpUpdateKind.AgentThoughtChunk;

        if (updateKind == null)
            return;

        var text = ExtractTextFromPayload(item.Payload);
        if (text == null)
            return;

        await acpTransport.SendNotificationAsync(AcpMethods.SessionUpdate, new SessionUpdateParams
        {
            SessionId = sessionId,
            Update = new AcpSessionUpdate
            {
                SessionUpdate = updateKind,
                Content = new AcpContentBlock { Type = "text", Text = text }
            }
        });
    }

    /// <summary>
    /// Extracts message text from wire item payload. After JSON deserialization, <paramref name="payload"/>
    /// is typically a <see cref="JsonElement"/> rather than <see cref="UserMessagePayload"/> / <see cref="AgentMessagePayload"/>.
    /// </summary>
    internal static string? ExtractTextFromPayload(object? payload)
    {
        if (payload is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Object &&
                el.TryGetProperty("text", out var textEl) &&
                textEl.ValueKind == JsonValueKind.String)
                return textEl.GetString();
            return null;
        }

        if (payload is UserMessagePayload userMsg)
            return userMsg.Text;
        if (payload is AgentMessagePayload agentMsg)
            return agentMsg.Text;
        if (payload is ReasoningContentPayload reasoning)
            return reasoning.Text;
        return null;
    }

    internal static bool IsTodoProgressTool(string? toolName) =>
        string.Equals(toolName, "TodoWrite", StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, "UpdateTodos", StringComparison.OrdinalIgnoreCase);

    internal static List<AcpContentBlock>? BuildToolResultContent(
        string? toolName,
        object? resultObj,
        bool success)
    {
        if (success && IsTodoProgressTool(toolName))
            return null;

        var preview = ImageContentSanitizingChatClient.DescribeResult(resultObj);
        if (preview.Length > 500) preview = preview[..500] + "...";
        return new List<AcpContentBlock> { new() { Type = "text", Text = preview } };
    }

    private static string ToolCallKey(string sessionId, string callId) =>
        $"{sessionId}\u001F{callId}";

    private async Task HandleSessionListAsync(JsonRpcRequest request, CancellationToken ct)
    {
        if (!await EnsureInitialized(request)) return;

        var listDoc = await wire.SendRequestAsync(AppServerMethods.ThreadList, new
        {
            identity = new
            {
                channelName = "acp",
                userId = "local",
                channelContext = (string?)null,
                workspacePath
            }
        }, ct: ct);

        ThrowIfWireError(listDoc, "thread/list");
        var dataEl = listDoc.RootElement.GetProperty("result").GetProperty("data");
        var summaries = JsonSerializer.Deserialize<List<ThreadSummary>>(dataEl.GetRawText(), WireReadOptions) ?? [];

        var sessions = summaries
            .Select(t => new SessionListEntry
            {
                SessionId = t.Id,
                UpdatedAt = t.LastActiveAt.ToString("O"),
                Cwd = workspacePath
            })
            .ToList();

        await acpTransport.SendResponseAsync(request.Id, new SessionListResult { Sessions = sessions });
    }

    private async Task HandleDotCraftSessionDeleteAsync(JsonRpcRequest request, CancellationToken ct)
    {
        if (!await EnsureInitialized(request)) return;

        var p = Deserialize<SessionDeleteParams>(request.Params);
        if (p == null || string.IsNullOrWhiteSpace(p.SessionId))
        {
            await acpTransport.SendErrorAsync(request.Id, -32602, "Invalid params");
            return;
        }

        if (_activePrompts.TryRemove(p.SessionId, out var cts))
        {
            await cts.CancelAsync();
            cts.Dispose();
        }

        _activeTurnIds.TryRemove(p.SessionId, out _);

        await wire.SendRequestAsync(AppServerMethods.ThreadArchive, new { threadId = p.SessionId }, ct: ct);
        await acpTransport.SendResponseAsync(request.Id, new SessionDeleteResult());
    }

    private async Task HandleSessionPromptAsync(JsonRpcRequest request, CancellationToken ct)
    {
        if (!await EnsureInitialized(request)) return;

        var p = Deserialize<SessionPromptParams>(request.Params);
        if (p == null)
        {
            await acpTransport.SendErrorAsync(request.Id, -32602, "Invalid params");
            return;
        }

        var sessionId = p.SessionId;
        using var promptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _activePrompts[sessionId] = promptCts;

        var channelRegistered = false;
        try
        {
            var promptText = ExtractPromptText(p.Prompt);
            var commandExpanded = false;
            if (p.Command != null)
                promptText = $"/{p.Command} {promptText}".Trim();

            if (customCommandLoader != null && promptText.StartsWith('/'))
            {
                var resolved = customCommandLoader.TryResolve(promptText);
                if (resolved != null)
                {
                    promptText = resolved.ExpandedPrompt;
                    commandExpanded = true;
                }
            }

            List<SessionWireInputPart> inputParts;
            if (commandExpanded)
            {
                inputParts = [new SessionWireInputPart { Type = "text", Text = promptText }];
            }
            else if (p.Command != null)
            {
                inputParts = [new SessionWireInputPart { Type = "text", Text = promptText }];
                foreach (var block in p.Prompt)
                {
                    if (!string.IsNullOrEmpty(block.Data) &&
                        !string.IsNullOrEmpty(block.MimeType) &&
                        block.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        inputParts.Add(new SessionWireInputPart
                        {
                            Type = "image",
                            Url = $"data:{block.MimeType};base64,{block.Data}"
                        });
                    }
                }
            }
            else
            {
                inputParts = BuildWireInputParts(p.Prompt);
            }

            logger?.LogEvent(
                $"Prompt start [session={sessionId}]: {(promptText.Length > 200 ? promptText[..200] + "..." : promptText)}");

            // Route turn notifications to a per-session channel so concurrent prompts do not share one queue.
            wire.RegisterThreadChannel(sessionId);
            channelRegistered = true;
            var startDoc = await wire.SendRequestAsync(AppServerMethods.TurnStart, new
            {
                threadId = sessionId,
                input = inputParts
            }, timeout: TimeSpan.FromSeconds(120), ct: promptCts.Token);

            ThrowIfWireError(startDoc, "turn/start");
            var turnId = startDoc.RootElement.GetProperty("result").GetProperty("turn").GetProperty("id").GetString();
            if (!string.IsNullOrEmpty(turnId))
                _activeTurnIds[sessionId] = turnId;

            var turnFailed = false;
            string? turnFailMessage = null;
            var turnCancelled = false;
            await foreach (var notif in wire.ReadThreadTurnNotificationsAsync(sessionId, timeout: TimeSpan.FromHours(2),
                         ct: promptCts.Token))
            {
                if (!notif.RootElement.TryGetProperty("method", out var mEl))
                    continue;
                var method = mEl.GetString() ?? "";
                if (!notif.RootElement.TryGetProperty("params", out var @params))
                    continue;

                if (method == AppServerMethods.TurnFailed)
                {
                    turnFailed = true;
                    if (@params.TryGetProperty("error", out var err))
                        turnFailMessage = err.GetString();
                }
                else if (method == AppServerMethods.TurnCancelled)
                {
                    turnCancelled = true;
                }

                await MapWireNotificationToAcp(sessionId, method, @params);
                // Yield to let concurrent server requests (permission, ext/acp) acquire the write lock,
                // preventing lock convoy during streaming bursts (see dotcraft-unity disconnection issue).
                await Task.Yield();
            }

            if (turnFailed)
            {
                await acpTransport.SendErrorAsync(request.Id, -32603, turnFailMessage ?? "Turn failed");
                return;
            }

            if (turnCancelled)
            {
                await acpTransport.SendResponseAsync(request.Id, new SessionPromptResult { StopReason = AcpStopReason.Cancelled });
                return;
            }

            if (planStore != null && planStore.StructuredPlanExists(sessionId))
            {
                var structuredPlan = await planStore.LoadStructuredPlanAsync(sessionId);
                if (structuredPlan != null)
                    await SendPlanUpdate(sessionId, structuredPlan);
            }

            logger?.LogEvent($"Prompt complete [session={sessionId}] stop=end_turn (AppServer wire)");
            await acpTransport.SendResponseAsync(request.Id, new SessionPromptResult { StopReason = AcpStopReason.EndTurn });
        }
        catch (OperationCanceledException)
        {
            logger?.LogEvent($"Prompt cancelled [session={sessionId}]");
            await acpTransport.SendResponseAsync(request.Id, new SessionPromptResult { StopReason = AcpStopReason.Cancelled });
        }
        catch (Exception ex)
        {
            logger?.LogError($"Prompt error [session={sessionId}]", ex);
            AnsiConsole.MarkupLine($"[red][[ACP]][/] Prompt error: {Markup.Escape(ex.Message)}");
            await acpTransport.SendErrorAsync(request.Id, -32603, ex.Message);
        }
        finally
        {
            if (channelRegistered)
                wire.UnregisterThreadChannel(sessionId);
            _activePrompts.TryRemove(sessionId, out _);
            _activeTurnIds.TryRemove(sessionId, out _);
        }
    }

    /// <summary>
    /// Single permanent handler: routes server-initiated requests to the active prompt.
    /// <c>item/approval/request</c> may omit <c>threadId</c> when exactly one prompt is active;
    /// <c>ext/acp/*</c> and <c>item/tool/call</c> require <c>threadId</c> per spec.
    /// </summary>
    private async Task<object?> OnWireServerRequestAsync(JsonDocument doc)
    {
        var root = doc.RootElement;
        if (!root.TryGetProperty("params", out var wireParams))
            return new { decision = "decline" };

        if (!root.TryGetProperty("method", out var methodEl))
        {
            logger?.LogEvent("Wire server request missing method; declining");
            return new { decision = "decline" };
        }

        var method = methodEl.GetString() ?? "";

        string? threadId = null;
        if (wireParams.TryGetProperty("threadId", out var tidEl) && tidEl.ValueKind == JsonValueKind.String)
            threadId = tidEl.GetString();

        string? sessionId = null;

        if (method == AppServerMethods.ItemApprovalRequest)
        {
            if (!string.IsNullOrEmpty(threadId) && _activePrompts.ContainsKey(threadId))
                sessionId = threadId;
            else if (string.IsNullOrEmpty(threadId))
            {
                if (_activePrompts.Count == 1)
                {
                    foreach (var kv in _activePrompts)
                    {
                        sessionId = kv.Key;
                        break;
                    }
                }
                else
                {
                    logger?.LogEvent(
                        $"item/approval/request without threadId; active prompts={_activePrompts.Count}, declining");
                    return new { decision = "decline" };
                }
            }
            else
            {
                logger?.LogEvent($"item/approval/request for inactive threadId={threadId}");
                return new { decision = "decline" };
            }
        }
        else if (method == AppServerMethods.ItemToolCall)
        {
            if (string.IsNullOrEmpty(threadId))
            {
                logger?.LogEvent("item/tool/call wire server request missing threadId; declining");
                return RuntimeToolFailed("InvalidRequest", "Dynamic tool request did not include threadId.");
            }

            if (!_activePrompts.ContainsKey(threadId))
            {
                logger?.LogEvent($"item/tool/call for inactive threadId={threadId}");
                return RuntimeToolFailed("InactiveThread", "Dynamic tool request targeted an inactive ACP session.");
            }

            sessionId = threadId;
        }
        else if (method.StartsWith("ext/acp/", StringComparison.Ordinal))
        {
            if (string.IsNullOrEmpty(threadId))
            {
                logger?.LogEvent("ext/acp wire server request missing threadId; declining");
                return new { decision = "decline" };
            }

            if (!_activePrompts.ContainsKey(threadId))
            {
                logger?.LogEvent($"Wire server request for inactive threadId={threadId}");
                return new { decision = "decline" };
            }

            sessionId = threadId;
        }
        else
        {
            logger?.LogEvent($"Wire server request unknown method={method}; declining");
            return new { decision = "decline" };
        }

        if (string.IsNullOrEmpty(sessionId) || !_activePrompts.TryGetValue(sessionId, out var cts))
        {
            logger?.LogEvent($"Wire server request could not resolve active session for method={method}");
            if (method == AppServerMethods.ItemToolCall)
                return RuntimeToolFailed("InactiveThread", "Dynamic tool request targeted an inactive ACP session.");
            return new { decision = "decline" };
        }

        CancellationToken token;
        try
        {
            token = cts.Token;
        }
        catch (ObjectDisposedException)
        {
            logger?.LogEvent($"Wire server request for disposed prompt CTS sessionId={sessionId}");
            if (method == AppServerMethods.ItemToolCall)
                return RuntimeToolFailed("InactiveThread", "Dynamic tool request targeted an inactive ACP session.");
            return new { decision = "decline" };
        }

        return await HandleWireServerRequestAsync(doc, sessionId, token).ConfigureAwait(false);
    }

    private async Task<object?> HandleWireServerRequestAsync(JsonDocument doc, string sessionId, CancellationToken ct)
    {
        var root = doc.RootElement;
        if (!root.TryGetProperty("method", out var mEl))
            return new { decision = "decline" };
        var method = mEl.GetString() ?? "";
        if (!root.TryGetProperty("params", out var wireParams))
            return new { decision = "decline" };

        if (method == AppServerMethods.ItemApprovalRequest)
        {
            var approvalType = wireParams.TryGetProperty("approvalType", out var at) ? at.GetString() : null;
            var operation = wireParams.TryGetProperty("operation", out var op) ? op.GetString() ?? "" : "";
            var target = wireParams.TryGetProperty("target", out var tg) ? tg.GetString() ?? "" : "";

            var toolKind = approvalType == "shell"
                ? AcpToolKind.Execute
                : operation.ToLowerInvariant() switch
                {
                    "write" or "edit" => AcpToolKind.Edit,
                    "delete" => AcpToolKind.Delete,
                    _ => AcpToolKind.Read
                };
            var reqId = wireParams.TryGetProperty("requestId", out var rid) ? rid.GetString() ?? "" : "";
            var toolCall = new AcpToolCallInfo
            {
                ToolCallId = reqId,
                Title = approvalType == "shell"
                    ? $"Shell: {(operation.Length > 80 ? operation[..80] + "..." : operation)}"
                    : $"File {operation}: {target}",
                Kind = toolKind,
                Status = AcpToolStatus.Pending
            };
            var permParams = new RequestPermissionParams
            {
                SessionId = sessionId,
                ToolCall = toolCall,
                Options =
                [
                    new PermissionOption { OptionId = "allow-once", Name = "Allow once", Kind = AcpPermissionKind.AllowOnce },
                    new PermissionOption { OptionId = "allow-always", Name = "Allow always", Kind = AcpPermissionKind.AllowAlways },
                    new PermissionOption { OptionId = "reject-once", Name = "Reject", Kind = AcpPermissionKind.RejectOnce },
                ]
            };

            try
            {
                var resultElement = await acpTransport.SendClientRequestAsync(
                    AcpMethods.RequestPermission, permParams,
                    timeout: _permissionRequestTimeout, ct: ct);
                var result = resultElement.Deserialize<RequestPermissionResult>(JsonOptions);
                var decision = result?.Outcome?.OptionId switch
                {
                    "allow-always" => "acceptAlways",
                    "allow-once" => "accept",
                    _ when result?.Outcome?.Outcome == "cancelled" => "cancel",
                    _ => "decline"
                };
                return new { decision };
            }
            catch
            {
                return new { decision = "decline" };
            }
        }

        if (method == AppServerMethods.ItemToolCall)
            return await HandleRuntimeToolCallAsync(wireParams, ct);

        if (method.StartsWith("ext/acp/", StringComparison.Ordinal))
        {
            var acpMethod = method["ext/acp/".Length..];
            if (string.IsNullOrEmpty(acpMethod))
                return new { error = "bad ext method" };
            var ideMethod = MapExtPathToAcpMethod(acpMethod);
            try
            {
                var paramObj = ParamsForIdeExtForward(wireParams);
                var el = await acpTransport.SendClientRequestAsync(ideMethod, paramObj, ct,
                    timeout: _extForwardTimeout).ConfigureAwait(false);
                return JsonSerializer.Deserialize<object>(el.GetRawText(), JsonOptions) ?? new { };
            }
            catch
            {
                return new { error = "ext forward failed" };
            }
        }

        return new { decision = "decline" };
    }

    private async Task<DynamicToolCallResult> HandleRuntimeToolCallAsync(JsonElement wireParams, CancellationToken ct)
    {
        var toolName = TryGetStringProperty(wireParams, "tool");
        if (string.IsNullOrWhiteSpace(toolName))
            return RuntimeToolFailed("InvalidRequest", "Dynamic tool request did not include tool name.");

        var @namespace = TryGetStringProperty(wireParams, "namespace");
        if (!_runtimeToolDescriptors.TryGetValue(RuntimeToolKey(@namespace, toolName), out var descriptor))
        {
            return RuntimeToolFailed(
                "UnsupportedTool",
                $"Dynamic tool '{toolName}' is not registered for this ACP session.");
        }

        var arguments = new JsonObject();
        if (wireParams.TryGetProperty("arguments", out var args) && args.ValueKind != JsonValueKind.Null)
        {
            if (JsonNode.Parse(args.GetRawText()) is JsonObject parsed)
                arguments = parsed;
        }

        try
        {
            var result = await acpTransport.SendClientRequestAsync(
                descriptor.AcpMethod,
                arguments,
                ct,
                timeout: _extForwardTimeout).ConfigureAwait(false);
            return new DynamicToolCallResult
            {
                Success = true,
                StructuredResult = JsonNode.Parse(result.GetRawText())
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return RuntimeToolFailed("DynamicToolTimeout", $"Dynamic tool '{toolName}' timed out.");
        }
        catch (Exception ex)
        {
            return RuntimeToolFailed("DynamicToolFailed", ex.Message);
        }
    }

    private static DynamicToolCallResult RuntimeToolFailed(string code, string message)
        => new()
        {
            Success = false,
            ErrorCode = code,
            ErrorMessage = message,
            ContentItems = [new ExtChannelToolContentItem { Type = "text", Text = $"{code}: {message}" }]
        };

    /// <summary>
    /// Strips routing-only <c>threadId</c> from wire params before forwarding <c>ext/acp/*</c> to the IDE.
    /// </summary>
    internal static object? ParamsForIdeExtForward(JsonElement wireParams)
    {
        if (wireParams.ValueKind != JsonValueKind.Object)
            return JsonSerializer.Deserialize<object>(wireParams.GetRawText(), JsonOptions);

        if (!wireParams.TryGetProperty("threadId", out _))
            return JsonSerializer.Deserialize<object>(wireParams.GetRawText(), JsonOptions);

        if (JsonNode.Parse(wireParams.GetRawText()) is not JsonObject obj)
            return JsonSerializer.Deserialize<object>(wireParams.GetRawText(), JsonOptions);

        obj.Remove("threadId");
        return obj;
    }

    /// <summary>
    /// Maps <c>fs/readTextFile</c> wire suffix to ACP <c>fs/readTextFile</c> method name.
    /// </summary>
    private static string MapExtPathToAcpMethod(string pathUnderExtAcp)
    {
        var parts = pathUnderExtAcp.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && parts[0] == "fs")
            return $"fs/{parts[1]}";
        if (parts.Length >= 2 && parts[0] == "terminal")
            return $"terminal/{parts[1]}";
        return string.Join('/', parts);
    }

    private async Task MapWireNotificationToAcp(string sessionId, string method, JsonElement @params)
    {
        switch (method)
        {
            case AppServerMethods.ItemAgentMessageDelta:
            {
                var delta = @params.TryGetProperty("delta", out var d) ? d.GetString() : null;
                if (!string.IsNullOrEmpty(delta))
                    await SendMessageChunk(sessionId, delta);
                break;
            }
            case AppServerMethods.ItemReasoningDelta:
            {
                var delta = @params.TryGetProperty("delta", out var d) ? d.GetString() : null;
                if (!string.IsNullOrEmpty(delta))
                    await SendThoughtChunk(sessionId, delta);
                break;
            }
            case AppServerMethods.ItemStarted:
            {
                if (!@params.TryGetProperty("item", out var item)) break;
                var type = item.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type is not ("toolCall" or "dynamicToolCall")) break;
                if (!item.TryGetProperty("payload", out var payload)) break;
                var toolName = payload.TryGetProperty("toolName", out var tn) ? tn.GetString() ?? "" : "";
                var callId = payload.TryGetProperty("callId", out var ci) ? ci.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(callId))
                    _activeToolNames[ToolCallKey(sessionId, callId)] = toolName;
                string? argsStr = null;
                if (payload.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Object)
                {
                    try
                    {
                        argsStr = JsonSerializer.Serialize(args, JsonOptions);
                    }
                    catch
                    {
                        argsStr = args.ToString();
                    }
                }

                var kind = AcpToolKindMapper.GetKind(toolName, ResolveRuntimeToolKind(payload, toolName));
                IDictionary<string, object?>? argsDict = null;
                if (!string.IsNullOrEmpty(argsStr))
                {
                    try
                    {
                        argsDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(argsStr, JsonOptions);
                    }
                    catch
                    {
                        /* ignore */
                    }
                }

                var filePaths = AcpToolKindMapper.ExtractFilePaths(toolName, argsDict);
                await acpTransport.SendNotificationAsync(AcpMethods.SessionUpdate, new SessionUpdateParams
                {
                    SessionId = sessionId,
                    Update = new AcpSessionUpdate
                    {
                        SessionUpdate = AcpUpdateKind.ToolCall,
                        ToolCallId = callId,
                        Title = toolName,
                        Kind = kind,
                        Status = AcpToolStatus.InProgress,
                        Content = string.IsNullOrEmpty(argsStr)
                            ? null
                            : new List<AcpContentBlock> { new() { Type = "text", Text = argsStr } },
                        FileLocations = filePaths?.Select(fp => new AcpFileLocation { Uri = $"file://{fp}" }).ToList()
                    }
                });
                break;
            }
            case AppServerMethods.ItemCompleted:
            {
                if (!@params.TryGetProperty("item", out var item)) break;
                var type = item.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type is not ("toolResult" or "dynamicToolCall")) break;
                if (!item.TryGetProperty("payload", out var payload)) break;
                var callId = payload.TryGetProperty("callId", out var ci) ? ci.GetString() ?? "" : "";
                object? resultObj = null;
                if (type == "dynamicToolCall")
                {
                    resultObj = ExtractDynamicToolResult(payload);
                }
                else if (payload.TryGetProperty("result", out var r))
                {
                    resultObj = r.ValueKind == JsonValueKind.String
                        ? r.GetString()
                        : JsonSerializer.Deserialize<object>(r.GetRawText(), JsonOptions);
                }

                var success = payload.TryGetProperty("success", out var su) ? su.GetBoolean() : true;
                var toolName = payload.TryGetProperty("toolName", out var tn) ? tn.GetString() : null;
                if (!string.IsNullOrEmpty(callId)
                    && _activeToolNames.TryRemove(ToolCallKey(sessionId, callId), out var trackedToolName)
                    && string.IsNullOrEmpty(toolName))
                {
                    toolName = trackedToolName;
                }
                var content = BuildToolResultContent(toolName, resultObj, success);

                await acpTransport.SendNotificationAsync(AcpMethods.SessionUpdate, new SessionUpdateParams
                {
                    SessionId = sessionId,
                    Update = new AcpSessionUpdate
                    {
                        SessionUpdate = AcpUpdateKind.ToolCallUpdate,
                        ToolCallId = callId,
                        Status = success ? AcpToolStatus.Completed : AcpToolStatus.Failed,
                        Content = content
                    }
                });
                break;
            }
            case AppServerMethods.TurnCompleted:
            {
                break;
            }
            case AppServerMethods.PlanUpdated:
            {
                if (!@params.TryGetProperty("todos", out var todosEl) || todosEl.ValueKind != JsonValueKind.Array)
                    break;
                var entries = new List<AcpPlanEntry>();
                foreach (var todo in todosEl.EnumerateArray())
                {
                    var content = todo.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                    var priority = todo.TryGetProperty("priority", out var pr) ? pr.GetString() : "medium";
                    var status = todo.TryGetProperty("status", out var st) ? st.GetString() : "pending";
                    entries.Add(new AcpPlanEntry
                    {
                        Content = content,
                        Priority = priority switch
                        {
                            "high" => AcpPlanEntryPriority.High,
                            "low" => AcpPlanEntryPriority.Low,
                            _ => AcpPlanEntryPriority.Medium
                        },
                        Status = status switch
                        {
                            "inProgress" => AcpToolStatus.InProgress,
                            "completed" or "cancelled" => AcpToolStatus.Completed,
                            _ => AcpToolStatus.Pending
                        }
                    });
                }

                await acpTransport.SendNotificationAsync(AcpMethods.SessionUpdate, new SessionUpdateParams
                {
                    SessionId = sessionId,
                    Update = new AcpSessionUpdate
                    {
                        SessionUpdate = AcpUpdateKind.Plan,
                        Entries = entries
                    }
                });
                break;
            }
        }
    }

    private string? ResolveRuntimeToolKind(JsonElement payload, string toolName)
    {
        var @namespace = TryGetStringProperty(payload, "namespace");
        return _runtimeToolDescriptors.TryGetValue(RuntimeToolKey(@namespace, toolName), out var descriptor)
            ? descriptor.Kind
            : null;
    }

    private static object? ExtractDynamicToolResult(JsonElement payload)
    {
        if (payload.TryGetProperty("structuredResult", out var structuredResult)
            && structuredResult.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            return JsonSerializer.Deserialize<object>(structuredResult.GetRawText(), JsonOptions);
        }

        if (payload.TryGetProperty("contentItems", out var contentItems)
            && contentItems.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            return JsonSerializer.Deserialize<object>(contentItems.GetRawText(), JsonOptions);
        }

        if (payload.TryGetProperty("errorMessage", out var error)
            && error.ValueKind == JsonValueKind.String)
        {
            return error.GetString();
        }

        return null;
    }

    private async Task SendMessageChunk(string sessionId, string text)
    {
        await SendContentChunk(sessionId, AcpUpdateKind.AgentMessageChunk, text);
    }

    private async Task SendThoughtChunk(string sessionId, string text)
    {
        await SendContentChunk(sessionId, AcpUpdateKind.AgentThoughtChunk, text);
    }

    private async Task SendContentChunk(string sessionId, string updateKind, string text)
    {
        await acpTransport.SendNotificationAsync(AcpMethods.SessionUpdate, new SessionUpdateParams
        {
            SessionId = sessionId,
            Update = new AcpSessionUpdate
            {
                SessionUpdate = updateKind,
                Content = new AcpContentBlock { Type = "text", Text = text }
            }
        });
    }

    private async Task SendPlanUpdate(string sessionId, StructuredPlan plan)
    {
        var entries = plan.Todos.Select(t => new AcpPlanEntry
        {
            Content = t.Content,
            Priority = t.Priority switch
            {
                PlanTodoPriority.High => AcpPlanEntryPriority.High,
                PlanTodoPriority.Low => AcpPlanEntryPriority.Low,
                _ => AcpPlanEntryPriority.Medium
            },
            Status = t.Status switch
            {
                PlanTodoStatus.InProgress => AcpToolStatus.InProgress,
                PlanTodoStatus.Completed or PlanTodoStatus.Cancelled => AcpToolStatus.Completed,
                _ => AcpToolStatus.Pending
            }
        }).ToList();

        await acpTransport.SendNotificationAsync(AcpMethods.SessionUpdate, new SessionUpdateParams
        {
            SessionId = sessionId,
            Update = new AcpSessionUpdate
            {
                SessionUpdate = AcpUpdateKind.Plan,
                Entries = entries
            }
        });
    }

    private async Task HandleSessionCancelAsync(JsonRpcRequest request, CancellationToken ct)
    {
        var p = Deserialize<SessionCancelParams>(request.Params);
        if (p == null) return;

        if (_activePrompts.TryGetValue(p.SessionId, out var cts))
        {
            try { await cts.CancelAsync(); }
            catch (ObjectDisposedException) { /* prompt already finished and disposed its CTS */ }
        }

        if (_activeTurnIds.TryGetValue(p.SessionId, out var turnId) && !string.IsNullOrEmpty(turnId))
        {
            try
            {
                await wire.SendRequestAsync(AppServerMethods.TurnInterrupt, new
                {
                    threadId = p.SessionId,
                    turnId
                }, ct: ct);
            }
            catch
            {
                // ignored
            }
        }

        logger?.LogEvent($"Session cancel requested: {p.SessionId}");
        AnsiConsole.MarkupLine($"[yellow][[ACP]][/] Cancelled session: {Markup.Escape(p.SessionId)}");
    }

    private async Task HandleSessionModeAsync(JsonRpcRequest request, CancellationToken ct)
    {
        if (!await EnsureInitialized(request)) return;

        var p = Deserialize<SessionModeParams>(request.Params);
        if (p == null)
        {
            await acpTransport.SendErrorAsync(request.Id, -32602, "Invalid params");
            return;
        }

        var modeName = p.Mode?.ToLowerInvariant() ?? "agent";
        await wire.SendRequestAsync(AppServerMethods.ThreadModeSet, new { threadId = p.SessionId, mode = modeName },
            ct: ct);

        var resolvedMode = modeName == "plan" ? AgentMode.Plan : AgentMode.Agent;
        await acpTransport.SendResponseAsync(request.Id, new { mode = resolvedMode.ToString().ToLower() });
        await SendModeUpdate(p.SessionId, resolvedMode);
        logger?.LogEvent($"Mode changed [session={p.SessionId}]: {modeName}");
    }

    private async Task HandleSessionSetConfigOptionAsync(JsonRpcRequest request, CancellationToken ct)
    {
        if (!await EnsureInitialized(request)) return;

        var p = Deserialize<SessionSetConfigOptionParams>(request.Params);
        if (p == null)
        {
            await acpTransport.SendErrorAsync(request.Id, -32602, "Invalid params");
            return;
        }

        if (p.ConfigId == "mode")
        {
            var modeName = p.Value.ToLowerInvariant();
            await wire.SendRequestAsync(AppServerMethods.ThreadModeSet,
                new { threadId = p.SessionId, mode = modeName }, ct: ct);

            var current = await ReadThreadConfigurationAsync(p.SessionId, includeTurns: false, ct);
            var updatedOptions = await BuildConfigOptionsAsync(modeName, current.Model, ct);
            await acpTransport.SendResponseAsync(request.Id, new SessionSetConfigOptionResult { ConfigOptions = updatedOptions });

            await SendConfigOptionsUpdate(p.SessionId, updatedOptions);
            return;
        }

        if (p.ConfigId == "model")
        {
            var current = await ReadThreadConfigurationAsync(p.SessionId, includeTurns: false, ct);
            var availableOptions = await BuildConfigOptionsAsync(current.Mode, current.Model, ct);
            var modelOption = availableOptions.FirstOrDefault(o => IsModelConfigOption(o));
            if (modelOption == null)
            {
                await acpTransport.SendErrorAsync(request.Id, -32602, "Model switching is unavailable for this session.");
                return;
            }

            if (modelOption.Options.All(o => !string.Equals(o.Value, p.Value, StringComparison.Ordinal)))
            {
                await acpTransport.SendErrorAsync(request.Id, -32602, $"Unknown model option: {p.Value}");
                return;
            }

            var nextModel = string.Equals(p.Value, DefaultModelValue, StringComparison.Ordinal)
                ? null
                : p.Value.Trim();
            await wire.WorkspaceConfigUpdateAsync(nextModel, ct);

            current = await ReadThreadConfigurationAsync(p.SessionId, includeTurns: false, ct);
            var config = current.Configuration ?? new JsonObject();
            SetCaseInsensitiveStringProperty(config, "model", nextModel);

            var updateDoc = await wire.SendRequestAsync(AppServerMethods.ThreadConfigUpdate,
                new { threadId = p.SessionId, config }, ct: ct);
            ThrowIfWireError(updateDoc, "thread/config/update");

            var updatedOptions = await BuildConfigOptionsAsync(current.Mode, nextModel, ct);
            await acpTransport.SendResponseAsync(request.Id, new SessionSetConfigOptionResult { ConfigOptions = updatedOptions });
            await SendConfigOptionsUpdate(p.SessionId, updatedOptions);
            logger?.LogEvent($"Model changed [session={p.SessionId}]: {nextModel ?? "Default"}");
            return;
        }

        await acpTransport.SendErrorAsync(request.Id, -32602, $"Unknown configId: {p.ConfigId}");
    }

    private async Task SendModeUpdate(string sessionId, AgentMode mode)
    {
        await acpTransport.SendNotificationAsync(AcpMethods.SessionUpdate, new SessionUpdateParams
        {
            SessionId = sessionId,
            Update = new AcpSessionUpdate
            {
                SessionUpdate = AcpUpdateKind.CurrentModeUpdate,
                Content = new AcpContentBlock { Type = "text", Text = mode.ToString().ToLower() }
            }
        });
    }

    private async Task BroadcastSlashCommands(string sessionId)
    {
        if (customCommandLoader == null) return;

        var commands = customCommandLoader.ListCommands()
            .Select(c => new AcpSlashCommand
            {
                Name = c.Name,
                Description = string.IsNullOrWhiteSpace(c.Description) ? null : c.Description
            })
            .ToList();

        if (commands.Count == 0) return;

        await acpTransport.SendNotificationAsync(AcpMethods.SessionUpdate, new SessionUpdateParams
        {
            SessionId = sessionId,
            Update = new AcpSessionUpdate
            {
                SessionUpdate = AcpUpdateKind.AvailableCommandsUpdate,
                Commands = commands
            }
        });
    }

    private static List<SessionWireInputPart> BuildWireInputParts(List<AcpContentBlock> prompt)
    {
        var parts = new List<SessionWireInputPart>();
        foreach (var block in prompt)
        {
            switch (block.Type)
            {
                case "text" when !string.IsNullOrEmpty(block.Text):
                    parts.Add(new SessionWireInputPart { Type = "text", Text = block.Text });
                    break;
                case "resource" when block.Resource != null:
                {
                    var t = $"[File: {block.Resource.Uri}]";
                    if (!string.IsNullOrEmpty(block.Resource.Text))
                        t += "\n" + block.Resource.Text;
                    parts.Add(new SessionWireInputPart { Type = "text", Text = t });
                    break;
                }
                default:
                    if (!string.IsNullOrEmpty(block.Data) &&
                        !string.IsNullOrEmpty(block.MimeType) &&
                        block.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        parts.Add(new SessionWireInputPart
                        {
                            Type = "image",
                            Url = $"data:{block.MimeType};base64,{block.Data}"
                        });
                    }
                    break;
            }
        }

        if (parts.Count == 0 && prompt.Count > 0)
            parts.Add(new SessionWireInputPart { Type = "text", Text = ExtractPromptText(prompt) });

        return parts;
    }

    private static string ExtractPromptText(List<AcpContentBlock> prompt)
    {
        if (prompt.Count == 0)
            return "";

        var sb = new StringBuilder();
        foreach (var block in prompt)
        {
            switch (block.Type)
            {
                case "text" when !string.IsNullOrEmpty(block.Text):
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(block.Text);
                    break;
                case "resource" when block.Resource != null:
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append($"[File: {block.Resource.Uri}]");
                    if (!string.IsNullOrEmpty(block.Resource.Text))
                    {
                        sb.Append('\n');
                        sb.Append(block.Resource.Text);
                    }
                    break;
            }
        }

        return sb.ToString();
    }

    private static List<McpServerConfig> ConvertToMcpConfigs(List<AcpMcpServer> mcpServers)
    {
        var configs = new List<McpServerConfig>();
        foreach (var s in mcpServers)
        {
            if (string.IsNullOrWhiteSpace(s.Name))
                continue;
            var transport = string.IsNullOrEmpty(s.Type) || s.Type.Equals("stdio", StringComparison.OrdinalIgnoreCase)
                ? "stdio"
                : s.Type.Equals("http", StringComparison.OrdinalIgnoreCase)
                    ? "http"
                    : null;
            if (transport == null)
                continue;
            configs.Add(new McpServerConfig
            {
                Name = s.Name,
                Enabled = true,
                Transport = transport,
                Command = s.Command ?? "",
                Arguments = s.Args ?? [],
                EnvironmentVariables = s.Env != null ? s.Env.ToDictionary(e => e.Name, e => e.Value) : new Dictionary<string, string>(),
                Url = s.Url ?? "",
                Headers = s.Headers != null ? s.Headers.ToDictionary(h => h.Name, h => h.Value) : new Dictionary<string, string>()
            });
        }

        return configs;
    }

    private async Task SendConfigOptionsUpdate(string sessionId, List<ConfigOption> configOptions)
    {
        await acpTransport.SendNotificationAsync(AcpMethods.SessionUpdate, new SessionUpdateParams
        {
            SessionId = sessionId,
            Update = new AcpSessionUpdate
            {
                SessionUpdate = AcpUpdateKind.ConfigOptionsUpdate,
                ConfigOptions = configOptions
            }
        });
    }

    private async Task<List<ConfigOption>> BuildConfigOptionsAsync(
        string? currentMode = "agent",
        string? currentModel = null,
        CancellationToken ct = default)
    {
        ModelListResult? modelList = null;
        try
        {
            modelList = await wire.ModelListAsync(ct);
        }
        catch (Exception ex)
        {
            logger?.LogError("ACP model list unavailable", ex);
        }

        return BuildConfigOptions(currentMode, currentModel, modelList);
    }

    internal static List<ConfigOption> BuildConfigOptions(
        string? currentMode = "agent",
        string? currentModel = null,
        ModelListResult? modelList = null)
    {
        var options = new List<ConfigOption>
        {
            new ConfigOption
            {
                Id = "mode",
                Name = "Mode",
                Category = "mode",
                CurrentValue = string.IsNullOrWhiteSpace(currentMode) ? "agent" : currentMode.Trim(),
                Options =
                [
                    new ConfigOptionValue { Value = "agent", Name = "Agent", Description = "Full agent mode with all tools" },
                    new ConfigOptionValue { Value = "plan", Name = "Plan", Description = "Read-only planning mode" }
                ]
            }
        };

        if (modelList?.Success == true)
        {
            var modelIds = modelList.Models
                .Select(m => m.Id?.Trim())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .ToList();

            var normalizedCurrentModel = NormalizeOptionalModel(currentModel);
            if (!string.IsNullOrWhiteSpace(normalizedCurrentModel)
                && modelIds.All(id => !string.Equals(id, normalizedCurrentModel, StringComparison.OrdinalIgnoreCase)))
            {
                modelIds.Add(normalizedCurrentModel);
            }

            modelIds = [.. modelIds
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)];

            var modelOptions = new List<ConfigOptionValue>
            {
                new() { Value = DefaultModelValue, Name = "Default" }
            };
            modelOptions.AddRange(modelIds.Select(id => new ConfigOptionValue { Value = id, Name = id }));

            options.Add(new ConfigOption
            {
                Id = "model",
                Name = "Model",
                Category = "model",
                CurrentValue = string.IsNullOrWhiteSpace(normalizedCurrentModel)
                    ? DefaultModelValue
                    : normalizedCurrentModel,
                Options = modelOptions
            });
        }

        return options;
    }

    private async Task<ThreadConfigurationState> ReadThreadConfigurationAsync(
        string threadId,
        bool includeTurns,
        CancellationToken ct)
    {
        var readDoc = await wire.SendRequestAsync(AppServerMethods.ThreadRead, new
        {
            threadId,
            includeTurns
        }, ct: ct);

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
        return (string.IsNullOrWhiteSpace(mode) ? "agent" : mode.Trim(), NormalizeOptionalModel(model));
    }

    private static string? TryGetCaseInsensitiveString(JsonElement obj, string name)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static string? TryGetStringProperty(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var property in obj.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            {
                return NormalizeOptionalString(property.Value.GetString());
            }
        }

        return null;
    }

    private static string? NormalizeOptionalModel(string? model)
    {
        var trimmed = model?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static void SetCaseInsensitiveStringProperty(JsonObject obj, string key, string? value)
    {
        var existingKey = obj
            .Select(kv => kv.Key)
            .FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(value))
        {
            if (existingKey != null)
                obj.Remove(existingKey);
            return;
        }

        obj[existingKey ?? key] = value;
    }

    private static bool IsModelConfigOption(ConfigOption option) =>
        string.Equals(option.Id, "model", StringComparison.OrdinalIgnoreCase)
        || string.Equals(option.Category, "model", StringComparison.OrdinalIgnoreCase);

    private sealed record ThreadConfigurationState(string Mode, string? Model, JsonObject? Configuration);

    private async Task<bool> EnsureInitialized(JsonRpcRequest request)
    {
        if (_initialized) return true;
        await acpTransport.SendErrorAsync(request.Id, -32002, "Agent not initialized. Call 'initialize' first.");
        return false;
    }

    private static T? Deserialize<T>(JsonElement? element) where T : class
    {
        if (element == null || element.Value.ValueKind == JsonValueKind.Undefined)
            return null;
        return element.Value.Deserialize<T>(JsonOptions);
    }
}
