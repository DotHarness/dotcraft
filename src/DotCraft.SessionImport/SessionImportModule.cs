using DotCraft.Configuration;
using DotCraft.Modules;
using DotCraft.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DotCraft.SessionImport;

[DotCraftModule("agent-import", Priority = 57, Description = "Import setup and chat sessions from other coding agents")]
public sealed partial class SessionImportModule : ModuleBase
{
    private const string SectionKey = "AgentImport";

    public override bool IsEnabled(AppConfig config) => config.GetSection<SessionImportConfig>(SectionKey).Enabled;

    public override IReadOnlyList<string> ValidateConfig(AppConfig config)
    {
        var section = config.GetSection<SessionImportConfig>(SectionKey);
        if (!section.Enabled)
            return [];
        var errors = new List<string>();
        if (section.SyncInterval <= TimeSpan.Zero)
            errors.Add("AgentImport: SyncInterval must be positive.");
        if (section.MaxSessionAgeDays < 1)
            errors.Add("AgentImport: MaxSessionAgeDays must be at least 1.");
        if (section.MaxSessionsPerSource < 1)
            errors.Add("AgentImport: MaxSessionsPerSource must be at least 1.");
        return errors;
    }

    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        var section = context.Config.GetSection<SessionImportConfig>(SectionKey);
        services.TryAddSingleton<ImportedSetupRuntime>();
        services.AddSingleton<ISessionRuntimeRefresher>(sp => sp.GetRequiredService<ImportedSetupRuntime>());
        services.AddSingleton<ISessionServiceConsumer>(sp => sp.GetRequiredService<ImportedSetupRuntime>());
        foreach (var source in CurrentUserSources())
            services.AddSingleton(source);
        services.TryAddSingleton(_ => new SetupImportService(new SetupImportPaths(
            context.Paths.WorkspacePath, context.Paths.Data.RootPath,
            Path.GetDirectoryName(UserConfigPath(context.Config))!,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            new Dictionary<string, string>
            {
                ["claude-code"] = EnvironmentRoot("CLAUDE_CONFIG_DIR") ?? HomeRoot(".claude"),
                ["codex"] = EnvironmentRoot("CODEX_HOME") ?? HomeRoot(".codex"),
                ["cursor"] = HomeRoot(".cursor")
            })));
        services.TryAddSingleton(sp => new SessionImportService(
            new SessionImportServiceOptions(context.Paths.WorkspacePath, context.Paths.Data.RootPath, section),
            new SessionImportSettingsStore(UserConfigPath(context.Config), WorkspaceConfigPath(context)),
            sp.GetServices<ISessionImportSource>(),
            sp.GetService<ILogger<SessionImportService>>(), sp.GetRequiredService<SetupImportService>()));
        services.AddSingleton<ISessionServiceConsumer>(sp => sp.GetRequiredService<SessionImportService>());
        services.TryAddSingleton(sp => new SessionImportSyncRuntime(sp.GetRequiredService<SessionImportService>()));
    }

    internal static IReadOnlyList<ISessionImportSource> CurrentUserSources() =>
    [
        new ClaudeCodeSessionSource(EnvironmentRoot("CLAUDE_CONFIG_DIR") ?? HomeRoot(".claude")),
        new CodexSessionSource(EnvironmentRoot("CODEX_HOME") ?? HomeRoot(".codex")),
        new CursorSessionSource(HomeRoot(".cursor"))
    ];

    private static string? EnvironmentRoot(string variable) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value ? value.Trim() : null;

    private static string HomeRoot(string directory) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), directory);

    private static string UserConfigPath(AppConfig config) =>
        string.IsNullOrWhiteSpace(config.GlobalConfigPath)
            ? Path.Combine(HomeRoot(".craft"), "config.json")
            : Path.GetFullPath(config.GlobalConfigPath);

    private static string WorkspaceConfigPath(ModuleContext context) =>
        string.IsNullOrWhiteSpace(context.Config.WorkspaceConfigPath)
            ? Path.Combine(context.Paths.Data.RootPath, "config.json")
            : Path.GetFullPath(context.Config.WorkspaceConfigPath);
}
