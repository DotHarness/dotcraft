namespace DotCraft.Agents;

public sealed record ProviderRoutingOption(string Id, string DisplayName);

public interface IProviderRoutingCatalog
{
    Task<IReadOnlyList<ProviderRoutingOption>> FetchRoutingProvidersAsync(CancellationToken cancellationToken);
}
