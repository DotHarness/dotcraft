using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerMemoryConsolidationTests : IDisposable
{
    private readonly AppServerTestHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task ThreadMemoryConsolidateStart_ReturnsConsolidationResult()
    {
        await _h.InitializeAsync();
        _h.Service.ConsolidateThreadMemoryHandler = (threadId, _) =>
            Task.FromResult(new ThreadMemoryConsolidationResult
            {
                Outcome = "succeeded",
                MemoryWritten = true,
                HistoryWritten = true
            });

        var msg = _h.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ThreadMemoryConsolidateStart, new
        {
            threadId = "thread_001"
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        var result = doc.RootElement.GetProperty("result");
        Assert.Equal("succeeded", result.GetProperty("outcome").GetString());
        Assert.True(result.GetProperty("memoryWritten").GetBoolean());
        Assert.True(result.GetProperty("historyWritten").GetBoolean());
    }

    [Fact]
    public async Task ThreadMemoryConsolidateStart_MapsRunningThreadToTurnInProgress()
    {
        await _h.InitializeAsync();
        _h.Service.ConsolidateThreadMemoryHandler = (threadId, _) =>
            throw new InvalidOperationException(
                $"Thread '{threadId}' has a running Turn. Wait for it to complete or cancel it first.");

        var msg = _h.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ThreadMemoryConsolidateStart, new
        {
            threadId = "thread_running"
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.TurnInProgressCode);
    }
}
