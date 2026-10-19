namespace DotCraft.Configuration;

public sealed partial class AppConfig
{
    [ConfigSection("Tools.Web", DisplayName = "Tools > Web", Order = 22)]
    public sealed class WebToolsConfig
    {
        [ConfigField(Min = 0)]
        public int MaxChars { get; set; } = 50000;

        [ConfigField(Min = 1, Hint = "seconds")]
        public int Timeout { get; set; } = 30;

        [ConfigField(Min = 1, Hint = "bytes")]
        public long MaxResponseBytes { get; set; } = 5_000_000;

        [ConfigField(Min = 1, Max = 10)]
        public int SearchMaxResults { get; set; } = 5;

        [ConfigField(FieldType = "select", Options = ["Auto", "Exa", "Bing", "DeepSeek"], Reload = ReloadBehavior.SubsystemRestart, HasReload = true, SubsystemKey = ConfigurationSubsystems.ThreadAgents)]
        public string SearchProvider { get; set; } = WebSearchProvider.Auto;

        [ConfigField(Sensitive = true, Hint = "Exa API key; empty uses EXA_API_KEY or the free rate-limited endpoint", Reload = ReloadBehavior.SubsystemRestart, HasReload = true, SubsystemKey = ConfigurationSubsystems.ThreadAgents)]
        public string ExaApiKey { get; set; } = string.Empty;

        [ConfigField(FieldType = "select", Options = ["Provider", "ApiKey"], Reload = ReloadBehavior.SubsystemRestart, HasReload = true, SubsystemKey = ConfigurationSubsystems.ThreadAgents)]
        public string DeepSeekCredentialSource { get; set; } = "Provider";

        [ConfigField(Hint = "Provider id; empty prefers the conversation provider, then the only eligible provider", Reload = ReloadBehavior.SubsystemRestart, HasReload = true, SubsystemKey = ConfigurationSubsystems.ThreadAgents)]
        public string DeepSeekProvider { get; set; } = string.Empty;

        [ConfigField(Sensitive = true, Hint = "Independent search key; empty uses DEEPSEEK_API_KEY", Reload = ReloadBehavior.SubsystemRestart, HasReload = true, SubsystemKey = ConfigurationSubsystems.ThreadAgents)]
        public string DeepSeekApiKey { get; set; } = string.Empty;
    }
}
