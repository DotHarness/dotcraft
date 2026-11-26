using DotCraft.Contributions;

namespace DotCraft.Agents;

public interface IRetirableModelProvider
{
    /// <summary>Closes admission synchronously and releases the provider after admitted calls drain.</summary>
    void Retire();
}

internal sealed class ModelProviderCacheEntry
{
    private readonly Lazy<IModelProvider> _provider;
    private IRetirableModelProvider? _retirable;
    private int _retired;

    public ModelProviderCacheEntry(Func<IModelProvider> create, ContributionId? contributionId = null)
    {
        ContributionId = contributionId;
        _provider = new Lazy<IModelProvider>(() =>
        {
            var provider = create();
            Interlocked.Exchange(ref _retirable, provider as IRetirableModelProvider);
            if (Volatile.Read(ref _retired) != 0)
                RetireProvider();
            return provider;
        });
    }

    public ContributionId? ContributionId { get; }
    public IModelProvider Value => _provider.Value;

    public void Retire()
    {
        Interlocked.Exchange(ref _retired, 1);
        RetireProvider();
    }

    private void RetireProvider() => Interlocked.Exchange(ref _retirable, null)?.Retire();
}
