using DotCraft.Sessions;
using DotCraft.Tools;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public partial class TestableSessionService : IThreadToolSnapshotService
{
    public Dictionary<string, EffectiveToolSnapshot> ToolSnapshots { get; } = new(StringComparer.Ordinal);

    public Task<EffectiveToolSnapshot> GetEffectiveToolSnapshotAsync(string threadId,
        CancellationToken cancellationToken = default) => Task.FromResult(
            ToolSnapshots.GetValueOrDefault(threadId) ?? new EffectiveToolSnapshotBuilder().Build([], 1));
}
