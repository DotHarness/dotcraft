using System.Collections.Concurrent;
using System.Text.Json;
using DotCraft.CodexGateway.Protocol;
using DotCraft.Sdk;

namespace DotCraft.CodexGateway.Workspace;

public sealed partial class CodexWorkspaceSession : IAsyncDisposable
{
    private readonly DotCraftClient _client;
    private readonly string _workspace;
    private readonly WorkspaceRoutes _routes;
    private readonly WorkspaceProjector _projector;
    private readonly Func<JsonElement, CancellationToken, Task> _send;
    private readonly CodexInteractiveBridge? _interactive;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<string, byte> _attached = new();
    private readonly Task _notifications;

    public CodexWorkspaceSession(DotCraftClient client, string workspacePath, string stateDirectory,
        Func<JsonElement, CancellationToken, Task> send)
        : this(client, workspacePath, stateDirectory, send, null) { }

    internal CodexWorkspaceSession(DotCraftClient client, string workspacePath, string stateDirectory,
        Func<JsonElement, CancellationToken, Task> send, CodexInteractiveBridge? interactive)
    {
        _client = client;
        _workspace = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspacePath));
        _routes = WorkspaceRoutes.Open(stateDirectory, _workspace);
        _projector = new(_routes, _workspace);
        _send = send;
        _interactive = interactive;
        _client.Wire.StateChanged += ConnectionChanged;
        _notifications = ForwardNotificationsAsync(_lifetime.Token);
    }

    public static async Task<CodexWorkspaceSession> ConnectAsync(string workspacePath, string stateDirectory,
        Func<JsonElement, CancellationToken, Task> send, DotCraftLocalOptions? options = null,
        CancellationToken ct = default, CodexRequestBroker? reverseRequests = null)
    {
        options ??= new DotCraftLocalOptions
        {
            ClientName = "dotcraft-codex-gateway",
            ExtraCapabilities = new Dictionary<string, object?> { ["commandExecutionStreaming"] = true, ["toolExecutionLifecycle"] = true }
        };
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspacePath));
        var bridge = reverseRequests is null ? null :
            new CodexInteractiveBridge(WorkspaceRoutes.Open(stateDirectory, canonical), canonical, reverseRequests, send);
        if (bridge is not null)
        {
            options.ApprovalSupport = true;
            options.RequestUserInputSupport = true;
            options.ApprovalHandler = bridge.HandleApprovalAsync;
            options.UserInputHandler = bridge.HandleUserInputAsync;
        }
        var client = await DotCraftClient.ConnectLocalAsync(canonical, options, ct);
        return new(client, canonical, stateDirectory, send, bridge);
    }

    public async Task<object?> HandleAsync(string method, JsonElement p, CancellationToken ct)
    {
        switch (method)
        {
            case "model/list": return await ModelsAsync(p, ct);
            case "collaborationMode/list": return await ModesAsync(ct);
            case "config/read": return await ConfigurationAsync(p, ct);
            case "config/value/write":
            case "config/batchWrite": return await WriteConfigurationAsync(method, p, ct);
            case "thread/list": return await ListThreadsAsync(p, ct);
            case "thread/start": return await StartThreadAsync(p, ct);
            case "thread/read":
            case "thread/resume": return await ReadThreadAsync(method, p, ct);
            case "thread/turns/list": return await TurnsAsync(p, ct);
            case "thread/items/list": return await ItemsAsync(p, ct);
            case "thread/items/read": return await ReadItemsAsync(p, ct);
            case "thread/timeline/list": return await TimelineAsync(p, ct);
            case "turn/start": return await StartTurnAsync(p, ct);
            case "turn/steer":
            {
                var thread = ThreadId(p);
                await EnsureAttachedAsync(thread, ct);
                var result = await RequestAsync(method, new
                {
                    threadId = thread,
                    expectedTurnId = _routes.Resolve(p.Required("expectedTurnId"), "turn", thread),
                    input = Input(p), clientUserMessageId = p.Text("clientUserMessageId")
                }, ct);
                return new { turnId = _routes.External("turn", thread, result.Required("turnId")) };
            }
            case "turn/interrupt":
            {
                var thread = ThreadId(p);
                return await RequestAsync(method, new
                {
                    threadId = thread, turnId = _routes.Resolve(p.Required("turnId"), "turn", thread)
                }, ct);
            }
            case "thread/archive":
                await RequestAsync(method, new { threadId = ThreadId(p) }, ct);
                return new { };
            case "thread/unarchive":
                await RequestAsync(method, new { threadId = ThreadId(p) }, ct);
                return await ReadThreadAsync("thread/read", p, ct);
            case "thread/unsubscribe":
            {
                var thread = ThreadId(p);
                _attached.TryRemove(thread, out _);
                await RequestAsync(method, new { threadId = thread }, ct);
                return new { status = "unsubscribed" };
            }
            case "thread/name/set":
                await RequestAsync("thread/rename", new { threadId = ThreadId(p), displayName = p.Required("name") }, ct);
                return new { };
            default: return null;
        }
    }

    private Task<JsonElement> RequestAsync(string method, object parameters, CancellationToken ct) =>
        _client.RequestRawAsync(method, parameters, ct);

    internal Task<JsonElement> ListMetadataAsync(JsonElement p, CancellationToken ct) =>
        RequestAsync("thread/list", new
        {
            identity = Identity(), scope = "workspace", includeArchived = p.Flag("archived"),
            query = p.Text("searchTerm")
        }, ct);

    internal Task<JsonElement> ReadConfigurationMetadataAsync(CancellationToken ct) =>
        RequestAsync("config/read", new { includeLayers = true }, ct);

    private string ThreadId(JsonElement p) => _routes.Resolve(p.Required("threadId"), "thread");
    private object Identity() => new { channelName = "codex-gateway", userId = Environment.UserName, workspacePath = _workspace };

    private void ValidateWorkspace(JsonElement p)
    {
        if (p.Text("cwd") is { } cwd && !WithinWorkspace(cwd))
            throw new ArgumentException("cwd must remain in the selected gateway workspace.");
        if (p.Get("runtimeWorkspaceRoots").Rows().Any(root =>
                root.ValueKind != JsonValueKind.String || !WithinWorkspace(root.GetString()!)))
            throw new ArgumentException("Runtime roots must stay in the selected gateway workspace.");
    }

    private bool WithinWorkspace(string path)
    {
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return canonical.Equals(_workspace, comparison) ||
            canonical.StartsWith(_workspace + Path.DirectorySeparatorChar, comparison);
    }

    private async Task<object> StartThreadAsync(JsonElement p, CancellationToken ct)
    {
        ValidateWorkspace(p);
        p.Reject("dynamicTools", "environments", "baseInstructions");
        var settings = await ExecutionSettingsAsync(p, ct, allowPolicy: true);
        if (p.Text("developerInstructions") is { } instructions) settings["developerInstructions"] = instructions;
        var result = await RequestAsync("thread/start", new
        {
            identity = Identity(), cwd = p.Text("cwd") ?? _workspace,
            runtimeWorkspaceRoots = p.Get("runtimeWorkspaceRoots").Value() ?? new[] { _workspace },
            config = settings.Count == 0 ? null : settings, ephemeral = p.Flag("ephemeral")
        }, ct);
        var source = result.Get("thread");
        _attached.TryAdd(source.Required("id"), 0);
        return await ThreadResultAsync(result, null, null, ct);
    }

    private async Task<object> ReadThreadAsync(string method, JsonElement p, CancellationToken ct)
    {
        ValidateWorkspace(p);
        var thread = ThreadId(p);
        JsonElement result;
        if (method == "thread/resume")
        {
            var settings = await ExecutionSettingsAsync(p, ct, allowPolicy: true);
            if (p.Text("developerInstructions") is { } instructions) settings["developerInstructions"] = instructions;
            if (settings.Count > 0)
            {
                var current = (await RequestAsync("thread/read", new { threadId = thread }, ct)).Get("thread").Get("configuration");
                var merged = current.ValueKind == JsonValueKind.Object
                    ? current.EnumerateObject().ToDictionary(v => v.Name, v => (object?)v.Value.Clone()) : new();
                foreach (var setting in settings) merged[setting.Key] = setting.Value;
                await RequestAsync("thread/config/update", new { threadId = thread, config = merged }, ct);
            }
            result = await AttachAsync(thread, p, ct);
        }
        else result = await RequestAsync(method, new { threadId = thread }, ct);
        HistoryPage? page = null;
        object[] turns = [];
        if (method == "thread/resume")
        {
            page = await InitialPageAsync(thread, p, result.Get("initialTurnsPage"), ct);
            turns = p.Flag("excludeTurns") ? [] : page?.Data ?? [];
        }
        else if (p.Flag("includeTurns"))
        {
            turns = await ReadAllTurnsAsync(thread, ct);
        }
        var source = result.Get("thread");
        if (method == "thread/read")
            return new { thread = _projector.Thread(source, turns), readState = (object?)null };
        return await ThreadResultAsync(result, turns, page, ct);
    }

    private async Task<object> ThreadResultAsync(JsonElement response, object[]? turns, object? initialPage, CancellationToken ct)
    {
        var source = response.Get("thread");
        var thread = source.Required("id");
        var config = source.Get("configuration");
        var provider = config.Text("providerId") ?? "";
        var model = config.Text("model") ?? "";
        var policy = await EffectiveApprovalPolicyAsync(config, ct);
        var reasoning = config.Get("reasoning");
        var effort = reasoning.Get("enabled").ValueKind == JsonValueKind.False ? "none" :
            reasoning.Text("effort") is { } value ? CodexEffort(value) : null;
        return new
        {
            thread = _projector.Thread(source, turns),
            model = WorkspaceProjector.Binding(provider, model), modelProvider = provider,
            serviceTier = config.Text("speed") == "fast" ? "fast" : (string?)null,
            disabledPluginIds = Array.Empty<string>(), cwd = source.Text("cwd") ?? _workspace,
            runtimeWorkspaceRoots = source.Get("runtimeWorkspaceRoots").Value() ?? new[] { _workspace },
            instructionSources = response.Get("instructionSources").Value() ?? Array.Empty<string>(),
            approvalPolicy = ApprovalPolicy(policy), approvalsReviewer = "user",
            sandbox = CodexPermissionProfile.Sandbox(),
            activePermissionProfile = CodexPermissionProfile.Active(),
            reasoningEffort = effort,
            collaborationMode = new
            {
                mode = config.Text("mode") == "plan" ? "plan" : "default",
                settings = new { model = WorkspaceProjector.Binding(provider, model),
                    reasoning_effort = effort, developer_instructions = (string?)null }
            },
            multiAgentMode = "explicitRequestOnly",
            initialTurnsPage = initialPage,
            turnsBackwardsCursor = WrapCursor("turns", thread, null, "desc", response.Text("turnsBackwardsCursor")),
            itemsBackwardsCursor = WrapCursor("itemBootstrap", thread, null, "desc", response.Text("itemsBackwardsCursor"))
        };
    }

    private async Task<object> StartTurnAsync(JsonElement p, CancellationToken ct)
    {
        ValidateWorkspace(p);
        p.Reject("outputSchema", "environments", "serviceTierForTurn");
        var thread = ThreadId(p);
        var settings = await ExecutionSettingsAsync(p, ct, allowPolicy: false);
        settings["threadId"] = thread;
        settings["input"] = Input(p);
        settings["clientUserMessageId"] = p.Text("clientUserMessageId");
        if (p.Text("cwd") is { } cwd) settings["cwd"] = cwd;
        if (p.Get("runtimeWorkspaceRoots").ValueKind == JsonValueKind.Array)
            settings["runtimeWorkspaceRoots"] = p.Get("runtimeWorkspaceRoots").Clone();
        await EnsureAttachedAsync(thread, ct);
        var result = await RequestAsync("turn/start", settings, ct);
        return new { turn = _projector.Turn(thread, result.Get("turn")) };
    }

    private object[] Input(JsonElement p)
    {
        if (p.Get("input").ValueKind != JsonValueKind.Array)
            throw new ArgumentException("'input' must be an array.");
        return p.Get("input").Rows().Select(part => part.Text("type") switch
        {
            "text" => (object)new { type = "text", text = part.Required("text") },
            "image" when part.Text("url") is { } url && url.StartsWith("data:image/", StringComparison.Ordinal)
                => new { type = "image", url },
            "localImage" when part.Text("path") is { } path && WithinWorkspace(path) => new { type = "localImage", path },
            "skill" when part.Text("path") is { } path && WithinWorkspace(path) => new { type = "skill", name = part.Required("name"), path },
            "mention" when part.Text("path") is { } path && WithinWorkspace(path) => new { type = "file", path, name = part.Text("name") },
            _ => throw new ArgumentException("Unsupported input part or attachment outside the authorized workspace.")
        }).ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Wire.StateChanged -= ConnectionChanged;
        _lifetime.Cancel();
        try
        {
            try { await Task.WhenAll(_notifications, _restoration); }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        }
        finally
        {
            try { await _client.DisposeAsync(); }
            finally
            {
                _attachmentLock.Dispose();
                _lifetime.Dispose();
            }
        }
    }
}
