namespace DotCraft.Hosting;

public interface IDotCraftHost : IAsyncDisposable
{
    Task RunAsync(CancellationToken cancellationToken = default);
}
