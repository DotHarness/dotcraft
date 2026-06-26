using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.SourceControl;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Handles workspace source control binding methods (spec Section 25A): reading the
/// effective binding snapshot and persisting non-sensitive provider/connection config.
/// Connection testing (<c>sourceControl/test</c>) is registered separately.
/// </summary>
internal sealed class SourceControlRequestHandler(
    IAppConfigMonitor? appConfigMonitor,
    string? workspaceCraftPath,
    string? hostWorkspacePath) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(AppServerMethods.SourceControlGet, HandleGetAsync);
        table.Map(AppServerMethods.SourceControlUpdate, HandleUpdateAsync);
        table.Map(AppServerMethods.SourceControlTest, HandleTestAsync);
    }

    private Task<object?> HandleGetAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureManagementAvailable();
        _ = AppServerParams.Get<SourceControlGetParams>(msg);

        var config = appConfigMonitor?.Current.SourceControl ?? new SourceControlConfig();
        return Task.FromResult<object?>(BuildSnapshot(config));
    }

    private Task<object?> HandleUpdateAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureManagementAvailable();

        if (!msg.Params.HasValue || msg.Params.Value.ValueKind != JsonValueKind.Object)
            throw AppServerErrors.InvalidParams("'provider' is required.");

        // Defense in depth: passwords are never persisted to workspace config.
        if (ParamsContainPassword(msg.Params.Value))
            throw AppServerErrors.InvalidParams(
                "Passwords are not accepted by sourceControl/update and are never persisted. Use sourceControl/test for a transient login.");

        var p = AppServerParams.Get<SourceControlUpdateParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Provider))
            throw AppServerErrors.InvalidParams("'provider' is required.");

        var current = appConfigMonitor?.Current.SourceControl ?? new SourceControlConfig();
        var updated = ApplyUpdate(current, p);

        Persist(updated);

        if (appConfigMonitor != null)
        {
            appConfigMonitor.Current.SourceControl = updated;
            appConfigMonitor.NotifyChanged(AppServerMethods.SourceControlUpdate, [ConfigChangeRegions.SourceControl]);
        }

        return Task.FromResult<object?>(BuildSnapshot(updated));
    }

    private async Task<object?> HandleTestAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        EnsureManagementAvailable();

        var p = AppServerParams.Get<SourceControlTestParams>(msg);
        if (!string.Equals(p.Provider, SourceControlProviders.Perforce, StringComparison.OrdinalIgnoreCase))
            throw AppServerErrors.InvalidParams("sourceControl/test currently supports only provider 'perforce'.");

        var perforce = p.Perforce ?? new PerforceConnectionWire();
        var mode = SourceControlConnectionModes.Normalize(p.ConnectionMode ?? SourceControlConnectionModes.P4Config);
        var workspacePath = ResolveHostWorkspacePath();
        var timeoutSeconds = perforce.TimeoutSeconds > 0 ? perforce.TimeoutSeconds : 30;
        var executable = string.IsNullOrWhiteSpace(perforce.P4ExecutablePath) ? "p4" : perforce.P4ExecutablePath.Trim();

        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        if (mode == SourceControlConnectionModes.P4Config && !string.IsNullOrWhiteSpace(perforce.P4ConfigName))
            env["P4CONFIG"] = perforce.P4ConfigName.Trim();

        var runner = new DefaultPerforceCommandRunner(executable, workspacePath, TimeSpan.FromSeconds(timeoutSeconds), env);
        var request = new PerforceTestRequest
        {
            WorkspacePath = workspacePath,
            ConnectionMode = mode,
            Port = perforce.Port ?? string.Empty,
            Client = perforce.Client ?? string.Empty,
            User = perforce.User ?? string.Empty,
            Charset = perforce.Charset ?? string.Empty,
            P4ConfigName = perforce.P4ConfigName ?? string.Empty,
            TimeoutSeconds = timeoutSeconds,
            // Transient: used only for a one-shot login, never persisted or logged.
            Password = p.Password
        };

        var report = await PerforceConnectionTester.TestAsync(runner, request, ct);
        return ToWire(report);
    }

    private static SourceControlTestResult ToWire(PerforceConnectionReport r) => new()
    {
        Status = r.Status,
        Code = r.Code,
        Summary = r.Summary,
        FallbackText = r.FallbackText,
        Identity = new SourceControlTestIdentity
        {
            ServerAddress = r.Identity.ServerAddress,
            User = r.Identity.User,
            Client = r.Identity.Client,
            Charset = r.Identity.Charset,
            ConnectionMode = r.Identity.ConnectionMode
        },
        Workspace = new SourceControlTestWorkspace
        {
            WorkspacePath = r.Workspace.WorkspacePath,
            ClientRoot = r.Workspace.ClientRoot,
            AltRoots = r.Workspace.AltRoots?.ToList() ?? [],
            MappingOk = r.Workspace.MappingOk
        },
        Authentication = new SourceControlTestAuthentication
        {
            TicketStatus = r.Authentication.TicketStatus,
            LoginRequired = r.Authentication.LoginRequired,
            ExpiresMessage = r.Authentication.ExpiresMessage
        },
        Diagnostics = new SourceControlTestDiagnostics
        {
            P4Version = r.Diagnostics.P4Version,
            TimeoutSeconds = r.Diagnostics.TimeoutSeconds,
            WarningCount = r.Warnings.Count,
            ErrorCode = r.Diagnostics.ErrorCode
        },
        Warnings = r.Warnings.Select(w => new SourceControlDiagnosticItem { Code = w.Code, FallbackText = w.FallbackText }).ToList(),
        Errors = r.Errors.Select(e => new SourceControlDiagnosticItem { Code = e.Code, FallbackText = e.FallbackText }).ToList()
    };

    private void EnsureManagementAvailable()
    {
        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(AppServerMethods.SourceControlGet);
    }

    /// <summary>Builds the wire snapshot from config, resolving the effective provider and status.</summary>
    private SourceControlSnapshot BuildSnapshot(SourceControlConfig config)
    {
        var workspacePath = ResolveHostWorkspacePath();
        var effective = SourceControlResolver.ResolveEffectiveProvider(config, workspacePath);
        var status = SourceControlResolver.DeriveStatus(config, effective);
        var includePerforce =
            effective == SourceControlProviders.Perforce
            || SourceControlProviders.Normalize(config.Provider) == SourceControlProviders.Perforce
            || SourceControlResolver.HasPerforceBinding(config);

        return new SourceControlSnapshot
        {
            Provider = SourceControlProviders.Normalize(config.Provider),
            EffectiveProvider = effective,
            ConnectionMode = SourceControlConnectionModes.Normalize(config.ConnectionMode),
            Status = status,
            WorkspacePath = workspacePath,
            Perforce = includePerforce ? ToWire(config.Perforce) : null,
            Capabilities = new SourceControlCapabilitiesWire
            {
                GitCommit = effective == SourceControlProviders.Git,
                PerforceBinding = effective == SourceControlProviders.Perforce,
                // Workflow capabilities are not available in this version.
                PerforceChangelist = false,
                PerforceShelve = false,
                PerforceSubmit = false
            }
        };
    }

    /// <summary>Merges update params into the current config. A provided Perforce object replaces the stored one.</summary>
    private static SourceControlConfig ApplyUpdate(SourceControlConfig current, SourceControlUpdateParams p)
    {
        return new SourceControlConfig
        {
            Provider = SourceControlProviders.Normalize(p.Provider),
            ConnectionMode = p.ConnectionMode != null
                ? SourceControlConnectionModes.Normalize(p.ConnectionMode)
                : SourceControlConnectionModes.Normalize(current.ConnectionMode),
            Perforce = p.Perforce != null ? FromWire(p.Perforce) : current.Perforce
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

    private static PerforceConnectionWire ToWire(PerforceConnectionConfig c) => new()
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

    private static PerforceConnectionConfig FromWire(PerforceConnectionWire w) => new()
    {
        Port = (w.Port ?? string.Empty).Trim(),
        Client = (w.Client ?? string.Empty).Trim(),
        User = (w.User ?? string.Empty).Trim(),
        Charset = (w.Charset ?? string.Empty).Trim(),
        P4ConfigName = (w.P4ConfigName ?? string.Empty).Trim(),
        P4ExecutablePath = (w.P4ExecutablePath ?? string.Empty).Trim(),
        TimeoutSeconds = w.TimeoutSeconds > 0 ? w.TimeoutSeconds : 30,
        Online = w.Online,
        AutoOffline = w.AutoOffline
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
}
