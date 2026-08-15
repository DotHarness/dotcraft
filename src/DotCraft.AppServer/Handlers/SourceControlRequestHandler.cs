using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.SourceControl;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;
using SessionThread = DotCraft.Sessions.SessionThread;

namespace DotCraft.AppServer;

/// <summary>
/// Handles workspace source control binding methods (spec Section 25A): reading the
/// effective binding snapshot and persisting non-sensitive provider/connection config.
/// Connection testing (<c>sourceControl/test</c>) is registered separately.
/// </summary>
internal sealed class SourceControlRequestHandler(
    ISessionService sessionService,
    IAppConfigMonitor? appConfigMonitor,
    string? workspaceCraftPath,
    string? hostWorkspacePath) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Protocol.AppServer.AppServerRpc.SourceControlGet, HandleGetAsync);
        table.Map(Protocol.AppServer.AppServerRpc.SourceControlUpdate, HandleUpdateAsync);
        table.Map(Protocol.AppServer.AppServerRpc.SourceControlTest, HandleTestAsync);
        table.Map(Protocol.AppServer.AppServerRpc.SourceControlChangelistList, HandleChangelistListAsync);
        table.Map(Protocol.AppServer.AppServerRpc.SourceControlChangelistCreate, HandleChangelistCreateAsync);
        table.Map(Protocol.AppServer.AppServerRpc.SourceControlChangelistPrepare, HandleChangelistPrepareAsync);
        table.Map(Protocol.AppServer.AppServerRpc.SourceControlThreadTargetGet, HandleThreadTargetGetAsync);
        table.Map(Protocol.AppServer.AppServerRpc.SourceControlThreadTargetUpdate, HandleThreadTargetUpdateAsync);
    }

    private Task<object?> HandleGetAsync(
        AppServerTypedRequest<Contract.SourceControlGetParams> request,
        CancellationToken ct)
    {
        _ = ct;
        EnsureManagementAvailable();
        _ = request.Params;

        var config = appConfigMonitor?.Current.SourceControl ?? new SourceControlConfig();
        return Task.FromResult<object?>(BuildSnapshot(config));
    }

    private Task<object?> HandleUpdateAsync(
        AppServerTypedRequest<Contract.SourceControlUpdateParams> request,
        CancellationToken ct)
    {
        _ = ct;
        EnsureManagementAvailable();

        if (!request.Message.Params.HasValue || request.Message.Params.Value.ValueKind != JsonValueKind.Object)
            throw AppServerErrors.InvalidParams("'provider' is required.");

        // Defense in depth: passwords are never persisted to workspace config.
        if (ParamsContainPassword(request.Message.Params.Value))
            throw AppServerErrors.InvalidParams(
                "Passwords are not accepted by sourceControl/update and are never persisted. Use sourceControl/test for a transient login.");

        var p = request.Params;
        if (string.IsNullOrWhiteSpace(ValueOrDefault(p.Provider)))
            throw AppServerErrors.InvalidParams("'provider' is required.");

        var current = appConfigMonitor?.Current.SourceControl ?? new SourceControlConfig();
        SourceControlConfig updated;
        try
        {
            updated = ApplyUpdate(current, p, request.Message.Params.Value);
        }
        catch (ArgumentException ex)
        {
            throw AppServerErrors.InvalidParams(ex.Message);
        }

        Persist(updated);

        if (appConfigMonitor != null)
        {
            appConfigMonitor.Current.SourceControl = updated;
            appConfigMonitor.NotifyChanged(Protocol.AppServer.AppServerMethodNames.SourceControlUpdate, [ConfigChangeRegions.SourceControl]);
        }

        return Task.FromResult<object?>(BuildSnapshot(updated));
    }

    private async Task<object?> HandleTestAsync(
        AppServerTypedRequest<Contract.SourceControlTestParams> request,
        CancellationToken ct)
    {
        EnsureManagementAvailable();

        var p = request.Params;
        if (!string.Equals(ValueOrDefault(p.Provider), SourceControlProviders.Perforce, StringComparison.OrdinalIgnoreCase))
            throw AppServerErrors.InvalidParams("sourceControl/test currently supports only provider 'perforce'.");

        var perforce = ValueOrDefault(p.Perforce) ?? new Contract.PerforceConnection();
        var mode = SourceControlConnectionModes.Normalize(ValueOrDefault(p.ConnectionMode) ?? SourceControlConnectionModes.P4Config);
        var workspacePath = ResolveHostWorkspacePath();
        var configuredTimeout = ValueOrDefault(perforce.TimeoutSeconds);
        var executablePath = ValueOrDefault(perforce.P4ExecutablePath);
        var timeoutSeconds = configuredTimeout > 0 ? configuredTimeout : 30;
        var executable = string.IsNullOrWhiteSpace(executablePath) ? "p4" : executablePath.Trim();

        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        var p4ConfigName = ValueOrDefault(perforce.P4ConfigName);
        if (mode == SourceControlConnectionModes.P4Config && !string.IsNullOrWhiteSpace(p4ConfigName))
            env["P4CONFIG"] = p4ConfigName.Trim();

        var runner = new DefaultPerforceCommandRunner(executable, workspacePath, TimeSpan.FromSeconds(timeoutSeconds), env);
        var testRequest = new PerforceTestRequest
        {
            WorkspacePath = workspacePath,
            ConnectionMode = mode,
            Port = ValueOrDefault(perforce.Port) ?? string.Empty,
            Client = ValueOrDefault(perforce.Client) ?? string.Empty,
            User = ValueOrDefault(perforce.User) ?? string.Empty,
            Charset = ValueOrDefault(perforce.Charset) ?? string.Empty,
            P4ConfigName = p4ConfigName ?? string.Empty,
            TimeoutSeconds = timeoutSeconds,
            // Transient: used only for a one-shot login, never persisted or logged.
            Password = ValueOrDefault(p.Password)
        };

        var report = await PerforceConnectionTester.TestAsync(runner, testRequest, ct);
        return ToContract(report);
    }

    private async Task<object?> HandleThreadTargetGetAsync(
        AppServerTypedRequest<Contract.SourceControlThreadTargetParams> request,
        CancellationToken ct)
    {
        EnsureManagementAvailable();
        var p = request.Params;
        var thread = await GetRequiredThreadAsync(ValueOrDefault(p.ThreadId), ct);
        return new Contract.SourceControlThreadTargetResult
        {
            Target = ToContract(ThreadSourceControlMetadata.GetPerforceTarget(thread.Metadata))
        };
    }

    private async Task<object?> HandleThreadTargetUpdateAsync(
        AppServerTypedRequest<Contract.SourceControlThreadTargetUpdateParams> request,
        CancellationToken ct)
    {
        EnsureManagementAvailable();
        var p = request.Params;
        var threadId = ValueOrDefault(p.ThreadId);
        _ = await GetRequiredThreadAsync(threadId, ct);

        var contractTarget = ValueOrDefault(p.Target);
        var target = contractTarget == null
            ? null
            : new ThreadSourceControlTarget
            {
                Provider = string.IsNullOrWhiteSpace(ValueOrDefault(contractTarget.Provider))
                    ? SourceControlProviders.Perforce
                    : ValueOrDefault(contractTarget.Provider)!,
                Changelist = PerforceChangelistManager.NormalizeChangelist(ValueOrDefault(contractTarget.Changelist))
            };
        var updated = await sessionService.UpdateThreadSourceControlTargetAsync(threadId!, target, ct);
        return new Contract.SourceControlThreadTargetResult
        {
            Target = ToContract(ThreadSourceControlMetadata.GetPerforceTarget(updated.Metadata))
        };
    }

    private async Task<object?> HandleChangelistListAsync(
        AppServerTypedRequest<Contract.SourceControlChangelistListParams> request,
        CancellationToken ct)
    {
        EnsureManagementAvailable();
        var p = request.Params;
        var threadId = ValueOrDefault(p.ThreadId);
        var config = RequirePerforceConfig();

        // Welcome (pre-thread) listing has no thread: enumerate the foreground workspace's
        // pending changelists and report the default target, mirroring how git branches list
        // before a thread exists.
        if (string.IsNullOrWhiteSpace(threadId))
        {
            var welcomeManager = CreatePerforceManager(config, ResolveHostWorkspacePath());
            var welcomeEntries = await welcomeManager.ListAsync(ct);
            return new Contract.SourceControlChangelistListResult
            {
                Changelists = welcomeEntries.Select(ToContract).ToList(),
                Target = ToContract(ThreadSourceControlMetadata.DefaultPerforceTarget)
            };
        }

        var thread = await GetRequiredThreadAsync(threadId, ct);
        var manager = CreatePerforceManager(config, ResolveEffectiveWorkspacePath(thread));
        var entries = await manager.ListAsync(ct);
        return new Contract.SourceControlChangelistListResult
        {
            Changelists = entries.Select(ToContract).ToList(),
            Target = ToContract(ThreadSourceControlMetadata.GetPerforceTarget(thread.Metadata))
        };
    }

    private async Task<object?> HandleChangelistCreateAsync(
        AppServerTypedRequest<Contract.SourceControlChangelistCreateParams> request,
        CancellationToken ct)
    {
        EnsureManagementAvailable();
        var p = request.Params;
        var threadId = ValueOrDefault(p.ThreadId);
        var description = ValueOrDefault(p.Description) ?? string.Empty;
        var config = RequirePerforceConfig();

        // Welcome (pre-thread) creation: create on the foreground workspace. There is no thread
        // yet to persist a target, so the client carries the returned id as its pre-selection
        // until the first thread is started.
        if (string.IsNullOrWhiteSpace(threadId))
        {
            var welcomeManager = CreatePerforceManager(config, ResolveHostWorkspacePath());
            var welcomeEntry = await welcomeManager.CreateAsync(description, ct);
            return new Contract.SourceControlChangelistCreateResult
            {
                Changelist = ToContract(welcomeEntry),
                Target = ToContract(new ThreadSourceControlTarget
                {
                    Provider = SourceControlProviders.Perforce,
                    Changelist = welcomeEntry.Id
                })
            };
        }

        var thread = await GetRequiredThreadAsync(threadId, ct);
        var manager = CreatePerforceManager(config, ResolveEffectiveWorkspacePath(thread));
        var entry = await manager.CreateAsync(description, ct);
        var target = ThreadSourceControlMetadata.GetPerforceTarget(thread.Metadata);
        if (ValueOrDefault(p.SetAsTarget))
        {
            var updated = await sessionService.UpdateThreadSourceControlTargetAsync(
                threadId!,
                new ThreadSourceControlTarget
                {
                    Provider = SourceControlProviders.Perforce,
                    Changelist = entry.Id
                },
                ct);
            target = ThreadSourceControlMetadata.GetPerforceTarget(updated.Metadata);
        }

        return new Contract.SourceControlChangelistCreateResult
        {
            Changelist = ToContract(entry),
            Target = ToContract(target)
        };
    }

    private async Task<object?> HandleChangelistPrepareAsync(
        AppServerTypedRequest<Contract.SourceControlChangelistPrepareParams> request,
        CancellationToken ct)
    {
        EnsureManagementAvailable();
        var p = request.Params;
        var threadId = ValueOrDefault(p.ThreadId);
        var thread = await GetRequiredThreadAsync(threadId, ct);
        var config = RequirePerforceConfig();
        var requestedTarget = ValueOrDefault(p.Target);
        var target = string.IsNullOrWhiteSpace(requestedTarget)
            ? ThreadSourceControlMetadata.GetPerforceTarget(thread.Metadata).Changelist
            : PerforceChangelistManager.NormalizeChangelist(requestedTarget);
        var manager = CreatePerforceManager(config, ResolveEffectiveWorkspacePath(thread));
        var result = await manager.PrepareAsync(
            ValueOrDefault(p.Paths) ?? [],
            target,
            ValueOrDefault(p.Description) ?? string.Empty,
            ct);
        if (string.Equals(result.Status, "ok", StringComparison.Ordinal)
            && !string.Equals(result.Changelist, target, StringComparison.Ordinal))
        {
            await sessionService.UpdateThreadSourceControlTargetAsync(
                threadId!,
                new ThreadSourceControlTarget
                {
                    Provider = SourceControlProviders.Perforce,
                    Changelist = result.Changelist
                },
                ct);
        }

        return ToContract(result);
    }

    private static Contract.SourceControlTestResult ToContract(PerforceConnectionReport r) => new()
    {
        Status = r.Status,
        Code = r.Code,
        Summary = r.Summary,
        FallbackText = r.FallbackText,
        Identity = new Contract.SourceControlTestIdentity
        {
            ServerAddress = r.Identity.ServerAddress,
            User = r.Identity.User,
            Client = r.Identity.Client,
            Charset = r.Identity.Charset,
            ConnectionMode = r.Identity.ConnectionMode
        },
        Workspace = new Contract.SourceControlTestWorkspace
        {
            WorkspacePath = r.Workspace.WorkspacePath,
            ClientRoot = r.Workspace.ClientRoot,
            AltRoots = r.Workspace.AltRoots?.ToList() ?? [],
            MappingOk = r.Workspace.MappingOk
        },
        Authentication = new Contract.SourceControlTestAuthentication
        {
            TicketStatus = r.Authentication.TicketStatus,
            LoginRequired = r.Authentication.LoginRequired,
            ExpiresMessage = r.Authentication.ExpiresMessage
        },
        Diagnostics = new Contract.SourceControlTestDiagnostics
        {
            P4Version = r.Diagnostics.P4Version,
            TimeoutSeconds = r.Diagnostics.TimeoutSeconds,
            WarningCount = r.Warnings.Count,
            ErrorCode = r.Diagnostics.ErrorCode
        },
        Warnings = r.Warnings.Select(w => new Contract.SourceControlDiagnosticItem { Code = w.Code, FallbackText = w.FallbackText }).ToList(),
        Errors = r.Errors.Select(e => new Contract.SourceControlDiagnosticItem { Code = e.Code, FallbackText = e.FallbackText }).ToList()
    };

    private void EnsureManagementAvailable()
    {
        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.SourceControlGet);
    }

    /// <summary>Builds the wire snapshot from config, resolving the effective provider and status.</summary>
    private Contract.SourceControlSnapshot BuildSnapshot(SourceControlConfig config)
    {
        var workspacePath = ResolveHostWorkspacePath();
        var effective = SourceControlResolver.ResolveEffectiveProvider(config, workspacePath);
        var status = SourceControlResolver.DeriveStatus(config, effective);
        var perforceOnline = config.Perforce.Online;
        var includePerforce =
            effective == SourceControlProviders.Perforce
            || SourceControlProviders.Normalize(config.Provider) == SourceControlProviders.Perforce
            || SourceControlResolver.HasPerforceBinding(config);

        return new Contract.SourceControlSnapshot
        {
            Provider = SourceControlProviders.Normalize(config.Provider),
            EffectiveProvider = effective,
            ConnectionMode = SourceControlConnectionModes.Normalize(config.ConnectionMode),
            Status = status,
            WorkspacePath = workspacePath,
            Perforce = includePerforce
                ? Protocol.Optional<Contract.PerforceConnection?>.FromValue(ToContract(config.Perforce))
                : default,
            Capabilities = new Contract.SourceControlCapabilities
            {
                GitCommit = effective == SourceControlProviders.Git,
                PerforceBinding = effective == SourceControlProviders.Perforce,
                PerforceChangelist = effective == SourceControlProviders.Perforce && perforceOnline,
                PerforceShelve = false,
                PerforceSubmit = false
            }
        };
    }

    /// <summary>Merges update params into the current config, preserving omitted Perforce fields.</summary>
    private static SourceControlConfig ApplyUpdate(SourceControlConfig current, Contract.SourceControlUpdateParams p, JsonElement paramsEl)
    {
        return new SourceControlConfig
        {
            Provider = SourceControlProviders.Normalize(ValueOrDefault(p.Provider)),
            ConnectionMode = ValueOrDefault(p.ConnectionMode) != null
                ? SourceControlConnectionModes.Normalize(ValueOrDefault(p.ConnectionMode))
                : SourceControlConnectionModes.Normalize(current.ConnectionMode),
            Perforce = MergePerforceUpdate(current.Perforce, paramsEl)
        };
    }

    private void Persist(SourceControlConfig config)
    {
        var configPath = Path.Combine(workspaceCraftPath!, "config.json");
        Directory.CreateDirectory(workspaceCraftPath!);
        var root = WorkspaceConfigEditor.LoadObject(configPath);
        var key = WorkspaceConfigEditor.FindCaseInsensitiveKey(root, "SourceControl") ?? "SourceControl";
        var node = JsonSerializer.SerializeToNode(config, AppConfig.SerializerOptions);
        if (node != null)
            root[key] = node;
        WorkspaceConfigEditor.WriteObject(configPath, root);
    }

    private string ResolveHostWorkspacePath() =>
        hostWorkspacePath
        ?? (workspaceCraftPath == null ? Directory.GetCurrentDirectory() : Directory.GetParent(workspaceCraftPath)?.FullName)
        ?? Directory.GetCurrentDirectory();

    private async Task<SessionThread> GetRequiredThreadAsync(string? threadId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");
        return await sessionService.GetThreadAsync(threadId.Trim(), ct);
    }

    private SourceControlConfig RequirePerforceConfig()
    {
        var config = appConfigMonitor?.Current.SourceControl ?? new SourceControlConfig();
        var effective = SourceControlResolver.ResolveEffectiveProvider(config, ResolveHostWorkspacePath());
        if (!string.Equals(effective, SourceControlProviders.Perforce, StringComparison.OrdinalIgnoreCase))
            throw AppServerErrors.InvalidParams("Perforce changelist methods require a Perforce source control binding.");
        if (!config.Perforce.Online)
            throw AppServerErrors.InvalidParams("Perforce source control is offline. Test the connection and save it online before using changelist methods.");
        return config;
    }

    private PerforceChangelistManager CreatePerforceManager(SourceControlConfig config, string workspacePath)
    {
        var perforce = config.Perforce;
        var timeoutSeconds = perforce.TimeoutSeconds > 0 ? perforce.TimeoutSeconds : 30;
        var executable = string.IsNullOrWhiteSpace(perforce.P4ExecutablePath) ? "p4" : perforce.P4ExecutablePath.Trim();
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        var mode = SourceControlConnectionModes.Normalize(config.ConnectionMode);
        if (mode == SourceControlConnectionModes.P4Config && !string.IsNullOrWhiteSpace(perforce.P4ConfigName))
            env["P4CONFIG"] = perforce.P4ConfigName.Trim();
        var runner = new DefaultPerforceCommandRunner(executable, workspacePath, TimeSpan.FromSeconds(timeoutSeconds), env);
        return new PerforceChangelistManager(runner, new PerforceWorkspaceCommandOptions
        {
            WorkspacePath = workspacePath,
            ConnectionMode = mode,
            Port = perforce.Port,
            Client = perforce.Client,
            User = perforce.User,
            Charset = perforce.Charset
        });
    }

    private static string ResolveEffectiveWorkspacePath(SessionThread thread)
        => ThreadWorkspaceResolver.Resolve(thread).Cwd;

    private static Contract.PerforceConnection ToContract(PerforceConnectionConfig c) => new()
    {
        Port = c.Port,
        Client = c.Client,
        User = c.User,
        Charset = c.Charset,
        P4ConfigName = c.P4ConfigName,
        P4ExecutablePath = c.P4ExecutablePath,
        TimeoutSeconds = c.TimeoutSeconds,
        Online = c.Online,
        AutoOffline = c.AutoOffline
    };

    private static Contract.SourceControlThreadTarget ToContract(ThreadSourceControlTarget target) => new()
    {
        Provider = target.Provider,
        Changelist = target.Changelist
    };

    private static Contract.SourceControlChangelistEntry ToContract(PerforceChangelistEntry entry) => new()
    {
        Id = entry.Id,
        IsDefault = entry.IsDefault,
        Description = entry.Description,
        User = entry.User,
        Client = entry.Client,
        Status = entry.Status
    };

    private static Contract.SourceControlChangelistPrepareResult ToContract(PerforceChangelistPrepareResult result) => new()
    {
        Status = result.Status,
        Code = result.Code,
        Changelist = result.Changelist,
        Created = result.Created,
        MovedPaths = result.MovedPaths.ToList(),
        SkippedPaths = result.SkippedPaths.ToList(),
        Warnings = result.Warnings.Select(w => new Contract.SourceControlDiagnosticItem { Code = w.Code, FallbackText = w.FallbackText }).ToList(),
        Errors = result.Errors.Select(e => new Contract.SourceControlDiagnosticItem { Code = e.Code, FallbackText = e.FallbackText }).ToList()
    };

    private static T? ValueOrDefault<T>(Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;

    private static PerforceConnectionConfig MergePerforceUpdate(PerforceConnectionConfig? current, JsonElement paramsEl)
    {
        if (!TryGetCaseInsensitiveProperty(paramsEl, "perforce", out var perforceEl))
            return ClonePerforce(current);

        if (perforceEl.ValueKind == JsonValueKind.Null)
            return new PerforceConnectionConfig();

        if (perforceEl.ValueKind != JsonValueKind.Object)
            throw AppServerErrors.InvalidParams("'perforce' must be an object or null.");

        var result = ClonePerforce(current);
        if (TryGetCaseInsensitiveProperty(perforceEl, "port", out var portEl))
            result.Port = ParseOptionalString(portEl, "perforce.port");
        if (TryGetCaseInsensitiveProperty(perforceEl, "client", out var clientEl))
            result.Client = ParseOptionalString(clientEl, "perforce.client");
        if (TryGetCaseInsensitiveProperty(perforceEl, "user", out var userEl))
            result.User = ParseOptionalString(userEl, "perforce.user");
        if (TryGetCaseInsensitiveProperty(perforceEl, "charset", out var charsetEl))
            result.Charset = ParseOptionalString(charsetEl, "perforce.charset");
        if (TryGetCaseInsensitiveProperty(perforceEl, "p4ConfigName", out var p4ConfigNameEl))
            result.P4ConfigName = ParseOptionalString(p4ConfigNameEl, "perforce.p4ConfigName");
        if (TryGetCaseInsensitiveProperty(perforceEl, "p4ExecutablePath", out var p4ExecutablePathEl))
            result.P4ExecutablePath = ParseOptionalString(p4ExecutablePathEl, "perforce.p4ExecutablePath");
        if (TryGetCaseInsensitiveProperty(perforceEl, "timeoutSeconds", out var timeoutSecondsEl))
            result.TimeoutSeconds = ParseTimeoutSeconds(timeoutSecondsEl, "perforce.timeoutSeconds");
        if (TryGetCaseInsensitiveProperty(perforceEl, "online", out var onlineEl))
            result.Online = ParseOptionalBoolean(onlineEl, "perforce.online", defaultValue: true);
        if (TryGetCaseInsensitiveProperty(perforceEl, "autoOffline", out var autoOfflineEl))
            result.AutoOffline = ParseOptionalBoolean(autoOfflineEl, "perforce.autoOffline", defaultValue: true);

        return result;
    }

    private static PerforceConnectionConfig ClonePerforce(PerforceConnectionConfig? current) => new()
    {
        Port = current?.Port ?? string.Empty,
        Client = current?.Client ?? string.Empty,
        User = current?.User ?? string.Empty,
        Charset = current?.Charset ?? string.Empty,
        P4ConfigName = current?.P4ConfigName ?? string.Empty,
        P4ExecutablePath = current?.P4ExecutablePath ?? string.Empty,
        TimeoutSeconds = current?.TimeoutSeconds > 0 ? current.TimeoutSeconds : 30,
        Online = current?.Online ?? true,
        AutoOffline = current?.AutoOffline ?? true
    };

    private static string ParseOptionalString(JsonElement element, string fieldName) => element.ValueKind switch
    {
        JsonValueKind.Null => string.Empty,
        JsonValueKind.String => (element.GetString() ?? string.Empty).Trim(),
        _ => throw AppServerErrors.InvalidParams($"'{fieldName}' must be a string or null.")
    };

    private static int ParseTimeoutSeconds(JsonElement element, string fieldName)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return 30;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be an integer or null.");
        return value > 0 ? value : 30;
    }

    private static bool ParseOptionalBoolean(JsonElement element, string fieldName, bool defaultValue) => element.ValueKind switch
    {
        JsonValueKind.Null => defaultValue,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw AppServerErrors.InvalidParams($"'{fieldName}' must be a boolean or null.")
    };

    private static bool ParamsContainPassword(JsonElement paramsEl)
    {
        foreach (var prop in paramsEl.EnumerateObject())
        {
            if (IsPasswordKey(prop.Name))
                return true;
            if (prop.NameEquals("perforce") && prop.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var inner in prop.Value.EnumerateObject())
                    if (IsPasswordKey(inner.Name))
                        return true;
            }
        }
        return false;
    }

    private static bool IsPasswordKey(string name) =>
        name.Equals("password", StringComparison.OrdinalIgnoreCase)
        || name.Equals("passwd", StringComparison.OrdinalIgnoreCase)
        || name.Equals("p4passwd", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetCaseInsensitiveProperty(JsonElement obj, string expectedName, out JsonElement value)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
