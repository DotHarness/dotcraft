namespace DotCraft.Sessions;

/// <summary>Refreshes host-owned resources before a new turn captures its agent and tool snapshot.</summary>
public interface ISessionRuntimeRefresher
{
    Task RefreshAsync(CancellationToken cancellationToken);
}
