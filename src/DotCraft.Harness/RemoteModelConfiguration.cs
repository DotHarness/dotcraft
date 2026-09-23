using DotCraft.Agents.Remote;
using DotCraft.Configuration;

namespace DotCraft.Harness;

public static class RemoteModelConfiguration
{
    public static void Apply(AppConfig config, ModelServiceCatalog catalog)
    {
        if (config.ModelService is null)
            throw new InvalidOperationException("Configure the model service connection before applying its catalog.");
        config.ModelService.CallerId = catalog.CallerId;
        config.Providers = catalog.Providers.ToDictionary(provider => provider.Id, provider => new AppConfig.ModelProviderConfig
        {
            DisplayName = provider.DisplayName,
            Protocol = provider.Protocol,
            EndPoint = provider.Endpoint,
            AuthMethod = provider.AuthMethod,
            ChatGptAccountId = provider.Authentication.AccountId ?? "",
            ChatGptPlanType = provider.Authentication.PlanType ?? "",
            SupportsHostedImageGeneration = provider.SupportsHostedImageGeneration,
            MaxOutputTokens = provider.MaxOutputTokens,
            NetworkTimeoutSeconds = provider.NetworkTimeoutSeconds,
            RemoteAuthentication = provider.Authentication,
            RemoteModels = provider.Models
        }, StringComparer.OrdinalIgnoreCase);
    }
}
