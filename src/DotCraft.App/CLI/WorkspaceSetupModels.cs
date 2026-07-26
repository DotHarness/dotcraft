using DotCraft.Configuration;

namespace DotCraft.CLI;

public enum WorkspaceBootstrapProfile
{
    Default,
    Developer,
    PersonalAssistant
}

/// <summary>
/// Provider setup strategy used by non-interactive workspace setup.
/// </summary>
public enum WorkspaceSetupProviderMode
{
    Legacy,
    Existing,
    Create,
    Skip
}

/// <summary>
/// Provider definition to create in personal config during workspace setup.
/// </summary>
public sealed record WorkspaceSetupProviderDraft
{
    public required string Id { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public required string Protocol { get; init; }

    public string ApiKey { get; init; } = string.Empty;

    public string EndPoint { get; init; } = string.Empty;

    public int? NetworkTimeoutSeconds { get; init; }

    /// <summary>
    /// Authentication mechanism: "apiKey" (default) or "chatgptOAuth".
    /// In chatgptOAuth mode the wizard records the preference; the user signs in via
    /// Settings → Providers after the workspace launches.
    /// </summary>
    public string AuthMethod { get; init; } = "apiKey";
}

public sealed record WorkspaceSetupRequest
{
    public required string Model { get; init; }

    /// <summary>Complete MainAgent preference selected by setup.</summary>
    public ModelPreference? Preference { get; init; }

    public required string EndPoint { get; init; }

    public required string ApiKey { get; init; }

    public WorkspaceBootstrapProfile Profile { get; init; } = WorkspaceBootstrapProfile.Default;

    public bool SaveToUserConfig { get; init; }

    public bool PreferExistingUserConfig { get; init; }

    public WorkspaceSetupProviderMode ProviderMode { get; init; } = WorkspaceSetupProviderMode.Legacy;

    public string ProviderId { get; init; } = string.Empty;

    public WorkspaceSetupProviderDraft? Provider { get; init; }

    public bool SetAsUserDefault { get; init; }
}
