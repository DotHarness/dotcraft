using DotCraft.Agents;

namespace DotCraft.Sessions;

internal sealed class ProviderHistorySnapshotScope(
    IProviderConversationHistory? history,
    ThreadRuntime? runtime) : IDisposable
{
    public void Dispose()
    {
        if (history != null && runtime != null)
            runtime.ResponsesProviderHistorySnapshot = history.CaptureOpaqueSnapshot();
    }
}
