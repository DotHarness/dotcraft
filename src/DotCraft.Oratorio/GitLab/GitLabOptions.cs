namespace DotCraft.Oratorio.GitLab;

public sealed class GitLabOptions
{
    public bool Enabled { get; set; }
    public bool WritesEnabled { get; set; }
    public string Endpoint { get; set; } = "https://gitlab.com";
    public string[] Projects { get; set; } = [];
    public GitLabProjectProfileOptions[] ProjectProfiles { get; set; } = [];
    public bool AllowLocalDevelopmentUnsafeWebhooks { get; set; }
    public int WebhookSigningToleranceSeconds { get; set; } = 300;

    public string EffectiveEndpoint =>
        string.IsNullOrWhiteSpace(Endpoint) ? "https://gitlab.com" : Endpoint.TrimEnd('/');

    public string EffectiveApiBaseUrl =>
        $"{EffectiveEndpoint}/api/v4";
}

public sealed class GitLabProjectProfileOptions
{
    public string Instance { get; set; } = "gitlab.com";
    public string ProjectPath { get; set; } = "";
    public string TokenKind { get; set; } = "accessToken";
    public string? Token { get; set; }
    public string? WebhookSecret { get; set; }
    public string? WebhookSigningToken { get; set; }
}
