using DotCraft.Agents;

namespace DotCraft.Contributions;

/// <summary>Reports account usage for a built-in API-key provider that has no model backend.</summary>
public interface IProviderUsageSource : IContributionContract
{
    /// <summary>Returns the provider's usage, or <see langword="null"/> to decline so later sources are asked.</summary>
    Task<ProviderUsageSnapshot?> ReadUsageAsync(ProviderUsageRequest request, CancellationToken cancellationToken);
}

public sealed record ProviderUsageRequest(string ProviderId, string Protocol, string EndPoint, string ApiKey);
