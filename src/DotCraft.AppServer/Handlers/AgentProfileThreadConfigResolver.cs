using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Sessions;
using Microsoft.Extensions.Logging;

namespace DotCraft.AppServer;

internal sealed class AgentProfileThreadConfigResolver(
    WorkspaceConfigEditor workspaceConfig,
    IAppConfigMonitor? appConfigMonitor,
    string? workspaceCraftPath,
    ILogger? logger)
{
    public ThreadConfiguration? Resolve(ThreadConfiguration? config, AppServerIncomingMessage msg)
    {
        if (string.IsNullOrWhiteSpace(config?.AgentProfileId))
            return config;

        try
        {
            var store = new AgentProfileStore(
                string.IsNullOrWhiteSpace(workspaceCraftPath) ? null : workspaceCraftPath,
                workspaceConfig.UserDataPath);
            var currentConfig = appConfigMonitor?.Current ?? workspaceConfig.LoadCurrentMergedConfig();
            var resolved = store.ResolveThreadStartConfiguration(config, currentConfig, TryGetConfigElement(msg));
            AppServerRuntimeRequestValidator.NormalizeCompleteModelConfiguration(currentConfig, resolved);
            return resolved;
        }
        catch (AgentProfileException ex)
        {
            if (ex.Kind == AgentProfileErrorKind.ValidationFailed)
            {
                logger?.LogWarning(
                    "Agent Profile rejected thread start. ProfileId={ProfileId} RequestedSource={RequestedSource} DiagnosticCodes={DiagnosticCodes}",
                    config?.AgentProfileId,
                    config?.AgentProfileSource,
                    string.Join(",", ex.Diagnostics.Select(diagnostic => diagnostic.Code).Distinct(StringComparer.Ordinal)));
            }
            throw AgentProfileRequestHandler.MapError(ex);
        }
    }

    private static JsonElement? TryGetConfigElement(AppServerIncomingMessage msg)
    {
        if (!msg.Params.HasValue || msg.Params.Value.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var property in msg.Params.Value.EnumerateObject())
        {
            if (string.Equals(property.Name, "config", StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }

        return null;
    }
}
