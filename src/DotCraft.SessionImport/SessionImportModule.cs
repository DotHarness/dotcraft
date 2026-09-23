using DotCraft.Configuration;
using DotCraft.Modules;
using DotCraft.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DotCraft.SessionImport;

[DotCraftModule("session-import", Priority = 57, Description = "Import chat sessions from other coding agents")]
public sealed partial class SessionImportModule : ModuleBase
{
    private const string SectionKey = "SessionImport";

    public override bool IsEnabled(AppConfig config) => config.GetSection<SessionImportConfig>(SectionKey).Enabled;

    public override IReadOnlyList<string> ValidateConfig(AppConfig config)
    {
        var section = config.GetSection<SessionImportConfig>(SectionKey);
        if (!section.Enabled)
            return [];
        var errors = new List<string>();
        if (section.SyncInterval <= TimeSpan.Zero)
            errors.Add("SessionImport: SyncInterval must be positive.");
        if (section.MaxSessionAgeDays < 1)
            errors.Add("SessionImport: MaxSessionAgeDays must be at least 1.");
        if (section.MaxSessionsPerSource < 1)
            errors.Add("SessionImport: MaxSessionsPerSource must be at least 1.");
        return errors;
    }

    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        var section = context.Config.GetSection<SessionImportConfig>(SectionKey);
        services.AddSingleton<ISessionImportSource>(_ => new ClaudeCodeSessionSource(
            EnvironmentRoot("CLAUDE_CONFIG_DIR") ?? HomeRoot(".claude")));
        services.AddSingleton<ISessionImportSource>(_ => new CodexSessionSource(
            EnvironmentRoot("CODEX_HOME") ?? HomeRoot(".codex")));
        services.AddSingleton<ISessionImportSource>(_ => new CursorSessionSource(HomeRoot(".cursor")));
        services.TryAddSingleton(sp => new SessionImportService(
            new SessionImportServiceOptions(context.Paths.WorkspacePath, context.Paths.Data.RootPath, section),
            new SessionImportSettingsStore(UserConfigPath(context.Config), WorkspaceConfigPath(context)),
            sp.GetServices<ISessionImportSource>(),
            sp.GetService<ILogger<SessionImportService>>()));
        services.AddSingleton<ISessionServiceConsumer>(sp => sp.GetRequiredService<SessionImportService>());
        services.TryAddSingleton(sp => new SessionImportSyncRuntime(sp.GetRequiredService<SessionImportService>()));
    }

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
