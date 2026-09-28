using DotCraft.Configuration;
using DotCraft.Hooks;
using DotCraft.Protocol.AppServer;

namespace DotCraft.SessionImport;

public sealed partial class SessionImportService
{
    public IReadOnlyList<ImportOutcome> ReadAttention(IReadOnlySet<string> unavailableMcpServers)
    {
        var config = AppConfig.LoadWithGlobalFallback(Path.Combine(_settingsStore.WorkspaceDataPath, "config.json"),
            Path.Combine(_settingsStore.UserDataPath, "config.json"));
        var hooks = new HooksLoader(_settingsStore.WorkspaceDataPath, Path.Combine(_settingsStore.UserDataPath, "hooks.json"))
            .Discover(config, _options.WorkspacePath).Hooks;
        return _history.Read().SelectMany(batch => batch.Outcomes)
            .Where(o => o.Status is "imported" or "attention")
            .DistinctBy(o => (o.Scope, o.Category, o.TargetPath, o.SourceId))
            .Where(o => o.Category switch
            {
                "hooks" => hooks.Any(h => h.SourcePath == o.TargetPath && h.TrustStatus is "untrusted" or "modified"),
                "plugins" => hooks.Any(h => h.PluginId == Path.GetFileName(o.TargetPath) && h.TrustStatus is "untrusted" or "modified")
                    || unavailableMcpServers.Any(name => name.StartsWith("plugin\n" + Path.GetFileName(o.TargetPath) + ":", StringComparison.Ordinal)),
                "mcp" => o.Title.IsSet && (unavailableMcpServers.Contains(o.Scope + "\n" + o.Title.Value!) || NeedsMcpEnvironment(o)),
                _ => false
            }).ToArray();
    }

    private static bool NeedsMcpEnvironment(ImportOutcome outcome)
    {
        var server = DotCraft.Mcp.McpScopeStore.Read(outcome.TargetPath, outcome.Scope)
            .FirstOrDefault(s => string.Equals(s.Name, outcome.Title.Value, StringComparison.OrdinalIgnoreCase));
        if (server is not { Enabled: true }) return false;
        var variables = server.EnvVars.Concat(server.EnvHttpHeaders.Values);
        if (server.BearerTokenEnvVar != null) variables = variables.Append(server.BearerTokenEnvVar);
        return variables.Any(name => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)));
    }
}
