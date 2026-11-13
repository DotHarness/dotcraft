using DotCraft.Sessions;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public partial class TestableSessionService
{
    public TurnSettingsOverrides? LastSubmittedTurnSettings { get; private set; }
    public Func<string, TurnInputAdmission>? TurnAdmissionHandler { get; set; }

    public async Task<TurnInputAdmission> StartOrSteerTurnAsync(
        string threadId,
        IList<AIContent> content,
        SenderContext? sender = null,
        ChatMessage[]? messages = null,
        CancellationToken ct = default,
        SessionInputSnapshot? inputSnapshot = null,
        TurnSettingsOverrides? settings = null)
    {
        LastSubmittedTurnSettings = settings;
        if (TurnAdmissionHandler is not null)
        {
            LastSubmittedContent = content.ToList();
            LastSubmittedInputSnapshot = inputSnapshot;
            LastSubmitCancellationToken = ct;
            return TurnAdmissionHandler(threadId);
        }
        if (settings?.Cwd is not null || settings?.RuntimeWorkspaceRoots is not null)
            await ((ISessionService)this).UpdateThreadWorkspaceAsync(threadId, settings?.Cwd, settings?.RuntimeWorkspaceRoots, ct);
        return await CannedTurnAdmission.CaptureAsync(
            SubmitInputAsync(threadId, content, sender, messages, ct, inputSnapshot), ct);
    }
}
