namespace DotCraft.Oratorio.GitHub;

public sealed class GitHubOptions
{
    public string Endpoint { get; set; } = "https://api.github.com";
    public string? AppId { get; set; }
    public GitHubInstallationProfileOptions[] InstallationProfiles { get; set; } = [];
    public string? PrivateKey { get; set; }
    public string? PrivateKeyPath { get; set; }
    public string? WebhookSecret { get; set; }
    public string[] Repositories { get; set; } = [];
    public bool WritesEnabled { get; set; }

    public bool HasAppAuthentication =>
        !string.IsNullOrWhiteSpace(AppId) &&
        (!string.IsNullOrWhiteSpace(PrivateKey) || !string.IsNullOrWhiteSpace(PrivateKeyPath));

    public bool CanWrite => WritesEnabled && HasAppAuthentication;
}

public sealed class GitHubInstallationProfileOptions
{
    public string Instance { get; set; } = "github.com";
    public string Owner { get; set; } = "";
    public string InstallationId { get; set; } = "";
    public string Source { get; set; } = "manual";
}
