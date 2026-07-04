using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Hooks;

namespace DotCraft.Protocol.AppServer;

internal sealed class HookRequestHandler(
    HookRunner? hookRunner,
    IAppConfigMonitor? appConfigMonitor,
    string? workspaceCraftPath,
    string? hostWorkspacePath,
    WorkspaceConfigEditor workspaceConfig,
    AppServerRuntimeConfigRefresher runtimeConfig,
    IReadOnlyList<string>? builtInPluginSourceRoots) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(AppServerMethods.HooksList, HandleHooksListAsync);
        table.Map(AppServerMethods.HooksSetState, HandleHooksSetStateAsync);
    }

    private Task<object?> HandleHooksListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = msg;
        _ = ct;
        EnsureAvailable();
        var discovery = RefreshHooks();
        return Task.FromResult<object?>(ToListResult(discovery));
    }

    private Task<object?> HandleHooksSetStateAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureAvailable();
        var p = AppServerParams.Get<HooksSetStateParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Key))
            throw AppServerErrors.InvalidParams("'key' is required.");
        if (!p.Enabled.HasValue && string.IsNullOrWhiteSpace(p.TrustedHash))
            throw AppServerErrors.InvalidParams("'enabled' or 'trustedHash' is required.");

        WriteHookState(p);
        appConfigMonitor?.NotifyChanged(
            AppServerMethods.HooksSetState,
            [ConfigChangeRegions.Hooks]);

        var discovery = RefreshHooks();
        return Task.FromResult<object?>(new HooksSetStateResult
        {
            Hooks = discovery.Hooks.Select(ToWire).ToList(),
            Warnings = discovery.Warnings,
            Errors = discovery.Errors.Select(ToWire).ToList()
        });
    }

    private void EnsureAvailable()
    {
        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(AppServerMethods.HooksList);
    }

    private HookDiscoveryResult RefreshHooks() =>
        AppServerHookRuntimeRefresher.Refresh(
            hookRunner,
            appConfigMonitor,
            workspaceCraftPath,
            hostWorkspacePath,
            workspaceConfig,
            runtimeConfig,
            builtInPluginSourceRoots);

    private void WriteHookState(HooksSetStateParams p)
    {
        var configPath = workspaceConfig.PersonalConfigPath;
        var root = WorkspaceConfigEditor.LoadObject(configPath);
        var hooksKey = WorkspaceConfigEditor.FindCaseInsensitiveKey(root, "Hooks") ?? "Hooks";
        var hooksObj = root[hooksKey] as JsonObject;
        if (hooksObj == null)
        {
            hooksObj = new JsonObject();
            root[hooksKey] = hooksObj;
        }

        var stateKey = WorkspaceConfigEditor.FindCaseInsensitiveKey(hooksObj, "State") ?? "State";
        var stateObj = hooksObj[stateKey] as JsonObject;
        if (stateObj == null)
        {
            stateObj = new JsonObject();
            hooksObj[stateKey] = stateObj;
        }

        var hookStateObj = stateObj[p.Key] as JsonObject;
        if (hookStateObj == null)
        {
            hookStateObj = new JsonObject();
            stateObj[p.Key] = hookStateObj;
        }

        if (p.Enabled.HasValue)
            hookStateObj["Enabled"] = p.Enabled.Value;
        if (!string.IsNullOrWhiteSpace(p.TrustedHash))
            hookStateObj["TrustedHash"] = p.TrustedHash;

        WorkspaceConfigEditor.WriteObject(configPath, root);
    }

    private static HooksListResult ToListResult(HookDiscoveryResult discovery) => new()
    {
        Hooks = discovery.Hooks.Select(ToWire).ToList(),
        Warnings = discovery.Warnings,
        Errors = discovery.Errors.Select(ToWire).ToList()
    };

    private static HookMetadataWire ToWire(HookMetadata hook) => new()
    {
        Key = hook.Key,
        EventName = hook.EventName,
        HandlerType = hook.HandlerType,
        Matcher = hook.Matcher,
        Condition = hook.Condition,
        Command = hook.Command,
        TimeoutSec = hook.TimeoutSec,
        ExecutionMode = hook.ExecutionMode,
        AsyncRewake = hook.AsyncRewake,
        RewakeMessage = hook.RewakeMessage,
        RewakeSummary = hook.RewakeSummary,
        Shell = hook.Shell,
        Once = hook.Once,
        StatusMessage = hook.StatusMessage,
        SourcePath = hook.SourcePath,
        Source = hook.Source,
        PluginId = hook.PluginId,
        DisplayOrder = hook.DisplayOrder,
        Enabled = hook.Enabled,
        IsManaged = hook.IsManaged,
        CurrentHash = hook.CurrentHash,
        TrustStatus = hook.TrustStatus
    };

    private static HookErrorInfoWire ToWire(HookErrorInfo error) => new()
    {
        Path = error.Path,
        Message = error.Message
    };
}
