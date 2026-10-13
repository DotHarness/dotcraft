using DotCraft.Configuration;

namespace DotCraft.Mcp;

public sealed class McpOAuthConfig
{
    public string? ClientId { get; set; }
    [ConfigField(Sensitive = true)]
    public string? ClientSecret { get; set; }
    public string? ClientSecretEnvVar { get; set; }
    public string? CallbackUrl { get; set; }
    public int? CallbackPort { get; set; }

    public McpOAuthConfig Clone() => (McpOAuthConfig)MemberwiseClone();
}
