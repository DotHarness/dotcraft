namespace DotCraft.Configuration;

/// <summary>
/// Provides a process-local app configuration snapshot and change notifications.
/// </summary>
public interface IAppConfigMonitor
{
    /// <summary>
    /// Gets the current app configuration snapshot.
    /// </summary>
    AppConfig Current { get; }

    /// <summary>
    /// Raised after workspace configuration has been successfully mutated.
    /// </summary>
    event EventHandler<AppConfigChangedEventArgs> Changed;

    /// <summary>
    /// Emits a coarse-grained configuration change event.
    /// </summary>
    /// <param name="source">RPC source method that caused the change.</param>
    /// <param name="regions">Logical regions touched by the change.</param>
    void NotifyChanged(string source, IReadOnlyList<string> regions);
}

/// <summary>
/// Event payload for app configuration changes.
/// </summary>
public sealed class AppConfigChangedEventArgs : EventArgs
{
    public required string Source { get; init; }

    public required IReadOnlyList<string> Regions { get; init; }

    public required DateTimeOffset ChangedAt { get; init; }
}

/// <summary>
/// Region tags for <see cref="IAppConfigMonitor"/> notifications.
/// </summary>
public static class ConfigChangeRegions
{
    public const string WorkspaceProviderPreferences = "workspace.providerPreferences";
    public const string WorkspaceProvider = "workspace.provider";
    public const string ProviderRegistry = "providers";
    public const string WelcomeSuggestions = "welcomeSuggestions";
    public const string Memory = "memory";
    public const string Skills = "skills";
    public const string Plugins = "plugins";
    public const string WorkspaceDefaultApprovalPolicy = "workspace.defaultApprovalPolicy";
    public const string Mcp = "mcp";
    public const string Lsp = "lsp";
    public const string Hooks = "hooks";
    public const string ExternalChannel = "externalChannel";
    public const string SubAgent = "subagent";
    public const string SourceControl = "sourceControl";
}

/// <summary>
/// Default app configuration monitor implementation.
/// </summary>
public sealed class AppConfigMonitor(AppConfig current) : IAppConfigMonitor
{
    /// <inheritdoc />
    public AppConfig Current { get; } = current;

    /// <inheritdoc />
    public event EventHandler<AppConfigChangedEventArgs>? Changed;

    /// <inheritdoc />
    public void NotifyChanged(string source, IReadOnlyList<string> regions)
    {
        if (string.IsNullOrWhiteSpace(source))
            return;

        var normalizedRegions = regions.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.Ordinal).ToArray();
        var args = new AppConfigChangedEventArgs
        {
            Source = source,
            Regions = normalizedRegions,
            ChangedAt = DateTimeOffset.UtcNow
        };

        var handlers = Changed?.GetInvocationList();
        if (handlers == null)
            return;

        foreach (var handler in handlers.Cast<EventHandler<AppConfigChangedEventArgs>>())
        {
            try
            {
                handler(this, args);
            }
            catch
            {
                // Notification fan-out is best-effort and must not fail callers.
            }
        }
    }
}
