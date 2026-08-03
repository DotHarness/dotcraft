using ModelPreference = DotCraft.Configuration.ModelPreference;
using Xunit;
namespace DotCraft.Configuration;

internal static class AppConfigTestFactory
{
    public static AppConfig CreateOpenAI(
        string model = "gpt-4o-mini",
        string providerId = "openai",
        string apiKey = "sk-test-not-used-for-network",
        string endPoint = "https://127.0.0.1:9/v1")
    {
        return new AppConfig
        {
            ProviderId = providerId,
            ProviderPreferences = new Dictionary<string, ModelPreference>(StringComparer.OrdinalIgnoreCase)
            {
                [providerId] = new ModelPreference { Model = model }
            },
            Providers =
            {
                [providerId] = new AppConfig.ModelProviderConfig
                {
                    DisplayName = "OpenAI",
                    Protocol = ModelProviderProtocols.OpenAI,
                    ApiKey = apiKey,
                    EndPoint = endPoint
                }
            }
        };
    }

    public static AppConfig CreateAnthropic(
        string model = "claude-sonnet-4-5",
        string providerId = "anthropic",
        string apiKey = "sk-ant-test",
        string endPoint = "https://api.anthropic.com")
    {
        return new AppConfig
        {
            ProviderId = providerId,
            ProviderPreferences = new Dictionary<string, ModelPreference>(StringComparer.OrdinalIgnoreCase)
            {
                [providerId] = new ModelPreference { Model = model }
            },
            Providers =
            {
                [providerId] = new AppConfig.ModelProviderConfig
                {
                    DisplayName = "Anthropic",
                    Protocol = ModelProviderProtocols.Anthropic,
                    ApiKey = apiKey,
                    EndPoint = endPoint
                }
            }
        };
    }
}
