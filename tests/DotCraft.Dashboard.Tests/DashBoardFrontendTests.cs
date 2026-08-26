using DotCraft.DashBoard;
using Xunit;

namespace DotCraft.Tests.DashBoard;

public sealed class DashBoardFrontendTests
{
    [Fact]
    public void Html_RendersDeferredToolLoadingTraceEvents()
    {
        var html = DashBoardFrontend.GetHtml();

        Assert.Contains("type-DeferredToolLoading", html);
        Assert.Contains("case 'DeferredToolLoading':", html);
        Assert.Contains("evt.type === 'DeferredToolLoading'", html);
        Assert.Contains("strategy ${escapeHtml(strategy)}", html);
        Assert.Contains("protocol ${escapeHtml(md.providerProtocol)}", html);
        Assert.Contains("wire ${escapeHtml(md.wireShape)}", html);
        Assert.Contains("event.type === 'SessionMetadata' || event.type === 'ToolInjection' || event.type === 'PromptCacheDiagnostic'", html);
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
        Assert.Contains("evt.type === 'ThreadRollback'", html);
        Assert.Contains("rollback ${formatNumber(session.rollbackCount)}", html);
    }

    [Fact]
    public void Html_RendersProviderAttemptDiagnosticsInExistingTraceTimeline()
    {
        var html = DashBoardFrontend.GetHtml();

        Assert.Contains("data-filter=\"Provider\"", html);
        Assert.Contains("md.eventType === 'stream_attempt'", html);
        Assert.Contains("Provider Stream Attempt", html);
        Assert.Contains("request ID: ${escapeHtml(md.requestId)}", html);
        Assert.Contains("session ${shortHash(md.sessionIdHash)}", html);
        Assert.Contains("thread ${shortHash(md.threadIdHash)}", html);
        Assert.Contains("cache ${shortHash(md.promptCacheKeyHash)}", html);
    }

    [Fact]
    public void Html_UsesSharedProviderAndResponseFilterCategories()
    {
        var html = DashBoardFrontend.GetHtml();

        Assert.Contains("function traceEventMatchesFilter(evt, filter)", html);
        Assert.Contains("filter === 'Response') return evt.type === 'Response' || evt.type === 'ResponseTerminal'", html);
        Assert.Contains("filter === 'Provider') return evt.type === 'ProviderResponseDiagnostic' || evt.type === 'ProviderError'", html);
        Assert.Contains("decoratedEvents.filter(e => traceEventMatchesFilter(e, currentFilter))", html);
        Assert.Contains("return traceEventMatchesFilter(evt, currentFilter);", html);
    }

    [Fact]
    public void Html_RendersAgentInstructionsInTraceTimeline()
    {
        var html = DashBoardFrontend.GetHtml();

        Assert.Contains("data-filter=\"AgentInstructions\"", html);
        Assert.Contains("case 'AgentInstructions':", html);
        Assert.Contains("AGENTS.md instructions", html);
        Assert.Contains("No AGENTS.md instructions were loaded.", html);
        Assert.Contains("escapeHtml(md.fingerprint || '')", html);
        Assert.Contains("escapeHtml(source)", html);
        Assert.Contains("renderExpandableTraceText(evtId, e.content, 500, { mono: true })", html);
    }

    [Fact]
    public void Html_RendersSubAgentRelationshipsAndPrefixDiagnostics()
    {
        var html = DashBoardFrontend.GetHtml();

        Assert.Contains("renderSessionRelationship(session)", html);
        Assert.Contains("sub-agent of", html);
        Assert.Contains("shared cache prefix", html);
        Assert.Contains("static prefix shared", html);
        Assert.Contains("parent static prefix diverged", html);
        Assert.Contains("prefix unavailable", html);
        Assert.Contains("prefix not recorded", html);
        Assert.Contains("case 'SubAgentPrefixDiagnostic':", html);
        Assert.Contains("inherited input lost", html);
        Assert.Contains("tool-schema divergence", html);
    }
}
