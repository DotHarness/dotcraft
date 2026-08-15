using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Mcp;
using DotCraft.Tools;
using ModelContextProtocol.Protocol;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using McpServerOrigin = DotCraft.Mcp.McpServerOrigin;

namespace DotCraft.AppServer;

/// <summary>
/// Handles the <c>mcp/*</c> wire methods, sharing workspace MCP persistence with plugin mutations
/// through <see cref="AppServerMcpConfigService"/>.
/// </summary>
internal sealed class McpRequestHandler(
    McpClientManager? mcpClientManager,
    AppServerMcpConfigService configService,
    IAppServerTransport transport,
    IAppConfigMonitor? appConfigMonitor,
    Action<McpServerStatusSnapshot>? broadcastMcpStatusChanged,
    IThreadToolDispatchService? threadToolDispatcher,
    IThreadMcpRuntimeService? threadMcpRuntimeService,
    IThreadAgentRefreshService? threadAgentRefreshService) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        mcpClientManager?.ConfigureElicitationHandler(HandleElicitationAsync);
        table.Map(Protocol.AppServer.AppServerRpc.McpList, HandleMcpListAsync);
        table.Map(Protocol.AppServer.AppServerRpc.McpGet, HandleMcpGetAsync);
        table.Map(Protocol.AppServer.AppServerRpc.McpUpsert, HandleMcpUpsertAsync);
        table.Map(Protocol.AppServer.AppServerRpc.McpRemove, HandleMcpRemoveAsync);
        table.Map(Protocol.AppServer.AppServerRpc.McpTest, HandleMcpTestAsync);
        table.Map(Protocol.AppServer.AppServerRpc.McpServerStatusList, HandleMcpServerStatusListAsync);
        table.Map(Protocol.AppServer.AppServerRpc.McpServerResourceRead, HandleMcpServerResourceReadAsync);
        table.Map(Protocol.AppServer.AppServerRpc.McpServerToolCall, HandleMcpServerToolCallAsync);
        table.Map(Protocol.AppServer.AppServerRpc.McpServerOAuthLogin, HandleMcpServerOAuthLoginAsync);
        table.Map(Protocol.AppServer.AppServerRpc.ConfigMcpServerReload, HandleMcpServerReloadAsync);
    }

    private async Task<object?> HandleMcpListAsync(AppServerTypedRequest<Protocol.RpcEmpty> request, CancellationToken ct)
    {
        _ = request;
        configService.EnsureManagementAvailable();
        var servers = await mcpClientManager!.ListConfigsAsync(ct);
        return new Contract.McpListResult
        {
            Servers = new Protocol.Optional<IReadOnlyList<Contract.McpServerConfig>>(
                servers.Select(McpContractMapper.ToContract).ToArray())
        };
    }

    private async Task<object?> HandleMcpGetAsync(AppServerTypedRequest<Contract.McpGetParams> request, CancellationToken ct)
    {
        var p = request.Params;
        var name = ValueOrDefault(p.Name);
        configService.EnsureManagementAvailable();
        if (string.IsNullOrWhiteSpace(name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var server = await mcpClientManager!.GetConfigAsync(name, ct);
        if (server == null)
            throw AppServerErrors.McpServerNotFound(name);

        return new Contract.McpGetResult { Server = McpContractMapper.ToContract(server) };
    }

    private async Task<object?> HandleMcpUpsertAsync(AppServerTypedRequest<Contract.McpUpsertParams> request, CancellationToken ct)
    {
        var p = request.Params;
        configService.EnsureManagementAvailable();
        var serverContract = Require(p.Server, "'server' is required.");
        McpContractMapper.ValidateContract(serverContract);

        var server = McpContractMapper.FromContract(serverContract);
        server.Origin = McpServerOrigin.Workspace();

        var existing = await mcpClientManager!.GetConfigAsync(server.Name, ct);
        if (existing?.ReadOnly == true)
            throw AppServerErrors.McpServerReadOnly(server.Name);

        var workspaceServers = await configService.GetWorkspaceServersAsync(ct);
        var existingIndex = workspaceServers.FindIndex(
            candidate => string.Equals(candidate.Name, server.Name, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            workspaceServers[existingIndex] = server;
        else
            workspaceServers.Add(server);

        await configService.SaveWorkspaceServersAsync(workspaceServers, ct);
        configService.SetCurrentWorkspaceServers(workspaceServers);
        await configService.ReconnectEffectiveRuntimeAsync(workspaceServers, ct);
        threadAgentRefreshService?.InvalidateThreadAgents();
        appConfigMonitor?.NotifyChanged(
            Protocol.AppServer.AppServerMethodNames.McpUpsert,
            [ConfigChangeRegions.Mcp]);

        var updated = await mcpClientManager.GetConfigAsync(server.Name, ct) ?? server;
        var status = (await mcpClientManager.ListStatusesAsync(ct))
            .FirstOrDefault(s => string.Equals(s.Name, updated.Name, StringComparison.OrdinalIgnoreCase));
        if (status != null)
            broadcastMcpStatusChanged?.Invoke(status);

        return new Contract.McpUpsertResult { Server = McpContractMapper.ToContract(updated) };
    }

    private async Task<object?> HandleMcpRemoveAsync(AppServerTypedRequest<Contract.McpRemoveParams> request, CancellationToken ct)
    {
        var p = request.Params;
        var name = ValueOrDefault(p.Name);
        configService.EnsureManagementAvailable();
        if (string.IsNullOrWhiteSpace(name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var existing = await mcpClientManager!.GetConfigAsync(name, ct);
        if (existing == null)
            throw AppServerErrors.McpServerNotFound(name);
        if (existing.ReadOnly)
            throw AppServerErrors.McpServerReadOnly(name);

        var workspaceServers = await configService.GetWorkspaceServersAsync(ct);
        var removed = workspaceServers.RemoveAll(
            candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed)
            throw AppServerErrors.McpServerNotFound(name);

        await configService.SaveWorkspaceServersAsync(workspaceServers, ct);
        configService.SetCurrentWorkspaceServers(workspaceServers);
        await configService.ReconnectEffectiveRuntimeAsync(workspaceServers, ct);
        threadAgentRefreshService?.InvalidateThreadAgents();
        appConfigMonitor?.NotifyChanged(
            Protocol.AppServer.AppServerMethodNames.McpRemove,
            [ConfigChangeRegions.Mcp]);
        return new Contract.McpRemoveResult { Removed = true };
    }

    private async Task<object?> HandleMcpTestAsync(AppServerTypedRequest<Contract.McpTestParams> request, CancellationToken ct)
    {
        var p = request.Params;
        if (mcpClientManager == null)
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.McpTest);

        var serverContract = Require(p.Server, "'server' is required.");
        McpContractMapper.ValidateContract(serverContract);
        var status = await mcpClientManager.TestAsync(McpContractMapper.FromContract(serverContract), ct);
        return new Contract.McpTestResult
        {
            Success = string.Equals(status.StartupState, "ready", StringComparison.OrdinalIgnoreCase),
            ErrorCode = status.LastError == null ? null : "McpServerTestFailed",
            ErrorMessage = status.LastError,
            ToolCount = string.Equals(status.StartupState, "ready", StringComparison.OrdinalIgnoreCase) ? status.ToolCount : null
        };
    }

    private async ValueTask<ElicitResult> HandleElicitationAsync(
        string serverName,
        ElicitRequestParams? request,
        CancellationToken ct)
    {
        if (request == null || !IsSupportedElicitation(request, out var requestedSchema))
            return new ElicitResult { Action = "decline" };

        try
        {
            var requestParams = new Contract.McpServerElicitationRequestParams
            {
                ServerName = serverName,
                Mode = string.IsNullOrWhiteSpace(request.Mode) ? "form" : request.Mode,
                ElicitationId = request.ElicitationId,
                Message = request.Message,
                Url = request.Url,
                RequestedSchema = ToElement(requestedSchema)
            };
            var response = await transport.RequestAsync(
                Contract.AppServerRpc.McpServerElicitationRequest,
                requestParams,
                ct,
                Timeout.InfiniteTimeSpan);

            if (response.Result is null)
                return new ElicitResult { Action = "cancel" };

            var actionValue = ValueOrDefault(response.Result.Action) ?? "cancel";
            var content = ValueOrDefault(response.Result.Content);
            var action = actionValue switch
            {
                "accept" when requestedSchema == null
                              || McpElicitationSchemaValidator.TryValidateContent(
                                  requestedSchema,
                                  content?.ToDictionary(static pair => pair.Key, static pair => pair.Value)) => "accept",
                "accept" => "decline",
                "decline" => "decline",
                _ => "cancel"
            };
            return new ElicitResult
            {
                Action = action,
                Content = action == "accept"
                    ? content?.ToDictionary(static pair => pair.Key, static pair => pair.Value)
                    : null
            };
        }
        catch (OperationCanceledException)
        {
            return new ElicitResult { Action = "cancel" };
        }
        catch
        {
            return new ElicitResult { Action = "decline" };
        }
    }

    private static bool IsSupportedElicitation(ElicitRequestParams request, out JsonObject? requestedSchema)
    {
        requestedSchema = null;
        var mode = string.IsNullOrWhiteSpace(request.Mode) ? "form" : request.Mode;
        if (string.Equals(mode, "form", StringComparison.Ordinal))
        {
            if (!McpElicitationSchemaValidator.TryValidateSchema(request.RequestedSchema, out var schema))
                return false;
            requestedSchema = schema;
            return true;
        }
        if (!string.Equals(mode, "url", StringComparison.Ordinal) ||
            !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
            return false;
        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
               (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback);
    }

    private async Task<object?> HandleMcpServerStatusListAsync(AppServerTypedRequest<Contract.McpServerStatusListParams> request, CancellationToken ct)
    {
        var p = request.Params;
        var detail = ValueOrDefault(p.Detail);
        var threadId = ValueOrDefault(p.ThreadId);
        if (mcpClientManager == null)
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.McpServerStatusList);
        if (detail is not null && detail is not "full" and not "toolsAndAuthOnly")
            throw AppServerErrors.InvalidParams("'detail' must be 'full' or 'toolsAndAuthOnly'.");

        var effectiveManager = !string.IsNullOrWhiteSpace(threadId) && threadMcpRuntimeService != null
            ? await threadMcpRuntimeService.GetEffectiveMcpRuntimeAsync(threadId, ct)
            : mcpClientManager;
        if (effectiveManager == null)
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.McpServerStatusList);
        var statuses = await effectiveManager.ListStatusesAsync(ct);
        var start = ParseCursor(ValueOrDefault(p.Cursor), statuses.Count);
        var limit = Math.Clamp(ValueOrDefault(p.Limit) ?? 100, 1, 500);
        var page = statuses.Skip(start).Take(limit).ToList();
        var includeFullInventory = detail is null or "full";
        if (includeFullInventory)
        {
            await Task.WhenAll(page
                .Where(static status => string.Equals(status.StartupState, "ready", StringComparison.Ordinal))
                .Select(status => RefreshOptionalInventoryAsync(effectiveManager, status.Name, ct)));
        }
        var data = new List<Contract.McpServerRuntimeStatus>(page.Count);
        foreach (var status in page)
        {
            var inventory = await effectiveManager.GetInventoryAsync(status.Name, ct);
            var resources = includeFullInventory
                ? inventory?.Resources.Select(ToRequiredElement).ToArray() ?? []
                : [];
            var resourceTemplates = includeFullInventory
                ? inventory?.ResourceTemplates.Select(ToRequiredElement).ToArray() ?? []
                : [];
            var tools = inventory?.Tools.ToDictionary(
                            static tool => tool.Name,
                            static tool => new Contract.McpRuntimeTool
                            {
                                Name = tool.Name,
                                Description = tool.Description,
                                InputSchema = tool.JsonSchema,
                                OutputSchema = tool.ReturnJsonSchema
                            },
                            StringComparer.Ordinal)
                        ?? new Dictionary<string, Contract.McpRuntimeTool>(StringComparer.Ordinal);
            data.Add(new Contract.McpServerRuntimeStatus
            {
                Name = status.Name,
                DeclaredName = status.Origin.DeclaredName ?? status.Name,
                RuntimeName = status.Name,
                Enabled = status.Enabled,
                StartupState = status.StartupState,
                LastError = status.LastError,
                AuthState = string.Equals(status.AuthStatus, McpAuthenticationStatuses.NotLoggedIn, StringComparison.Ordinal)
                    ? "loginRequired"
                    : "notRequired",
                ServerInfo = inventory?.ServerInfo is null ? null : ToElement(inventory.ServerInfo),
                Tools = new Protocol.Optional<IReadOnlyDictionary<string, Contract.McpRuntimeTool>>(tools),
                Resources = new Protocol.Optional<IReadOnlyList<JsonElement>>(resources),
                ResourceTemplates = new Protocol.Optional<IReadOnlyList<JsonElement>>(resourceTemplates),
                AuthStatus = status.AuthStatus,
                Transport = status.Transport,
                ToolCount = status.ToolCount,
                ResourceCount = resources.Length,
                ResourceTemplateCount = resourceTemplates.Length,
                Generation = await effectiveManager.GetGenerationAsync(status.Name, ct),
                Origin = McpContractMapper.ToContract(status.Origin),
                FailureReason = status.FailureReason
            });
        }

        var next = start + page.Count;
        return new Contract.McpServerStatusListResult
        {
            Data = new Protocol.Optional<IReadOnlyList<Contract.McpServerRuntimeStatus>>(data),
            NextCursor = next < statuses.Count ? next.ToString(System.Globalization.CultureInfo.InvariantCulture) : null
        };
    }

    private async Task<object?> HandleMcpServerResourceReadAsync(AppServerTypedRequest<Contract.McpServerResourceReadParams> request, CancellationToken ct)
    {
        var p = request.Params;
        var serverName = ValueOrDefault(p.Server) ?? string.Empty;
        var uri = ValueOrDefault(p.Uri) ?? string.Empty;
        var threadId = ValueOrDefault(p.ThreadId);
        EnsureRuntimeCallParams(serverName, uri, "uri");
        if (mcpClientManager == null)
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.McpServerResourceRead);

        var effectiveManager = !string.IsNullOrWhiteSpace(threadId) && threadMcpRuntimeService != null
            ? await threadMcpRuntimeService.GetEffectiveMcpRuntimeAsync(threadId, ct)
            : mcpClientManager;
        if (effectiveManager == null)
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.McpServerResourceRead);
        var result = await effectiveManager.ReadResourceAsync(serverName, uri, ct);
        return new Contract.McpServerResourceReadResult { Contents = ToElement(result.Contents) };
    }

    private async Task<object?> HandleMcpServerToolCallAsync(AppServerTypedRequest<Contract.McpServerToolCallParams> request, CancellationToken ct)
    {
        var p = request.Params;
        var threadId = ValueOrDefault(p.ThreadId) ?? string.Empty;
        var serverName = ValueOrDefault(p.Server) ?? string.Empty;
        var toolName = ValueOrDefault(p.Tool) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(threadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");
        EnsureRuntimeCallParams(serverName, toolName, "tool");
        if (mcpClientManager == null)
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.McpServerToolCall);

        if (threadToolDispatcher == null)
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.McpServerToolCall);

        var arguments = new JsonObject();
        var contractArguments = ValueOrDefault(p.Arguments);
        if (contractArguments != null)
        {
            foreach (var (name, value) in contractArguments)
                arguments[name] = JsonNode.Parse(value.GetRawText());
        }

        var dispatched = await threadToolDispatcher.DispatchThreadToolAsync(
            threadId,
            McpToolNaming.CanonicalToolName(serverName, toolName),
            arguments,
            $"host_{Guid.NewGuid():N}",
            ToolInvocationAudience.Host,
            ct);
        var result = dispatched.RawSourceResult is { } raw
            ? raw.Deserialize<CallToolResult>(SessionWireJsonOptions.Default)
            : null;
        return new Contract.McpServerToolCallResult
        {
            Content = ToElement(result != null
                ? result.Content
                : dispatched.Content == null
                    ? null
                    : new object[] { new { type = "text", text = dispatched.Content } }),
            StructuredContent = ToElement(result != null ? result.StructuredContent : dispatched.StructuredContent),
            IsError = result?.IsError ?? !dispatched.Success,
            Meta = ToElement(result != null ? result.Meta : dispatched.Meta)
        };
    }

    private async Task<object?> HandleMcpServerReloadAsync(AppServerTypedRequest<Protocol.RpcEmpty> request, CancellationToken ct)
    {
        _ = request;
        configService.EnsureManagementAvailable();
        var workspaceServers = await configService.GetWorkspaceServersAsync(ct);
        await configService.ReconnectEffectiveRuntimeAsync(workspaceServers, ct);
        threadAgentRefreshService?.InvalidateThreadAgents();
        return new Contract.McpServerReloadResult();
    }

    private async Task<object?> HandleMcpServerOAuthLoginAsync(AppServerTypedRequest<Contract.McpServerOAuthLoginParams> request, CancellationToken ct)
    {
        var p = request.Params;
        var name = ValueOrDefault(p.Name) ?? string.Empty;
        var threadId = ValueOrDefault(p.ThreadId);
        if (string.IsNullOrWhiteSpace(name))
            throw AppServerErrors.InvalidParams("'name' is required.");
        if (mcpClientManager == null)
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.McpServerOAuthLogin);

        var effectiveManager = !string.IsNullOrWhiteSpace(threadId) && threadMcpRuntimeService != null
            ? await threadMcpRuntimeService.GetEffectiveMcpRuntimeAsync(threadId, ct)
            : mcpClientManager;
        if (effectiveManager == null)
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.McpServerOAuthLogin);

        var server = await effectiveManager.GetConfigAsync(name, ct)
                     ?? throw AppServerErrors.McpServerNotFound(name);
        var runtimeStatus = (await effectiveManager.ListStatusesAsync(ct))
            .FirstOrDefault(status => string.Equals(status.Name, name, StringComparison.OrdinalIgnoreCase));
        if (runtimeStatus == null
            || !string.Equals(runtimeStatus.AuthStatus, McpAuthenticationStatuses.NotLoggedIn, StringComparison.Ordinal))
        {
            throw AppServerErrors.InvalidRequest(
                $"MCP server '{name}' does not currently require OAuth authentication.");
        }
        var authorizationUrl = await McpOAuthLoginCoordinator.BeginAsync(
            server,
            ValueOrDefault(p.Scopes)?.ToList(),
            ValueOrDefault(p.TimeoutSecs),
            effectiveManager.UserDataPath,
            async (success, error) =>
            {
                if (success)
                {
                    try
                    {
                        await effectiveManager.UpsertAsync(server, CancellationToken.None);
                    }
                    catch (Exception reloadError)
                    {
                        success = false;
                        error = reloadError.Message;
                    }
                }

                await transport.NotifyContractAsync(
                    Contract.AppServerRpc.McpServerOAuthLoginCompleted,
                    new Contract.McpServerOAuthLoginCompletedNotification
                    {
                        Name = server.Name,
                        ThreadId = threadId,
                        Success = success,
                        Error = error
                    },
                    CancellationToken.None);
            },
            ct);

        return new Contract.McpServerOAuthLoginResult { AuthorizationUrl = authorizationUrl };
    }

    private static int ParseCursor(string? cursor, int count)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 0;
        if (!int.TryParse(cursor, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var value) || value < 0 || value > count)
            throw AppServerErrors.InvalidParams("'cursor' is invalid.");
        return value;
    }

    private static async Task RefreshOptionalInventoryAsync(
        McpClientManager manager,
        string serverName,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await manager.RefreshResourceInventoryAsync(serverName, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Optional inventory timeout does not affect server readiness.
        }
        catch
        {
            // Optional inventory failure is represented as an empty collection.
        }
    }

    private static void EnsureRuntimeCallParams(string server, string value, string valueName)
    {
        if (string.IsNullOrWhiteSpace(server))
            throw AppServerErrors.InvalidParams("'server' is required.");
        if (string.IsNullOrWhiteSpace(value))
            throw AppServerErrors.InvalidParams($"'{valueName}' is required.");
    }

    private static T? ValueOrDefault<T>(Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;

    private static T Require<T>(Protocol.Optional<T> value, string message)
        where T : class =>
        value.IsSet && value.Value is { } present ? present : throw AppServerErrors.InvalidParams(message);

    private static JsonElement? ToElement(object? value) =>
        value is null ? null : JsonSerializer.SerializeToElement(value, SessionWireJsonOptions.Default);

    private static JsonElement ToRequiredElement(object value) =>
        JsonSerializer.SerializeToElement(value, SessionWireJsonOptions.Default);
}
