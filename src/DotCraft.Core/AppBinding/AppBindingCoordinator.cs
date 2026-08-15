using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;
using DotCraft.Mcp;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using DotCraft.Sessions;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using McpServerOrigin = DotCraft.Mcp.McpServerOrigin;

namespace DotCraft.AppBinding;

/// <summary>Owns the in-memory, one-session-per-binding MCP lifecycle.</summary>
public sealed class AppBindingCoordinator(AppBindingService controlPlane)
{
    private const int MaxUiResources = 32;
    private const int MaxUiResourceBytes = 2 * 1024 * 1024;
    private const int MaxTotalUiBytes = 8 * 1024 * 1024;
    private readonly ConcurrentDictionary<string, LiveBinding> _liveConfigs = new(StringComparer.Ordinal);
    public event Action<AppBindingStatusChanged>? BindingStatusChanged;

    private sealed class LiveBinding
    {
        public required string CraftPath { get; init; }
        public required string ThreadId { get; init; }
        public required McpServerConfig Config { get; init; }
        public McpClientManager? Manager { get; set; }
        public EventHandler<McpServerStatusChangedEventArgs>? StatusHandler { get; set; }
    }

    public async Task<AppBindingSnapshot> ActivateAsync(
        string craftPath,
        string principalId,
        AppBindingActivateCommand parameters,
        IThreadMcpRuntimeService runtime,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(parameters.Bearer))
            throw AppBindingErrors.InvalidInput("'bearer' is required.");
        var request = controlPlane.GetBindingRequest(craftPath,
            new AppBindingRequestQuery { BindingRequestId = parameters.BindingRequestId }, principalId);
        return await BindAsync(craftPath, principalId, request.BindingId, request.ThreadId, parameters.Endpoint,
            parameters.Bearer, null, parameters.BindingRequestId, runtime, cancellationToken);
    }

    public async Task<AppBindingSnapshot> RebindAsync(
        string craftPath,
        string principalId,
        AppBindingRebindCommand parameters,
        IThreadMcpRuntimeService runtime,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(parameters.Bearer))
            throw AppBindingErrors.InvalidInput("'bearer' is required.");
        var binding = controlPlane.GetBinding(craftPath, parameters.BindingId);
        return await BindAsync(craftPath, principalId, binding.BindingId, binding.ThreadId, parameters.Endpoint,
            parameters.Bearer, parameters.AuthorityRevision, null, runtime, cancellationToken);
    }

    private async Task<AppBindingSnapshot> BindAsync(
        string craftPath,
        string principalId,
        string bindingId,
        string threadId,
        string endpoint,
        string bearer,
        long? expectedRevision,
        string? requestId,
        IThreadMcpRuntimeService runtime,
        CancellationToken cancellationToken)
    {
        var binding = controlPlane.BeginActivation(craftPath, principalId, bindingId, expectedRevision, endpoint, requestId);
        var serverName = $"binding-{binding.BindingId}";
        var config = new McpServerConfig
        {
            Name = serverName,
            Enabled = true,
            Transport = "streamableHttp",
            Url = endpoint,
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Authorization"] = $"Bearer {bearer}"
            },
            Origin = McpServerOrigin.Binding(binding.BindingId, binding.AppId)
        };
        RemoveLive(binding.BindingId);
        var live = new LiveBinding { CraftPath = craftPath, ThreadId = threadId, Config = config };
        _liveConfigs[binding.BindingId] = live;
        try
        {
            // Discovery runs in an isolated manager. Candidate tools must never enter the
            // thread runtime before the capability decision is committed.
            DetachThread(threadId);
            await runtime.SetBindingMcpServersAsync(threadId, binding.BindingId, [], cancellationToken);
            await ReattachThreadAsync(runtime, threadId, cancellationToken);

            await using var probe = new McpClientManager();
            await probe.ConnectAsync([config], cancellationToken);
            var status = (await probe.ListStatusesAsync(cancellationToken)).FirstOrDefault(candidate =>
                string.Equals(candidate.Name, serverName, StringComparison.Ordinal));
            if (status == null || !string.Equals(status.StartupState, "ready", StringComparison.Ordinal))
                throw new InvalidOperationException(status?.LastError ?? "The binding MCP session did not become ready.");
            var inventory = await probe.GetInventoryAsync(serverName, cancellationToken)
                            ?? throw new InvalidOperationException("The binding MCP inventory is unavailable.");
            var capabilities = await BuildCapabilitiesAsync(binding.AppId, serverName, inventory, probe, cancellationToken);
            var completed = controlPlane.CompleteSync(craftPath, binding.BindingId, capabilities);
            if (completed.State == AppBindingStates.Active)
            {
                DetachThread(threadId);
                await runtime.SetBindingMcpServersAsync(threadId, binding.BindingId, [config], cancellationToken);
                var manager = await runtime.GetEffectiveMcpRuntimeAsync(threadId, cancellationToken)
                              ?? throw new InvalidOperationException("The thread MCP runtime is unavailable.");
                AttachThread(manager, threadId);
                var liveStatus = (await manager.ListStatusesAsync(cancellationToken)).FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, serverName, StringComparison.Ordinal));
                if (liveStatus == null || liveStatus.StartupState != "ready")
                    throw new InvalidOperationException(liveStatus?.LastError ?? "The binding MCP session did not become ready.");
            }
            return completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DetachThread(threadId);
            await runtime.SetBindingMcpServersAsync(threadId, binding.BindingId, [], CancellationToken.None);
            RemoveLive(binding.BindingId);
            await ReattachThreadAsync(runtime, threadId, CancellationToken.None);
            controlPlane.MarkUnavailable(craftPath, binding.BindingId, "activationCancelled", failed: true);
            throw;
        }
        catch (Exception ex)
        {
            DetachThread(threadId);
            await runtime.SetBindingMcpServersAsync(threadId, binding.BindingId, [], CancellationToken.None);
            RemoveLive(binding.BindingId);
            await ReattachThreadAsync(runtime, threadId, CancellationToken.None);
            return controlPlane.MarkUnavailable(craftPath, binding.BindingId, StableFailure(ex), failed: true);
        }
    }

    public async Task<AppBindingSnapshot> ConfirmAsync(
        string craftPath,
        ThreadAppBindingConfirmCapabilitiesCommand parameters,
        string actor,
        IThreadMcpRuntimeService runtime,
        CancellationToken cancellationToken)
    {
        var accepted = string.Equals(parameters.Decision, "accept", StringComparison.OrdinalIgnoreCase);
        var result = controlPlane.ConfirmCapabilities(craftPath, parameters, actor);
        if (accepted && _liveConfigs.TryGetValue(parameters.BindingId, out var live))
        {
            try
            {
                DetachThread(live.ThreadId);
                await runtime.SetBindingMcpServersAsync(live.ThreadId, parameters.BindingId, [live.Config], cancellationToken);
                var manager = await runtime.GetEffectiveMcpRuntimeAsync(live.ThreadId, cancellationToken)
                              ?? throw new InvalidOperationException("The thread MCP runtime is unavailable.");
                AttachThread(manager, live.ThreadId);
                var status = (await manager.ListStatusesAsync(cancellationToken)).FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, live.Config.Name, StringComparison.Ordinal));
                if (status == null || status.StartupState != "ready")
                    throw new InvalidOperationException(status?.LastError ?? "The binding MCP session did not become ready.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                DetachThread(live.ThreadId);
                await runtime.SetBindingMcpServersAsync(live.ThreadId, parameters.BindingId, [], CancellationToken.None);
                RemoveLive(parameters.BindingId);
                await ReattachThreadAsync(runtime, live.ThreadId, CancellationToken.None);
                controlPlane.MarkUnavailable(craftPath, parameters.BindingId, "activationCancelled");
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DetachThread(live.ThreadId);
                await runtime.SetBindingMcpServersAsync(live.ThreadId, parameters.BindingId, [], CancellationToken.None);
                RemoveLive(parameters.BindingId);
                await ReattachThreadAsync(runtime, live.ThreadId, CancellationToken.None);
                result = controlPlane.MarkUnavailable(craftPath, parameters.BindingId, StableFailure(ex));
            }
        }
        else if (!accepted)
        {
            DetachThread(parameters.ThreadId);
            await runtime.SetBindingMcpServersAsync(parameters.ThreadId, parameters.BindingId, [], cancellationToken);
            RemoveLive(parameters.BindingId);
            await ReattachThreadAsync(runtime, parameters.ThreadId, cancellationToken);
            result = controlPlane.MarkUnavailable(craftPath, parameters.BindingId, "capabilityExpansionRejected");
        }
        else
        {
            result = controlPlane.MarkUnavailable(craftPath, parameters.BindingId, "bindingMcpSessionMissing");
        }
        return result;
    }

    public async Task RemoveAsync(string threadId, string bindingId, IThreadMcpRuntimeService runtime, CancellationToken cancellationToken)
    {
        DetachThread(threadId);
        RemoveLive(bindingId);
        await runtime.SetBindingMcpServersAsync(threadId, bindingId, [], cancellationToken);
        await ReattachThreadAsync(runtime, threadId, cancellationToken);
    }

    private void DetachThread(string threadId)
    {
        foreach (var live in _liveConfigs.Values.Where(candidate => candidate.ThreadId == threadId))
            DetachStatus(live);
    }

    private void AttachThread(McpClientManager manager, string threadId)
    {
        foreach (var pair in _liveConfigs.Where(pair => pair.Value.ThreadId == threadId))
            AttachStatus(pair.Value, manager, pair.Key, pair.Value.Config.Name);
    }

    private async Task ReattachThreadAsync(
        IThreadMcpRuntimeService runtime,
        string threadId,
        CancellationToken cancellationToken)
    {
        var manager = await runtime.GetEffectiveMcpRuntimeAsync(threadId, cancellationToken);
        if (manager != null) AttachThread(manager, threadId);
    }

    private void AttachStatus(LiveBinding live, McpClientManager manager, string bindingId, string serverName)
    {
        DetachStatus(live);
        EventHandler<McpServerStatusChangedEventArgs> handler = (_, args) =>
        {
            if (!string.Equals(args.Status.Name, serverName, StringComparison.Ordinal)
                || args.Status.StartupState is "ready" or "starting") return;
            try
            {
                var current = controlPlane.GetBinding(live.CraftPath, bindingId);
                if (current.State == AppBindingStates.Active)
                {
                    var changed = controlPlane.MarkUnavailable(live.CraftPath, bindingId,
                        args.Status.FailureReason ?? "mcpSessionLost");
                    BindingStatusChanged?.Invoke(new AppBindingStatusChanged(
                        changed.ThreadId,
                        changed.BindingId,
                        changed.AppId,
                        changed.State,
                        changed.FailureReason,
                        changed.AuthorityRevision));
                }
            }
            catch { }
        };
        live.Manager = manager;
        live.StatusHandler = handler;
        manager.StatusChanged += handler;
    }

    private static void DetachStatus(LiveBinding live)
    {
        if (live.Manager != null && live.StatusHandler != null)
            live.Manager.StatusChanged -= live.StatusHandler;
        live.Manager = null;
        live.StatusHandler = null;
    }

    private void RemoveLive(string bindingId)
    {
        if (_liveConfigs.TryRemove(bindingId, out var live))
            DetachStatus(live);
    }

    private static async Task<List<AppBindingToolCapability>> BuildCapabilitiesAsync(
        string appId,
        string serverName,
        McpServerInventorySnapshot inventory,
        McpClientManager manager,
        CancellationToken cancellationToken)
    {
        var protocol = inventory.Tools.OfType<McpClientTool>()
            .ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        var uiUris = new HashSet<string>(StringComparer.Ordinal);
        var tools = new List<AppBindingToolCapability>();
        foreach (var function in inventory.Tools.OfType<AIFunction>().OrderBy(tool => tool.Name, StringComparer.Ordinal))
        {
            protocol.TryGetValue(function.Name, out var raw);
            var valid = McpAppMetadataParser.TryParseToolMetadata(raw?.ProtocolTool.Meta, out var metadata);
            if (!valid) metadata = new McpAppToolMetadata(null, McpAppVisibility.None);
            var visibility = new List<string>();
            if (metadata.Visibility.HasFlag(McpAppVisibility.Model)) visibility.Add("model");
            if (metadata.Visibility.HasFlag(McpAppVisibility.App)) visibility.Add("app");
            var annotations = new JsonObject();
            var hints = raw?.ProtocolTool.Annotations;
            annotations["requiresApproval"] = hints?.DestructiveHint != false || hints?.OpenWorldHint != false;
            annotations["readOnly"] = hints?.ReadOnlyHint == true;
            annotations["destructive"] = hints?.DestructiveHint != false;
            annotations["openWorld"] = hints?.OpenWorldHint != false;
            AppBindingUiCapability? ui = null;
            if (metadata.ResourceUri != null && uiUris.Count < MaxUiResources)
            {
                uiUris.Add(metadata.ResourceUri.AbsoluteUri);
                ui = new() { ResourceUri = metadata.ResourceUri.AbsoluteUri };
            }
            tools.Add(new AppBindingToolCapability
            {
                Namespace = appId,
                Name = function.Name,
                InputSchema = JsonNode.Parse(function.JsonSchema.GetRawText()) as JsonObject ?? new JsonObject(),
                Visibility = visibility,
                Annotations = annotations,
                Ui = ui
            });
        }

        var semaphore = new SemaphoreSlim(4, 4);
        var totalBytes = 0;
        var validUiResources = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        var uiByUri = tools.Where(tool => tool.Ui != null).Select(tool => tool.Ui!)
            .GroupBy(ui => ui.ResourceUri, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray());
        await Task.WhenAll(uiByUri.Select(async pair =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var uri = new Uri(pair.Key, UriKind.Absolute);
                var result = await manager.ReadResourceAsync(serverName, pair.Key, inventory.Generation, timeout.Token);
                if (!McpAppMetadataParser.TryParseResourceContent(result, uri, out var content, out _)
                    || content == null) return;
                var bytes = content.Text == null ? content.Blob.Length : Encoding.UTF8.GetByteCount(content.Text);
                if (bytes > MaxUiResourceBytes) return;
                if (Interlocked.Add(ref totalBytes, bytes) > MaxTotalUiBytes) return;
                validUiResources.TryAdd(pair.Key, 0);
                var metadata = content.Metadata;
                var permissions = new List<string>();
                if (metadata.Permissions?.Camera == true) permissions.Add("camera");
                if (metadata.Permissions?.Microphone == true) permissions.Add("microphone");
                if (metadata.Permissions?.Geolocation == true) permissions.Add("geolocation");
                if (metadata.Permissions?.ClipboardWrite == true) permissions.Add("clipboardWrite");
                foreach (var ui in pair.Value)
                {
                    ui.ConnectDomains = metadata.Csp?.ConnectDomains.ToList() ?? [];
                    ui.ResourceDomains = metadata.Csp?.ResourceDomains.ToList() ?? [];
                    ui.Permissions = permissions;
                    ui.SecurityHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                        JsonSerializer.Serialize(new { ui.ConnectDomains, ui.ResourceDomains, ui.Permissions, metadata.Domain })))).ToLowerInvariant();
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested) { }
            finally { semaphore.Release(); }
        }));
        foreach (var tool in tools)
        {
            if (tool.Ui != null && !validUiResources.ContainsKey(tool.Ui.ResourceUri))
                tool.Ui = null;
        }
        return tools;
    }

    private static string StableFailure(Exception error) => error switch
    {
        TimeoutException => "startupTimeout",
        HttpRequestException => "connectionFailed",
        _ => "mcpStartupFailed"
    };
}
