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

    [Fact]
    public void Html_UsesPagedTraceLoading()
    {
        var html = DashBoardFrontend.GetHtml();

        Assert.Contains("TRACE_PAGE_LIMIT = 1000", html);
        Assert.Contains("/events/page", html);
        Assert.Contains("/events/page'", html);
        Assert.Contains("beforeCursor", html);
        Assert.Contains("handleTraceScroll", html);
        Assert.Contains("loadOlderTraceEvents", html);
    }

    [Fact]
    public void Html_RendersThreadRollbackTraceEventsAndOperations()
    {
        var html = DashBoardFrontend.GetHtml();

        Assert.Contains("type-ThreadRollback", html);
        Assert.Contains("case 'ThreadRollback':", html);
        Assert.Contains("fetchSessionOperations", html);
        Assert.Contains("/operations", html);
        Assert.Contains("mergeTraceEventsWithOperations", html);
        Assert.Contains("rollbackDedupeKeyFromEvent", html);
        Assert.Contains("e.type === 'ThreadRollback'", html);
        Assert.Contains("evt.type === 'ThreadRollback'", html);
        Assert.Contains("rollback ${formatNumber(session.rollbackCount)}", html);
    }
}
