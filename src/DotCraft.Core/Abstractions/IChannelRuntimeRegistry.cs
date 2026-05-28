namespace DotCraft.Abstractions;

/// <summary>
/// Stores unified channel runtimes by name.
/// </summary>
public interface IChannelRuntimeRegistry
{
    void Register(IChannelRuntime runtime);

    bool TryRemove(string channelName);

    bool TryGet(string channelName, out IChannelRuntime? runtime);

    IReadOnlyList<IChannelRuntime> Snapshot();
}
