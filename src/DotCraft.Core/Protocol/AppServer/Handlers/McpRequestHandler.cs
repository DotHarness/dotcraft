using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Mcp;
using DotCraft.Tools;
using ModelContextProtocol.Protocol;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Handles the <c>mcp/*</c> wire methods, sharing workspace MCP persistence with plugin mutations
/// through <see cref="AppServerMcpConfigService"/>.
/// </summary>
internal sealed class McpRequestHandler(
    McpClientManager? mcpClientManager,
    AppServerMcpConfigService configService,
    IAppServerTransport transport,
    IAppConfigMonitor? appConfigMonitor,
    Action<McpStatusInfoWire>? broadcastMcpStatusChanged,
    IThreadToolDispatchService? threadToolDispatcher,
    IThreadMcpRuntimeService? threadMcpRuntimeService,
    IThreadAgentRefreshService? threadAgentRefreshService) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        mcpClientManager?.ConfigureElicitationHandler(HandleElicitationAsync);
        table.Map(AppServerMethods.McpList, HandleMcpListAsync);
        table.Map(AppServerMethods.McpGet, HandleMcpGetAsync);
        table.Map(AppServerMethods.McpUpsert, HandleMcpUpsertAsync);
        table.Map(AppServerMethods.McpRemove, HandleMcpRemoveAsync);
        table.Map(AppServerMethods.McpTest, HandleMcpTestAsync);
        table.Map(AppServerMethods.McpServerStatusList, HandleMcpServerStatusListAsync);
        table.Map(AppServerMethods.McpServerResourceRead, HandleMcpServerResourceReadAsync);
        table.Map(AppServerMethods.McpServerToolCall, HandleMcpServerToolCallAsync);
        table.Map(AppServerMethods.McpServerOAuthLogin, HandleMcpServerOAuthLoginAsync);
        table.Map(AppServerMethods.ConfigMcpServerReload, HandleMcpServerReloadAsync);
    }

    private async Task<object?> HandleMcpListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = msg;
        configService.EnsureManagementAvailable();
        var servers = await mcpClientManager!.ListConfigsAsync(ct);
        return new McpListResult { Servers = servers.Select(McpWireMapper.ToWire).ToList() };
    }

    private async Task<object?> HandleMcpGetAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<McpGetParams>(msg);
        configService.EnsureManagementAvailable();
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var server = await mcpClientManager!.GetConfigAsync(p.Name, ct);
        if (server == null)
            throw AppServerErrors.McpServerNotFound(p.Name);

        return new McpGetResult { Server = McpWireMapper.ToWire(server) };
    }

    private async Task<object?> HandleMcpUpsertAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<McpUpsertParams>(msg);
        configService.EnsureManagementAvailable();
        McpWireMapper.ValidateConfig(p.Server);

        var server = McpWireMapper.FromWire(p.Server);
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
            AppServerMethods.McpUpsert,
            [ConfigChangeRegions.Mcp]);

        var updated = await mcpClientManager.GetConfigAsync(server.Name, ct) ?? server;
        var status = (await mcpClientManager.ListStatusesAsync(ct))
            .FirstOrDefault(s => string.Equals(s.Name, updated.Name, StringComparison.OrdinalIgnoreCase));
        if (status != null)
            broadcastMcpStatusChanged?.Invoke(McpWireMapper.ToWire(status));

        return new McpUpsertResult { Server = McpWireMapper.ToWire(updated) };
    }

    private async Task<object?> HandleMcpRemoveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<McpRemoveParams>(msg);
        configService.EnsureManagementAvailable();
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var existing = await mcpClientManager!.GetConfigAsync(p.Name, ct);
        if (existing == null)
            throw AppServerErrors.McpServerNotFound(p.Name);
        if (existing.ReadOnly)
            throw AppServerErrors.McpServerReadOnly(p.Name);

        var workspaceServers = await configService.GetWorkspaceServersAsync(ct);
        var removed = workspaceServers.RemoveAll(
            candidate => string.Equals(candidate.Name, p.Name, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed)
            throw AppServerErrors.McpServerNotFound(p.Name);

        await configService.SaveWorkspaceServersAsync(workspaceServers, ct);
        configService.SetCurrentWorkspaceServers(workspaceServers);
        await configService.ReconnectEffectiveRuntimeAsync(workspaceServers, ct);
        threadAgentRefreshService?.InvalidateThreadAgents();
        appConfigMonitor?.NotifyChanged(
            AppServerMethods.McpRemove,
            [ConfigChangeRegions.Mcp]);
        return new McpRemoveResult { Removed = true };
    }

    private async Task<object?> HandleMcpTestAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<McpTestParams>(msg);
        if (mcpClientManager == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.McpTest);

        McpWireMapper.ValidateConfig(p.Server);
        var status = await mcpClientManager.TestAsync(McpWireMapper.FromWire(p.Server), ct);
        return new McpTestResult
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
            var response = await transport.SendClientRequestAsync(
                AppServerMethods.McpServerElicitationRequest,
                new McpServerElicitationRequestParams
                {
                    ServerName = serverName,
                    Mode = string.IsNullOrWhiteSpace(request.Mode) ? "form" : request.Mode,
                    ElicitationId = request.ElicitationId,
                    Message = request.Message,
                    Url = request.Url,
                    RequestedSchema = requestedSchema
                },
                ct,
                Timeout.InfiniteTimeSpan);

            if (!response.Result.HasValue)
                return new ElicitResult { Action = "cancel" };

            var result = response.Result.Value.Deserialize<McpServerElicitationResponse>(
                SessionWireJsonOptions.Default);
            var action = result?.Action switch
            {
                "accept" when requestedSchema == null
                              || McpElicitationSchemaValidator.TryValidateContent(requestedSchema, result.Content) => "accept",
                "accept" => "decline",
                "decline" => "decline",
                _ => "cancel"
            };
            return new ElicitResult
            {
                Action = action,
                Content = action == "accept" ? result?.Content : null
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

    private async Task<object?> HandleMcpServerStatusListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<McpServerStatusListParams>(msg);
        if (mcpClientManager == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.McpServerStatusList);
        if (p.Detail is not null && p.Detail is not "full" and not "toolsAndAuthOnly")
            throw AppServerErrors.InvalidParams("'detail' must be 'full' or 'toolsAndAuthOnly'.");

        var effectiveManager = !string.IsNullOrWhiteSpace(p.ThreadId) && threadMcpRuntimeService != null
            ? await threadMcpRuntimeService.GetEffectiveMcpRuntimeAsync(p.ThreadId, ct)
            : mcpClientManager;
        if (effectiveManager == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.McpServerStatusList);
        var statuses = await effectiveManager.ListStatusesAsync(ct);
        var start = ParseCursor(p.Cursor, statuses.Count);
        var limit = Math.Clamp(p.Limit ?? 100, 1, 500);
        var page = statuses.Skip(start).Take(limit).ToList();
        var data = new List<McpServerRuntimeStatusWire>(page.Count);
        foreach (var status in page)
        {
            var inventory = await effectiveManager.GetInventoryAsync(status.Name, ct);
            var config = await effectiveManager.GetConfigAsync(status.Name, ct);
            data.Add(new McpServerRuntimeStatusWire
            {
                Name = status.Name,
                DeclaredName = status.Origin.DeclaredName ?? status.Name,
                RuntimeName = status.Name,
                Enabled = status.Enabled,
                StartupState = status.StartupState,
                AuthState = InferAuthState(status),
                ServerInfo = inventory?.ServerInfo,
                Tools = inventory?.Tools.ToDictionary(
                            static tool => tool.Name,
                            static tool => (object)new
                            {
                                name = tool.Name,
                                description = tool.Description,
                                inputSchema = tool.JsonSchema,
                                outputSchema = tool.ReturnJsonSchema
                            },
                            StringComparer.Ordinal)
                        ?? new Dictionary<string, object>(StringComparer.Ordinal),
                Resources = inventory?.Resources.Cast<object>().ToList() ?? [],
                ResourceTemplates = inventory?.ResourceTemplates.Cast<object>().ToList() ?? [],
                AuthStatus = await ResolveAuthStatusAsync(config, status, ct),
                Transport = status.Transport,
                ToolCount = status.ToolCount,
                ResourceCount = status.ResourceCount,
                ResourceTemplateCount = status.ResourceTemplateCount,
                Generation = await effectiveManager.GetGenerationAsync(status.Name, ct),
                Origin = McpWireMapper.ToWire(status.Origin),
                FailureReason = string.Equals(InferAuthState(status), "loginRequired", StringComparison.Ordinal)
                    ? "reauthenticationRequired"
                    : null
            });
        }

        var next = start + page.Count;
        return new McpServerStatusListResult
        {
            Data = data,
            NextCursor = next < statuses.Count ? next.ToString(System.Globalization.CultureInfo.InvariantCulture) : null
        };
    }

    private async Task<object?> HandleMcpServerResourceReadAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<McpServerResourceReadParams>(msg);
        EnsureRuntimeCallParams(p.Server, p.Uri, "uri");
        if (mcpClientManager == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.McpServerResourceRead);

        var effectiveManager = !string.IsNullOrWhiteSpace(p.ThreadId) && threadMcpRuntimeService != null
            ? await threadMcpRuntimeService.GetEffectiveMcpRuntimeAsync(p.ThreadId, ct)
            : mcpClientManager;
        if (effectiveManager == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.McpServerResourceRead);
        var result = await effectiveManager.ReadResourceAsync(p.Server, p.Uri, ct);
        return new McpServerResourceReadResult { Contents = result.Contents };
    }

    private async Task<object?> HandleMcpServerToolCallAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<McpServerToolCallParams>(msg);
        if (string.IsNullOrWhiteSpace(p.ThreadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");
        EnsureRuntimeCallParams(p.Server, p.Tool, "tool");
        if (mcpClientManager == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.McpServerToolCall);

        if (threadToolDispatcher == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.McpServerToolCall);

        var arguments = new JsonObject();
        if (p.Arguments != null)
        {
            foreach (var (name, value) in p.Arguments)
                arguments[name] = JsonNode.Parse(value.GetRawText());
        }

        var dispatched = await threadToolDispatcher.DispatchThreadToolAsync(
            p.ThreadId,
            McpToolNaming.CanonicalToolName(p.Server, p.Tool),
            arguments,
            $"host_{Guid.NewGuid():N}",
            ToolInvocationAudience.Host,
            ct);
        var result = dispatched.RawSourceResult is { } raw
            ? raw.Deserialize<CallToolResult>(SessionWireJsonOptions.Default)
            : null;
        return new McpServerToolCallResult
        {
            Content = result != null
                ? result.Content
                : dispatched.Content == null
                    ? null
                    : new object[] { new { type = "text", text = dispatched.Content } },
            StructuredContent = result != null ? result.StructuredContent : dispatched.StructuredContent,
            IsError = result?.IsError ?? !dispatched.Success,
            Meta = result != null ? result.Meta : dispatched.Meta
        };
    }

    private async Task<object?> HandleMcpServerReloadAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = msg;
        configService.EnsureManagementAvailable();
        var workspaceServers = await configService.GetWorkspaceServersAsync(ct);
        await configService.ReconnectEffectiveRuntimeAsync(workspaceServers, ct);
        threadAgentRefreshService?.InvalidateThreadAgents();
        return new McpServerReloadResult();
    }

    private async Task<object?> HandleMcpServerOAuthLoginAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<McpServerOAuthLoginParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");
        if (mcpClientManager == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.McpServerOAuthLogin);

        var server = await mcpClientManager.GetConfigAsync(p.Name, ct)
                     ?? throw AppServerErrors.McpServerNotFound(p.Name);
        var authorizationUrl = await McpOAuthLoginCoordinator.BeginAsync(
            server,
            p.Scopes,
            p.TimeoutSecs,
            async (success, error) =>
            {
                if (success)
                {
                    try
                    {
                        var workspaceServers = await configService.GetWorkspaceServersAsync(CancellationToken.None);
                        await configService.ReconnectEffectiveRuntimeAsync(workspaceServers, CancellationToken.None);
                    }
                    catch (Exception reloadError)
                    {
                        success = false;
                        error = reloadError.Message;
                    }
                }

                await transport.WriteMessageAsync(new
                {
                    jsonrpc = "2.0",
                    method = AppServerMethods.McpServerOAuthLoginCompleted,
                    @params = new McpServerOAuthLoginCompletedNotification
                    {
                        Name = server.Name,
                        ThreadId = p.ThreadId,
                        Success = success,
                        Error = error
                    }
                }, CancellationToken.None);
            },
            ct);

        return new McpServerOAuthLoginResult { AuthorizationUrl = authorizationUrl };
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

    private static string InferAuthState(McpServerStatusSnapshot status)
    {
        if (status.LastError?.Contains("401", StringComparison.OrdinalIgnoreCase) == true ||
            status.LastError?.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) == true ||
            status.LastError?.Contains("authentication", StringComparison.OrdinalIgnoreCase) == true)
            return "loginRequired";
        return "notRequired";
    }

    private static async Task<string> ResolveAuthStatusAsync(
        McpServerConfig? config,
        McpServerStatusSnapshot status,
        CancellationToken ct)
    {
        if (config == null || !string.Equals(config.NormalizedTransport, "streamableHttp", StringComparison.Ordinal))
            return "unsupported";
        if (await McpOAuthTokenStore.Create(config).HasTokensAsync(ct))
            return "oAuth";
        if (!string.IsNullOrWhiteSpace(config.BearerTokenEnvVar) || config.Headers.ContainsKey("Authorization"))
            return "bearerToken";
        return string.Equals(InferAuthState(status), "loginRequired", StringComparison.Ordinal)
            ? "notLoggedIn"
            : "unsupported";
    }

    private static void EnsureRuntimeCallParams(string server, string value, string valueName)
    {
        if (string.IsNullOrWhiteSpace(server))
            throw AppServerErrors.InvalidParams("'server' is required.");
        if (string.IsNullOrWhiteSpace(value))
            throw AppServerErrors.InvalidParams($"'{valueName}' is required.");
    }
}
