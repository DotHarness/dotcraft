namespace DotCraft.SourceControl;

/// <summary>
/// Resolves the effective source control provider and a derived (non-live) binding status from
/// configuration. Connectivity is never probed here — live Perforce status comes from a connection
/// test. There is no auto-detection: the effective provider is simply the configured one (a legacy
/// <c>auto</c> value resolves to <c>git</c>).
/// </summary>
public static class SourceControlResolver
{
    /// <summary>Returns the normalized configured provider.</summary>
    public static string ResolveEffectiveProvider(SourceControlConfig config, string? hostWorkspacePath = null)
    {
        _ = hostWorkspacePath;
        return SourceControlProviders.Normalize(config.Provider);
    }

    /// <summary>
    /// Derives the snapshot status from config. This is a hint, not a live probe:
    /// Perforce reports <c>offline</c> when disabled and <c>notTested</c> otherwise; providers
    /// without a server identity report <c>notTested</c> when bound and <c>notConfigured</c> when
    /// nothing is bound.
    /// </summary>
    public static string DeriveStatus(SourceControlConfig config, string effectiveProvider)
    {
        if (effectiveProvider == SourceControlProviders.Perforce)
            return config.Perforce.Online ? SourceControlStatuses.NotTested : SourceControlStatuses.Offline;

        if (effectiveProvider == SourceControlProviders.Git)
            return SourceControlStatuses.NotTested;

        return SourceControlStatuses.NotConfigured;
    }

    /// <summary>
    /// True when the workspace has a Perforce binding — either an explicit provider selection
    /// or any populated connection parameter.
    /// </summary>
    public static bool HasPerforceBinding(SourceControlConfig config)
    {
        if (SourceControlProviders.Normalize(config.Provider) == SourceControlProviders.Perforce)
            return true;

        var p = config.Perforce;
        return !string.IsNullOrWhiteSpace(p.Port)
            || !string.IsNullOrWhiteSpace(p.Client)
            || !string.IsNullOrWhiteSpace(p.User);
    }
}
