using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Hooks;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

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
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.HooksList, HandleHooksListAsync);
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.HooksSetState, HandleHooksSetStateAsync);
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.HooksTrustPlugin, HandleHooksTrustPluginAsync);
    }

    private Task<AppServerTypedResult<Contract.HooksListResult>> HandleHooksListAsync(
        AppServerTypedRequest<Contract.HooksListParams> request,
        CancellationToken ct)
    {
        _ = request;
        _ = ct;
        EnsureAvailable();
        var discovery = RefreshHooks();
        return Task.FromResult(AppServerTypedResult<Contract.HooksListResult>.FromResult(ToListResult(discovery)));
    }

    private Task<AppServerTypedResult<Contract.HooksSetStateResult>> HandleHooksSetStateAsync(
        AppServerTypedRequest<Contract.HooksSetStateParams> request,
        CancellationToken ct)
    {
        _ = ct;
        EnsureAvailable();
        var p = request.Params;
        var key = p.Key.IsSet ? p.Key.Value : null;
        var trustedHash = p.TrustedHash.IsSet ? p.TrustedHash.Value : null;
        if (string.IsNullOrWhiteSpace(key))
            throw AppServerErrors.InvalidParams("'key' is required.");
        if (!p.Enabled.IsSet && string.IsNullOrWhiteSpace(trustedHash))
            throw AppServerErrors.InvalidParams("'enabled' or 'trustedHash' is required.");

        WriteHookState(p);
        appConfigMonitor?.NotifyChanged(
            DotCraft.Protocol.AppServer.AppServerMethodNames.HooksSetState,
            [ConfigChangeRegions.Hooks]);

        var discovery = RefreshHooks();
        return Task.FromResult(AppServerTypedResult<Contract.HooksSetStateResult>.FromResult(new Contract.HooksSetStateResult
        {
            Hooks = discovery.Hooks.Select(ToWire).ToList(),
            Warnings = discovery.Warnings,
            Errors = discovery.Errors.Select(ToWire).ToList()
        }));
    }

    private Task<AppServerTypedResult<Contract.HooksTrustPluginResult>> HandleHooksTrustPluginAsync(
        AppServerTypedRequest<Contract.HooksTrustPluginParams> request,
        CancellationToken ct)
    {
        _ = ct;
        EnsureAvailable();
        var pluginId = request.Params.PluginId.IsSet ? request.Params.PluginId.Value : null;
        if (string.IsNullOrWhiteSpace(pluginId))
            throw AppServerErrors.InvalidParams("'pluginId' is required.");

        var discovery = RefreshHooks();
        var pluginHooks = discovery.Hooks
            .Where(hook => string.Equals(hook.Source, HookSources.Plugin, StringComparison.Ordinal)
                           && string.Equals(hook.PluginId, pluginId, StringComparison.Ordinal))
            .ToList();
        if (pluginHooks.Count == 0)
            throw AppServerErrors.InvalidParams($"No hooks found for enabled plugin '{pluginId}'.");

        WritePluginTrustState(pluginHooks);
        appConfigMonitor?.NotifyChanged(
            DotCraft.Protocol.AppServer.AppServerMethodNames.HooksTrustPlugin,
            [ConfigChangeRegions.Hooks]);

        var refreshed = RefreshHooks();
        return Task.FromResult(AppServerTypedResult<Contract.HooksTrustPluginResult>.FromResult(new Contract.HooksTrustPluginResult
        {
            Hooks = refreshed.Hooks.Select(ToWire).ToList(),
            Warnings = refreshed.Warnings,
            Errors = refreshed.Errors.Select(ToWire).ToList()
        }));
    }

    private void EnsureAvailable()
    {
        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(DotCraft.Protocol.AppServer.AppServerMethodNames.HooksList);
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

    private void WriteHookState(Contract.HooksSetStateParams p)
    {
        WriteHookStates(stateObj =>
        {
            var hookStateObj = GetOrCreateState(stateObj, p.Key.Value!);

            if (p.Enabled.IsSet)
                hookStateObj["Enabled"] = p.Enabled.Value;
            if (p.TrustedHash.IsSet && !string.IsNullOrWhiteSpace(p.TrustedHash.Value))
                hookStateObj["TrustedHash"] = p.TrustedHash.Value;
        });
    }

    private void WritePluginTrustState(IEnumerable<HookMetadata> hooks)
    {
        WriteHookStates(stateObj =>
        {
            foreach (var hook in hooks)
            {
                var hookStateObj = GetOrCreateState(stateObj, hook.Key);
                hookStateObj["TrustedHash"] = hook.CurrentHash;
            }
        });
    }

    private void WriteHookStates(Action<JsonObject> mutateState)
    {
        var configPath = workspaceConfig.PersonalConfigPath;
        var root = WorkspaceConfigEditor.LoadObject(configPath);
        var stateObj = GetOrCreateHooksState(root);
        mutateState(stateObj);
        WorkspaceConfigEditor.WriteObject(configPath, root);
    }

    private static JsonObject GetOrCreateHooksState(JsonObject root)
    {
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

        return stateObj;
    }

    private static JsonObject GetOrCreateState(JsonObject stateObj, string hookKey)
    {
        var hookStateObj = stateObj[hookKey] as JsonObject;
        if (hookStateObj == null)
        {
            hookStateObj = new JsonObject();
            stateObj[hookKey] = hookStateObj;
        }

        return hookStateObj;
    }

    private static Contract.HooksListResult ToListResult(HookDiscoveryResult discovery) => new()
    {
        Hooks = discovery.Hooks.Select(ToWire).ToList(),
        Warnings = discovery.Warnings,
        Errors = discovery.Errors.Select(ToWire).ToList()
    };

    private static Contract.HookMetadata ToWire(HookMetadata hook) => new()
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

    private static Contract.HookErrorInfo ToWire(HookErrorInfo error) => new()
    {
        Path = error.Path,
        Message = error.Message
    };
}
