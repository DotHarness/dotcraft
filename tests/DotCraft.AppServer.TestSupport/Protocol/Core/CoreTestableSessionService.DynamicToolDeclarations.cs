using DotCraft.Sessions;
using DotCraft.Tools;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

internal sealed partial class CoreTestableSessionService : IThreadDynamicToolDeclarationService
{
    public async Task UpdateThreadDynamicToolsAsync(
        string threadId,
        IReadOnlyList<ThreadDynamicToolDeclaration> declarations,
        Func<bool> canApply,
        Action updateLease,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        if (!canApply())
            return;

        var captured = ThreadDynamicToolDeclaration.Freeze(declarations);
        if (!ThreadDynamicToolDeclaration.Match(thread.DynamicToolDeclarations, captured))
        {
            if (thread.Ephemeral)
                thread.DynamicToolDeclarations = captured;
            else
                await _store.CommitDynamicToolDeclarationsAsync(thread, captured, ct);
        }

        updateLease();
    }
}
