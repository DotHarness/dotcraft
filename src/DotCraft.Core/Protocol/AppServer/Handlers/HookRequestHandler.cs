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
        table.Map(AppServerMethods.HooksTrustPlugin, HandleHooksTrustPluginAsync);
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

    private Task<object?> HandleHooksTrustPluginAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureAvailable();
        var p = AppServerParams.Get<HooksTrustPluginParams>(msg);
        if (string.IsNullOrWhiteSpace(p.PluginId))
            throw AppServerErrors.InvalidParams("'pluginId' is required.");

        var discovery = RefreshHooks();
        var pluginHooks = discovery.Hooks
            .Where(hook => string.Equals(hook.Source, HookSources.Plugin, StringComparison.Ordinal)
                           && string.Equals(hook.PluginId, p.PluginId, StringComparison.Ordinal))
            .ToList();
        if (pluginHooks.Count == 0)
            throw AppServerErrors.InvalidParams($"No hooks found for enabled plugin '{p.PluginId}'.");

        WritePluginTrustState(pluginHooks);
        appConfigMonitor?.NotifyChanged(
            AppServerMethods.HooksTrustPlugin,
            [ConfigChangeRegions.Hooks]);

        var refreshed = RefreshHooks();
        return Task.FromResult<object?>(new HooksTrustPluginResult
        {
            Hooks = refreshed.Hooks.Select(ToWire).ToList(),
            Warnings = refreshed.Warnings,
            Errors = refreshed.Errors.Select(ToWire).ToList()
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
        WriteHookStates(stateObj =>
        {
            var hookStateObj = GetOrCreateState(stateObj, p.Key);

            if (p.Enabled.HasValue)
                hookStateObj["Enabled"] = p.Enabled.Value;
            if (!string.IsNullOrWhiteSpace(p.TrustedHash))
                hookStateObj["TrustedHash"] = p.TrustedHash;
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
