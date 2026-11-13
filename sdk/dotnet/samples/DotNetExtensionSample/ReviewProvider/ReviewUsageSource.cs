using DotCraft.Agents;
using DotCraft.Contributions;

namespace Acme.ReviewCore;

internal sealed class ReviewUsageSource : IProviderUsageSource
{
    private const string Gateway = "https://review.example/";

    public Task<ProviderUsageSnapshot?> ReadUsageAsync(ProviderUsageRequest request, CancellationToken cancellationToken)
    {
        if (!request.EndPoint.StartsWith(Gateway, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<ProviderUsageSnapshot?>(null);
        return Task.FromResult<ProviderUsageSnapshot?>(new ProviderUsageSnapshot(
            DateTimeOffset.UtcNow,
            new Dictionary<string, double> { ["usage"] = 1.5, ["limit"] = 10, ["limit_remaining"] = 8.5 },
            new Dictionary<string, string> { ["currency"] = "USD", ["scope"] = "apiKey" }));
    }
}
