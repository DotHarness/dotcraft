using DotCraft.AppServer;
using DotCraft.Sessions;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerErrorTests : IDisposable
{
    private readonly AppServerTestHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task RequestsBeforeAndAfterInitialize_ReturnLifecycleErrorsWithTheRequestId()
    {
        await _h.ExecuteRequestAsync(InMemoryTransport.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadList,
            new { identity = new { channelName = "test", workspacePath = "/tmp" } },
            id: 42));
        using (var notInitialized = await _h.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsErrorResponse(notInitialized, AppServerErrors.NotInitializedCode);
            Assert.Equal(42, notInitialized.RootElement.GetProperty("id").GetInt32());
        }

        await _h.InitializeAsync();
        await _h.ExecuteRequestAsync(_h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.Initialize, new
        {
            clientInfo = new { name = "re-init", version = "0.0.1" }
        }));
        using (var alreadyInitialized = await _h.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsErrorResponse(alreadyInitialized, AppServerErrors.AlreadyInitializedCode);
            Assert.Equal(-32003, alreadyInitialized.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        }

        await _h.ExecuteRequestAsync(_h.BuildRequest("thread/doesNotExist"));
        using var methodNotFound = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(methodNotFound, AppServerErrors.MethodNotFoundCode);
    }

    [Fact]
    public async Task ThreadAndTurnLookups_ReturnTheirSpecificErrors()
    {
        await _h.InitializeAsync();
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        thread.Turns.Add(new SessionTurn
        {
            Id = "turn_001",
            ThreadId = thread.Id,
            Status = TurnStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        });

        foreach (var (method, parameters, code) in new (string Method, object Params, int Code)[]
                 {
                     (DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadRead, new { threadId = "thread_does_not_exist" }, AppServerErrors.ThreadNotFoundCode),
                     (DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadResume, new { threadId = "thread_ghost" }, AppServerErrors.ThreadNotFoundCode),
                     (DotCraft.Protocol.AppServer.AppServerMethodNames.TurnInterrupt, new { threadId = "thread_ghost", turnId = "turn_001" }, AppServerErrors.ThreadNotFoundCode),
                     (DotCraft.Protocol.AppServer.AppServerMethodNames.TurnInterrupt, new { threadId = thread.Id, turnId = "turn_does_not_exist" }, AppServerErrors.TurnNotFoundCode),
                     (DotCraft.Protocol.AppServer.AppServerMethodNames.TurnInterrupt, new { threadId = thread.Id, turnId = "turn_001" }, AppServerErrors.TurnNotRunningCode),
                     (DotCraft.Protocol.AppServer.AppServerMethodNames.TurnEnqueue, new { threadId = thread.Id, input = Array.Empty<object>() }, AppServerErrors.InvalidParamsCode)
                 })
        {
            await _h.ExecuteRequestAsync(_h.BuildRequest(method, parameters));
            using var response = await _h.Transport.ReadNextSentAsync();
            AppServerTestHarness.AssertIsErrorResponse(response, code);
        }
    }
}
