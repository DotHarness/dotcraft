using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol;

namespace DotCraft.RemoteTools;

internal sealed class RemoteToolHostMcpHandlers : IAsyncDisposable
{
    private readonly RemoteToolHostStorage _storage;
    private readonly WorkspaceLeaseManager _leases;
    private readonly LeaseTerminalRegistry _leaseTerminals;
    private readonly RemoteToolHostActivityMonitor? _activity;
    private readonly string _hostInstanceId = "host_" + Guid.NewGuid().ToString("N");
    private readonly object _gate = new();
    private readonly Dictionary<string, HostWorkspaceRuntime> _runtimes = new(StringComparer.Ordinal);
    private Task<IReadOnlyList<RemoteToolContractSummary>>? _contracts;

    public RemoteToolHostMcpHandlers(
        RemoteToolHostStorage storage,
        WorkspaceLeaseManager leases,
        LeaseTerminalRegistry leaseTerminals,
        RemoteToolHostActivityMonitor? activity = null)
    {
        _storage = storage;
        _leases = leases;
        _leaseTerminals = leaseTerminals;
        _activity = activity;
    }

    /// <summary>The Hub-assigned peer id is the host identity every Agent-side surface sees.</summary>
    public IReadOnlyList<McpServerRequestHandler> CreateExtensionHandlers(string peerId) =>
    [
        Raw(
            RemoteToolHostProtocol.WorkspacesList,
            (request, ct) => HandleWorkspaceListAsync(request, peerId, ct)),
        Raw(RemoteToolHostProtocol.WorkspacesAcquire, HandleWorkspaceAcquireAsync),
        Raw(RemoteToolHostProtocol.WorkspacesRelease, HandleWorkspaceReleaseAsync),
        Raw(RemoteToolHostProtocol.WorkspacesHeartbeat, HandleWorkspaceHeartbeatAsync)
    ];

    public async ValueTask<ListToolsResult> ListToolsAsync(
        RequestContext<ListToolsRequestParams> request,
        CancellationToken cancellationToken)
    {
        var scope = request.Params?.Meta?["dotcraft"]?
            .Deserialize<RemoteCatalogScope>(RemoteToolHostProtocol.JsonOptions);
        if (scope is null || string.IsNullOrWhiteSpace(scope.LeaseId) || string.IsNullOrWhiteSpace(scope.WorkspaceId))
            throw new RemoteToolHostException(
                RemoteToolErrorCodes.ProtocolMismatch,
                "tools/list requires an active workspace lease.");

        var workspacePath = _leases.Validate(scope.LeaseId, scope.WorkspaceId);
        var state = RequireState();
        var runtime = await GetRuntimeAsync(scope.WorkspaceId, workspacePath, state, cancellationToken)
            .ConfigureAwait(false);
        return new ListToolsResult
        {
            Tools = runtime.Registrations.Select(registration => ToMcpTool(registration, state)).ToList()
        };
    }

    public async ValueTask<CallToolResult> CallToolAsync(
        RequestContext<CallToolRequestParams> request,
        string peerId,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        RemoteInvocationMeta? invocation = null;
        try
        {
            invocation = ParseInvocationMeta(request.Params.Meta);
            var workspacePath = _leases.Validate(invocation.LeaseId, invocation.WorkspaceId);
            var state = RequireState();
            if (!state.Workspaces.TryGetValue(invocation.WorkspaceId, out var registeredPath)
                || !PathsEqual(workspacePath, registeredPath))
            {
                throw new RemoteToolHostException(
                    RemoteToolErrorCodes.WorkspaceNotFound,
                    $"Workspace '{invocation.WorkspaceId}' is not registered.",
                    invocation.InvocationId);
            }

            var runtime = await GetRuntimeAsync(
                invocation.WorkspaceId,
                workspacePath,
                state,
                cancellationToken).ConfigureAwait(false);
            var registration = runtime.Registrations.FirstOrDefault(item =>
                string.Equals(item.Definition.Name.ToString(), request.Params.Name, StringComparison.Ordinal));
            if (registration is null)
                throw new RemoteToolHostException(
                    RemoteToolErrorCodes.RemoteToolUnavailable,
                    $"Remote tool '{request.Params.Name}' is unavailable.",
                    invocation.InvocationId);

            var definitionId = registration.Definition.Id.ToString();
            var contractHash = RemoteToolContractHasher.Compute(registration.Definition);
            if (!string.Equals(definitionId, invocation.DefinitionId, StringComparison.Ordinal)
                || !string.Equals(contractHash, invocation.ContractHash, StringComparison.Ordinal))
            {
                throw new RemoteToolHostException(
                    RemoteToolErrorCodes.ToolContractMismatch,
                    $"Remote tool contract mismatch for '{request.Params.Name}'.",
                    invocation.InvocationId);
            }

            var toolName = registration.Definition.Name.Name;
            using var activity = _activity?.Begin(peerId, toolName, ReadCommandPreview(request.Params.Arguments));
            RequireBoundTerminal(toolName, request.Params.Arguments, invocation);
            await EnforcePolicyAsync(request, state, invocation, cancellationToken).ConfigureAwait(false);

            var arguments = new JsonObject();
            foreach (var (name, value) in request.Params.Arguments ?? new Dictionary<string, JsonElement>())
                arguments[name] = JsonNode.Parse(value.GetRawText());
            var context = new ToolInvocationContext(
                invocation.ThreadId ?? $"remote:{invocation.LeaseId}",
                invocation.TurnId,
                invocation.InvocationId,
                ToolInvocationAudience.Host,
                registration.Definition.Name,
                registration.Definition.Id,
                registration.Binding.Id,
                registration.Binding.Revision,
                started,
                new ToolInvocationOrigin("remoteToolHost", invocation.InvocationId),
                workspacePath);
            var terminalsBeforeExec = string.Equals(toolName, "Exec", StringComparison.Ordinal)
                ? await SnapshotTerminalIdsAsync(runtime, cancellationToken).ConfigureAwait(false)
                : null;
            using var approvalScope = HostInvocationApprovalService.BeginApprovedInvocation();
            var result = await registration.Binding.Runtime.InvokeAsync(context, arguments, cancellationToken)
                .ConfigureAwait(false);
            if (terminalsBeforeExec is not null)
            {
                var current = await SnapshotTerminalIdsAsync(runtime, cancellationToken).ConfigureAwait(false);
                _leaseTerminals.Bind(
                    invocation.LeaseId,
                    current.Where(sessionId => !terminalsBeforeExec.Contains(sessionId)));
            }
            var materialized = RemoteToolArtifactStore.Materialize(
                _storage.ArtifactsRootPath,
                invocation,
                toolName,
                result);
            result = materialized.Result;
            Audit(
                invocation,
                request.Params.Name,
                result.Error?.Code ?? "ok",
                started,
                cancelled: false);
            return ToMcpResult(result, invocation, started, materialized.Artifact);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (invocation is not null)
                Audit(invocation, request.Params.Name, ToolErrorCodes.Cancelled, started, cancelled: true);
            throw;
        }
        catch (RemoteToolHostException ex)
        {
            if (invocation is not null)
                Audit(invocation, request.Params.Name, ex.Code, started, cancelled: false);
            return Error(ex.Code, ex.Message, ex.InvocationId ?? invocation?.InvocationId, started);
        }
        catch (Exception)
        {
            if (invocation is not null)
                Audit(invocation, request.Params.Name, ToolErrorCodes.ExecutionFailed, started, cancelled: false);
            return Error(
                ToolErrorCodes.ExecutionFailed,
                "Remote Tool Host execution failed.",
                invocation?.InvocationId,
                started);
        }
    }

    private void Audit(
        RemoteInvocationMeta invocation,
        string toolName,
        string resultCode,
        DateTimeOffset started,
        bool cancelled) =>
        _storage.AppendAudit(new RemoteToolAuditEntry(
            DateTimeOffset.UtcNow,
            invocation.ThreadId,
            invocation.TurnId,
            invocation.WorkspaceId,
            toolName,
            invocation.InvocationId,
            resultCode,
            (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
            cancelled));

    public async ValueTask DisposeAsync()
    {
        _leases.ReleaseAll();
        HostWorkspaceRuntime[] runtimes;
        lock (_gate)
        {
            runtimes = _runtimes.Values.ToArray();
            _runtimes.Clear();
        }
        foreach (var runtime in runtimes)
            await runtime.DisposeAsync().ConfigureAwait(false);
    }

    private async ValueTask<JsonNode?> HandleWorkspaceListAsync(
        JsonRpcRequest request,
        string peerId,
        CancellationToken cancellationToken)
    {
        var parameters = Deserialize<WorkspaceListRequest>(request);
        ValidateProfile(parameters.ProfileVersion);
        var state = RequireState();
        var contracts = await GetContractsAsync().ConfigureAwait(false);
        var response = new WorkspaceListResponse(
            state.ProfileVersion,
            peerId,
            state.DisplayName,
            _hostInstanceId,
            Environment.MachineName,
            System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Environment.UserName,
            state.CatalogRevision,
            RemoteToolHostProtocol.BuildVersion,
            RemoteToolHostProtocol.ComputeCatalogDigest(contracts),
            contracts,
            state.Workspaces.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => ToCatalogEntry(pair.Key, pair.Value, parameters.ClientInstanceId))
                .ToArray());
        return JsonSerializer.SerializeToNode(response, RemoteToolHostProtocol.JsonOptions);
    }

    private ValueTask<JsonNode?> HandleWorkspaceAcquireAsync(
        JsonRpcRequest request,
        CancellationToken cancellationToken)
    {
        var parameters = Deserialize<WorkspaceAcquireRequest>(request);
        ValidateProfile(parameters.ProfileVersion);
        var state = RequireState();
        if (!state.Workspaces.TryGetValue(parameters.WorkspaceId, out var path))
            throw new RemoteToolHostException(
                RemoteToolErrorCodes.WorkspaceNotFound,
                $"Workspace '{parameters.WorkspaceId}' is not registered.");
        var response = _leases.Acquire(
            parameters.ClientInstanceId,
            parameters.WorkspaceId,
            Path.GetFullPath(path),
            _hostInstanceId,
            state.CatalogRevision);
        return ValueTask.FromResult(JsonSerializer.SerializeToNode(response, RemoteToolHostProtocol.JsonOptions));
    }

    private async ValueTask<JsonNode?> HandleWorkspaceReleaseAsync(
        JsonRpcRequest request,
        CancellationToken cancellationToken)
    {
        var parameters = Deserialize<WorkspaceLeaseRequest>(request);
        ValidateProfile(parameters.ProfileVersion);
        var finalRelease = _leases.Release(
            parameters.ClientInstanceId,
            parameters.LeaseId,
            parameters.WorkspaceId);
        if (finalRelease)
            await DisposeRuntimeAsync(parameters.WorkspaceId).ConfigureAwait(false);
        return new JsonObject();
    }

    private ValueTask<JsonNode?> HandleWorkspaceHeartbeatAsync(
        JsonRpcRequest request,
        CancellationToken cancellationToken)
    {
        var parameters = Deserialize<WorkspaceLeaseRequest>(request);
        ValidateProfile(parameters.ProfileVersion);
        var expiresAt = _leases.Heartbeat(
            parameters.ClientInstanceId,
            parameters.LeaseId,
            parameters.WorkspaceId);
        return ValueTask.FromResult<JsonNode?>(JsonSerializer.SerializeToNode(
            new WorkspaceHeartbeatResponse(expiresAt),
            RemoteToolHostProtocol.JsonOptions));
    }

    private async Task<HostWorkspaceRuntime> GetRuntimeAsync(
        string workspaceId,
        string workspacePath,
        RemoteToolHostState state,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_runtimes.TryGetValue(workspaceId, out var cached)
                && cached.CatalogRevision == state.CatalogRevision
                && PathsEqual(cached.WorkspacePath, workspacePath))
                return cached;
        }

        var runtime = await HostWorkspaceRuntime.CreateAsync(
            workspaceId,
            workspacePath,
            state.CatalogRevision,
            _storage.GlobalConfigPath,
            _storage.RootPath,
            cancellationToken).ConfigureAwait(false);
        HostWorkspaceRuntime? retired = null;
        lock (_gate)
        {
            if (_runtimes.TryGetValue(workspaceId, out var current)
                && current.CatalogRevision == state.CatalogRevision
                && PathsEqual(current.WorkspacePath, workspacePath))
            {
                retired = runtime;
                runtime = current;
            }
            else
            {
                _runtimes.TryGetValue(workspaceId, out retired);
                _runtimes[workspaceId] = runtime;
            }
        }
        if (retired is not null)
            await retired.DisposeAsync().ConfigureAwait(false);
        return runtime;
    }

    private WorkspaceCatalogEntry ToCatalogEntry(string workspaceId, string path, string clientInstanceId)
    {
        var status = _leases.GetStatus(workspaceId);
        return new WorkspaceCatalogEntry(
            workspaceId,
            Path.GetFullPath(path),
            status is not null,
            status is null
                ? null
                : string.Equals(status.OwnerId, clientInstanceId, StringComparison.Ordinal) ? "self" : "other",
            status?.ExpiresAt);
    }

    /// <summary>The exported catalog is shared by every session, so one caller cannot cancel it.</summary>
    private Task<IReadOnlyList<RemoteToolContractSummary>> GetContractsAsync()
    {
        lock (_gate)
        {
            return _contracts ??= DescribeContractsAsync();
        }
    }

    private async Task<IReadOnlyList<RemoteToolContractSummary>> DescribeContractsAsync()
    {
        var definitions = await HostWorkspaceRuntime.DescribeExportedToolsAsync(
            _storage.GlobalConfigPath,
            _storage.RootPath,
            CancellationToken.None).ConfigureAwait(false);
        return
        [
            .. definitions
                .Select(definition => new RemoteToolContractSummary(
                    definition.Id.ToString(),
                    definition.Name.ToString(),
                    RemoteToolContractHasher.Compute(definition)))
                .OrderBy(summary => summary.DefinitionId, StringComparer.Ordinal)
        ];
    }

    private static string? ReadCommandPreview(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null
            || !arguments.TryGetValue("command", out var value)
            || value.ValueKind != JsonValueKind.String)
            return null;
        var command = value.GetString() ?? string.Empty;
        return command.Length <= 120 ? command : command[..120];
    }

    private void RequireBoundTerminal(
        string toolName,
        IDictionary<string, JsonElement>? arguments,
        RemoteInvocationMeta invocation)
    {
        if (!string.Equals(toolName, "WriteStdin", StringComparison.Ordinal))
            return;
        var sessionId = arguments is not null
            && arguments.TryGetValue("sessionId", out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        if (sessionId is null || !_leaseTerminals.IsBound(invocation.LeaseId, sessionId))
            throw new RemoteToolHostException(
                RemoteToolErrorCodes.RemotePolicyDenied,
                "WriteStdin accepts only background terminals created by an approved Exec in this lease.",
                invocation.InvocationId);
    }

    private static async Task<IReadOnlyCollection<string>> SnapshotTerminalIdsAsync(
        HostWorkspaceRuntime runtime,
        CancellationToken cancellationToken)
    {
        var terminals = await runtime.Terminals.ListAsync(ct: cancellationToken).ConfigureAwait(false);
        return terminals.Select(terminal => terminal.SessionId).ToHashSet(StringComparer.Ordinal);
    }

    private async Task DisposeRuntimeAsync(string workspaceId)
    {
        HostWorkspaceRuntime? runtime;
        lock (_gate)
        {
            _runtimes.Remove(workspaceId, out runtime);
        }
        if (runtime is not null)
            await runtime.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task EnforcePolicyAsync(
        RequestContext<CallToolRequestParams> request,
        RemoteToolHostState state,
        RemoteInvocationMeta invocation,
        CancellationToken cancellationToken)
    {
        var toolName = request.Params.Name;
        var policy = state.ToolPolicies.GetValueOrDefault(toolName)
            ?? DefaultPolicy(toolName);
        if (string.Equals(policy, "deny", StringComparison.OrdinalIgnoreCase))
            throw new RemoteToolHostException(
                RemoteToolErrorCodes.RemotePolicyDenied,
                $"Remote Tool Host policy denied '{toolName}'.",
                invocation.InvocationId);
        if (!string.Equals(policy, "needsApproval", StringComparison.OrdinalIgnoreCase))
            return;

        var result = await request.Server.ElicitAsync<RemoteExecutionApproval>(
            $"Approve this Remote Tool Host call once? Tool: {toolName}; workspace: {invocation.WorkspaceId}",
            new RequestOptions
            {
                Meta = new JsonObject
                {
                    ["dotcraft"] = new JsonObject
                    {
                        ["invocationId"] = invocation.InvocationId,
                        ["threadId"] = invocation.ThreadId,
                        ["turnId"] = invocation.TurnId
                    }
                }
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsAccepted || result.Content?.Approved != true)
            throw new RemoteToolHostException(
                RemoteToolErrorCodes.ApprovalDeclined,
                $"Approval was declined for remote tool '{toolName}'.",
                invocation.InvocationId);
    }

    private static string DefaultPolicy(string toolName) => toolName switch
    {
        "Exec" or "WriteFile" or "EditFile" => "needsApproval",
        _ => "allow"
    };

    private static Tool ToMcpTool(ToolRegistration registration, RemoteToolHostState state)
    {
        var definition = registration.Definition;
        return new Tool
        {
            Name = definition.Name.ToString(),
            Description = definition.Description,
            InputSchema = definition.InputSchema,
            OutputSchema = definition.OutputSchema,
            Meta = new JsonObject
            {
                ["dotcraft/remoteTool"] = new JsonObject
                {
                    ["profileVersion"] = RemoteToolHostProtocol.ProfileVersion,
                    ["definitionId"] = definition.Id.ToString(),
                    ["contractHash"] = RemoteToolContractHasher.Compute(definition),
                    ["catalogRevision"] = state.CatalogRevision.ToString()
                }
            }
        };
    }

    private static CallToolResult ToMcpResult(
        ToolExecutionResult result,
        RemoteInvocationMeta invocation,
        DateTimeOffset started,
        RemoteToolArtifactMeta? artifact)
    {
        var response = new CallToolResult
        {
            IsError = !result.Success,
            StructuredContent = result.StructuredContent,
            Content = ToMcpContent(result),
            Meta = BuildResultMeta(
                result.Error?.Code,
                invocation.InvocationId,
                started,
                result.Error?.Message,
                artifact)
        };
        return response;
    }

    private static IList<ContentBlock> ToMcpContent(ToolExecutionResult result)
    {
        if (result.ContentItems is not { Count: > 0 })
            return [new TextContentBlock { Text = result.Content ?? string.Empty }];

        var content = new List<ContentBlock>();
        foreach (var item in result.ContentItems)
        {
            switch (item)
            {
                case TextContent text:
                    content.Add(new TextContentBlock { Text = text.Text });
                    break;
                case DataContent data when data.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase):
                    content.Add(ImageContentBlock.FromBytes(data.Data, data.MediaType));
                    break;
                case DataContent data when data.MediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase):
                    content.Add(AudioContentBlock.FromBytes(data.Data, data.MediaType));
                    break;
                default:
                    content.Add(new TextContentBlock { Text = $"[{item.GetType().Name} content]" });
                    break;
            }
        }
        return content;
    }

    private static CallToolResult Error(
        string code,
        string message,
        string? invocationId,
        DateTimeOffset started) => new()
        {
            IsError = true,
            Content = [new TextContentBlock { Text = message }],
            Meta = BuildResultMeta(code, invocationId, started, message)
        };

    private static JsonObject BuildResultMeta(
        string? code,
        string? invocationId,
        DateTimeOffset started,
        string? diagnostic,
        RemoteToolArtifactMeta? artifact = null)
    {
        var dotcraft = new JsonObject
        {
            ["code"] = code,
            ["invocationId"] = invocationId,
            ["latencyMs"] = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
            ["diagnostic"] = diagnostic
        };
        if (artifact is not null)
            dotcraft["remoteArtifact"] = JsonSerializer.SerializeToNode(
                artifact,
                RemoteToolHostProtocol.JsonOptions);
        return new JsonObject { ["dotcraft"] = dotcraft };
    }

    private RemoteToolHostState RequireState() => _storage.LoadHostState()
        ?? throw new RemoteToolHostException(
            RemoteToolErrorCodes.HostOffline,
            "Remote Tool Host has not been set up.");

    private static T Deserialize<T>(JsonRpcRequest request) where T : class =>
        request.Params?.Deserialize<T>(RemoteToolHostProtocol.JsonOptions)
        ?? throw new RemoteToolHostException(
            ToolErrorCodes.InputInvalid,
            $"Invalid parameters for '{request.Method}'.");

    private static void ValidateProfile(string version)
    {
        if (!string.Equals(version, RemoteToolHostProtocol.ProfileVersion, StringComparison.Ordinal))
            throw new RemoteToolHostException(
                RemoteToolErrorCodes.ProtocolMismatch,
                $"Unsupported Remote Tool Host profile '{version}'.");
    }

    private static RemoteInvocationMeta ParseInvocationMeta(JsonObject? meta)
    {
        var node = meta?["dotcraft"];
        return node?.Deserialize<RemoteInvocationMeta>(RemoteToolHostProtocol.JsonOptions)
            ?? throw new RemoteToolHostException(
                ToolErrorCodes.InputInvalid,
                "Remote tool invocation metadata is required.");
    }

    private static McpServerRequestHandler Raw(
        string method,
        Func<JsonRpcRequest, CancellationToken, ValueTask<JsonNode?>> handler) => new()
        {
            Method = method,
            Handler = async (request, cancellationToken) =>
            {
                try
                {
                    var result = await handler(request, cancellationToken).ConfigureAwait(false);
                    return new JsonObject
                    {
                        ["success"] = true,
                        ["result"] = result
                    };
                }
                catch (RemoteToolHostException ex)
                {
                    return new JsonObject
                    {
                        ["success"] = false,
                        ["error"] = JsonSerializer.SerializeToNode(
                            new ExtensionError(ex.Code, ex.Message),
                            RemoteToolHostProtocol.JsonOptions)
                    };
                }
                catch (Exception)
                {
                    return new JsonObject
                    {
                        ["success"] = false,
                        ["error"] = JsonSerializer.SerializeToNode(
                            new ExtensionError(
                                ToolErrorCodes.ExecutionFailed,
                                "Remote Tool Host request failed."),
                            RemoteToolHostProtocol.JsonOptions)
                    };
                }
            }
        };

    private static bool PathsEqual(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private sealed record RemoteExecutionApproval(bool Approved);
}
