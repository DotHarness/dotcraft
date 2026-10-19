using DotCraft.AppServer;
using DotCraft.Channels;
using DotCraft.Context.WorldState;
using DotCraft.Sessions;
using Xunit;
using SessionThread = DotCraft.Sessions.SessionThread;

namespace DotCraft.Tests.AppServer;

public sealed class ClientSurfaceSectionTests
{
    private static readonly ClientSurface Desktop = new("dotcraft-desktop", ClientSurfaceKind.DesktopApp);
    private static readonly ClientSurface Mobile = new("dotcraft-mobile", ClientSurfaceKind.MobileApp, RemoteHost: true);
    private static readonly ClientSurface Terminal = new("cli", ClientSurfaceKind.Terminal);

    private readonly SessionThread _thread = new() { Id = "thread-1", OriginChannel = "dotcraft-desktop" };
    private readonly WorldState _world = new();
    private WorldStateSnapshot? _baseline;

    public ClientSurfaceSectionTests() => _world.AddSection(new ClientSurfaceSection());

    [Fact]
    public void LocalAppClient_AddsNothing()
    {
        Assert.False(Sent(Desktop));
        Assert.False(Sent(Desktop));
    }

    [Fact]
    public void TurnWithoutAClient_KeepsWhatTheModelWasLastTold()
    {
        Assert.True(Sent(Mobile));
        Assert.False(Sent(null));
        Assert.False(Sent(Mobile));
    }

    [Fact]
    public void ChangedNotes_AreSentOnce_AndRetiredOnceWhenTheyStopApplying()
    {
        Assert.True(Sent(Mobile));
        Assert.False(Sent(Mobile));
        Assert.True(Sent(Terminal));
        Assert.True(Sent(Desktop));
        Assert.False(Sent(Desktop));
    }

    [Fact]
    public void SubAgentThread_HearsNothing()
    {
        _thread.Source = ThreadSource.ForSubAgent(new SubAgentThreadSource { ParentThreadId = "parent" });

        Assert.False(Sent(Mobile));
        Assert.False(Sent(Terminal));
    }

    private bool Sent(ClientSurface? surface)
    {
        var context = new WorldStateContext { Thread = _thread, ClientSurface = surface };
        var render = _baseline == null
            ? _world.RenderFull(context)
            : _world.Render(context, _baseline, sectionIdsInHistory: null);
        _baseline = render.Snapshot;
        return render.Fragments.Count > 0;
    }
}
