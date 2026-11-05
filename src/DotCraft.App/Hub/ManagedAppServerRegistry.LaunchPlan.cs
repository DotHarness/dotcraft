using System.Security.Cryptography;
using DotCraft.AppServer;
using DotCraft.Configuration;
using Microsoft.AspNetCore.Http;

namespace DotCraft.Hub;

public sealed partial class ManagedAppServerRegistry
{
    private ServicePlan BuildServicePlan(
        string canonicalWorkspacePath,
        string craftPath,
        HubRuntimeToolsRequest? runtimeTools)
    {
        var configPath = Path.Combine(craftPath, "config.json");
        var config = AppConfig.LoadWithGlobalFallback(configPath);
        var endpoints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var status = new Dictionary<string, HubServiceStatus>(StringComparer.OrdinalIgnoreCase);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        var usedPorts = new HashSet<int>();

        var wsPort = AllocateUniquePort(usedPorts);
        var wsHost = "127.0.0.1";
        var wsToken = CreateToken();
        var wsProbeUrl = $"ws://{wsHost}:{wsPort}/ws";
        var wsResponseUrl = $"{wsProbeUrl}?token={Uri.EscapeDataString(wsToken)}";
        endpoints["appServerWebSocket"] = wsResponseUrl;
        status["appServerWebSocket"] = new HubServiceStatus("allocated", wsResponseUrl);

        environment[ManagedAppServerEnvironment.ManagedFlag] = "1";
        environment[ManagedAppServerEnvironment.HubApiBaseUrl] = _hubApiBaseUrl;
        environment[ManagedAppServerEnvironment.HubToken] = _hubToken;
        environment[ManagedAppServerEnvironment.WebSocketHost] = wsHost;
        environment[ManagedAppServerEnvironment.WebSocketPort] = wsPort.ToString();
        environment[ManagedAppServerEnvironment.WebSocketToken] = wsToken;

        _events.Publish("port.allocated", canonicalWorkspacePath, new { service = "appServerWebSocket", port = wsPort });

        if (config.DashBoard.Enabled && config.Tracing.Enabled)
        {
            var port = AllocateUniquePort(usedPorts);
            var url = $"http://127.0.0.1:{port}/dashboard";
            endpoints["dashboard"] = url;
            status["dashboard"] = new HubServiceStatus("allocated", url);
            environment[ManagedAppServerEnvironment.DashboardHost] = "127.0.0.1";
            environment[ManagedAppServerEnvironment.DashboardPort] = port.ToString();
            _events.Publish("port.allocated", canonicalWorkspacePath, new { service = "dashboard", port });
        }
        else
        {
            status["dashboard"] = new HubServiceStatus("disabled", Reason: "Dashboard or tracing is disabled.");
        }

        AddRuntimeTools(runtimeTools, environment, status);

        return new ServicePlan(
            environment,
            endpoints,
            status,
            NormalizeOptionalPath(runtimeTools?.NodeBin),
            runtimeTools?.NodeRunAsNode == true,
            NormalizeOptionalPath(runtimeTools?.ModulesDir),
            NormalizeOptionalValue(runtimeTools?.BuiltInPluginRoots),
            NormalizeOptionalValue(runtimeTools?.DefaultPluginRegistryUrl));
    }

    internal static void AddRuntimeTools(
        HubRuntimeToolsRequest? runtimeTools,
        Dictionary<string, string?> environment,
        Dictionary<string, HubServiceStatus>? status = null)
    {
        if (!string.IsNullOrWhiteSpace(runtimeTools?.RipgrepPath))
            environment["DOTCRAFT_RG_PATH"] = runtimeTools.RipgrepPath.Trim();

        var nodeBin = NormalizeOptionalPath(runtimeTools?.NodeBin);
        var modulesDir = NormalizeOptionalPath(runtimeTools?.ModulesDir);
        if (!string.IsNullOrWhiteSpace(nodeBin))
            environment["DOTCRAFT_NODE_BIN"] = nodeBin;
        if (runtimeTools?.NodeRunAsNode == true)
            environment["DOTCRAFT_NODE_RUN_AS_NODE"] = "1";
        if (!string.IsNullOrWhiteSpace(modulesDir))
            environment["DOTCRAFT_MODULES_DIR"] = modulesDir;
        if (!string.IsNullOrWhiteSpace(runtimeTools?.BuiltInPluginRoots))
            environment["DOTCRAFT_BUILTIN_PLUGIN_ROOTS"] = runtimeTools.BuiltInPluginRoots.Trim();
        if (!string.IsNullOrWhiteSpace(runtimeTools?.DefaultPluginRegistryUrl))
            environment["DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL"] = runtimeTools.DefaultPluginRegistryUrl.Trim();

        if (status != null)
        {
            status["typescriptRuntime"] =
                !string.IsNullOrWhiteSpace(nodeBin) && !string.IsNullOrWhiteSpace(modulesDir)
                    ? new HubServiceStatus("allocated", Reason: "TypeScript channel runtime hints are available.")
                    : new HubServiceStatus("unavailable", Reason: "TypeScript channel runtime is not configured. Launch DotCraft Desktop once or run 'dotcraft hub set-runtime'.");
        }
    }

}
