namespace DotCraft.Agents;

public interface IProviderConnectionLifecycle
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
