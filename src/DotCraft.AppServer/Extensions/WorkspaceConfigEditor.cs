using DotCraft.Configuration;

namespace DotCraft.AppServer;

internal sealed class WorkspaceConfigEditor(IAppConfigMonitor? appConfigMonitor, string? workspaceCraftPath)
{
    public string? EffectiveGlobalConfigPath => appConfigMonitor?.Current.GlobalConfigPath;

    public string? PersonalConfigPath =>
        !string.IsNullOrWhiteSpace(appConfigMonitor?.Current.GlobalConfigPath)
            ? appConfigMonitor.Current.GlobalConfigPath!
            : null;

    public string? UserDataPath =>
        PersonalConfigPath is { } path ? Path.GetDirectoryName(Path.GetFullPath(path)) : null;

    public string RequirePersonalConfigPath(string operation) =>
        PersonalConfigPath ?? throw new InvalidOperationException(
            $"UserDataPath is required for {operation}.");

    public AppConfig LoadCurrentMergedConfig()
    {
        if (!string.IsNullOrWhiteSpace(workspaceCraftPath))
        {
            var loaded = AppConfig.LoadWithGlobalFallback(Path.Combine(workspaceCraftPath, "config.json"), EffectiveGlobalConfigPath);
            if (appConfigMonitor?.Current.ModelService is { } connection)
            {
                loaded.ModelService = connection;
                loaded.Providers = appConfigMonitor.Current.Providers;
            }
            return loaded;
        }

        return appConfigMonitor?.Current ?? new AppConfig();
    }
}
