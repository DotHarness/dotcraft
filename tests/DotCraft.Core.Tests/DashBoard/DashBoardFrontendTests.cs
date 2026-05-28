using DotCraft.DashBoard;

namespace DotCraft.Tests.DashBoard;

public sealed class DashBoardFrontendTests
{
    [Fact]
    public void Html_RendersDeferredToolLoadingTraceEvents()
    {
        var html = DashBoardFrontend.GetHtml();

        Assert.Contains("type-DeferredToolLoading", html);
        Assert.Contains("case 'DeferredToolLoading':", html);
        Assert.Contains("e.type === 'DeferredToolLoading'", html);
        Assert.Contains("strategy ${escapeHtml(strategy)}", html);
        Assert.Contains("protocol ${escapeHtml(md.providerProtocol)}", html);
    }

    [Fact]
    public void Html_GatesReadOnlyRuntimeCapabilities()
    {
        var html = DashBoardFrontend.GetHtml();

        Assert.Contains("loadDashboardRuntime", html);
        Assert.Contains("dashboardCapability('sessionDeletion')", html);
        Assert.Contains("Settings are disabled in read-only mode.", html);
        Assert.Contains("Session deletion is disabled in read-only mode.", html);
        Assert.Contains("id=\"navTabSettings\"", html);
        Assert.Contains("id=\"clearAllSessionsBtn\"", html);
    }
}
