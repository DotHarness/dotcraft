namespace DotCraft.Agents;

public sealed partial class OpenRouterProvider
{
    public async Task<IReadOnlyList<ProviderRoutingOption>> FetchRoutingProvidersAsync(CancellationToken cancellationToken)
    {
        var response = OpenRouterJson.Read<OpenRouterData<OpenRouterSupplier[]>>(
            await GetAsync("providers", ApiKey(), cancellationToken).ConfigureAwait(false));
        return response.Data.Select(item => new ProviderRoutingOption(item.Slug, item.Name))
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
