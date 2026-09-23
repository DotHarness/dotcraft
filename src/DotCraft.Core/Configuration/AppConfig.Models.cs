using System.Text.Json.Serialization;
using DotCraft.Agents;

namespace DotCraft.Configuration;

public sealed partial class AppConfig
{
    public ModelServiceConfig? ModelService { get; set; }

    public sealed class ModelServiceConfig
    {
        public string Endpoint { get; set; } = string.Empty;
        [ConfigField(Sensitive = true)]
        public string Token { get; set; } = string.Empty;
        [JsonIgnore]
        public string? CallerId { get; set; }
    }

    public sealed class ModelProviderConfig
    {
        [JsonIgnore]
        public ProviderAuthenticationStatus? RemoteAuthentication { get; set; }

        [JsonIgnore]
        public IReadOnlyDictionary<string, ProviderRequestAdaptation>? RemoteModels { get; set; }

        public string DisplayName { get; set; } = string.Empty;

        public string Protocol { get; set; } = ModelProviderProtocols.OpenAIChatCompletions;

        [ConfigField(Sensitive = true)]
        public string ApiKey { get; set; } = string.Empty;

        [ConfigField(Hint = "Authentication method: 'apiKey' or 'chatgptOAuth' (OpenAI subscription).")]
        public string AuthMethod { get; set; } = ModelProviderAuthMethods.ApiKey;

        [ConfigField(Hint = "Populated by Sign in with ChatGPT; do not edit manually.")]
        public string ChatGptAccountId { get; set; } = string.Empty;

        [ConfigField(Hint = "Populated by Sign in with ChatGPT; do not edit manually.")]
        public string ChatGptPlanType { get; set; } = string.Empty;

        /// <summary>Empty values use the protocol's official default endpoint.</summary>
        public string EndPoint { get; set; } = string.Empty;

        public int? NetworkTimeoutSeconds { get; set; }

        [ConfigField(Min = 1, Hint = "Default max output tokens for provider requests when the request does not override it.")]
        public int? MaxOutputTokens { get; set; }

        [ConfigField(Min = 0, Hint = "Maximum stream reconnection attempts. 0 disables stream retry.")]
        public int? StreamMaxRetries { get; set; }

        [ConfigField(Min = 1, Hint = "Streaming idle timeout in milliseconds.")]
        public int? StreamIdleTimeoutMs { get; set; }

        /// <summary>Defaults to enabled for official OpenAI Responses providers and ChatGPT OAuth.</summary>
        public bool? SupportsHostedImageGeneration { get; set; }

        public ModelProviderConfig Clone() => new()
        {
            RemoteAuthentication = RemoteAuthentication,
            RemoteModels = RemoteModels,
            DisplayName = DisplayName,
            Protocol = Protocol,
            ApiKey = ApiKey,
            AuthMethod = AuthMethod,
            ChatGptAccountId = ChatGptAccountId,
            ChatGptPlanType = ChatGptPlanType,
            EndPoint = EndPoint,
            NetworkTimeoutSeconds = NetworkTimeoutSeconds,
            MaxOutputTokens = MaxOutputTokens,
            StreamMaxRetries = StreamMaxRetries,
            StreamIdleTimeoutMs = StreamIdleTimeoutMs,
            SupportsHostedImageGeneration = SupportsHostedImageGeneration
        };
    }

}
