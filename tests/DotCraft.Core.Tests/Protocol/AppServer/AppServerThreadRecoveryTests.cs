using DotCraft.AppServer;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerThreadRecoveryTests : IDisposable
{
    private readonly AppServerTestHarness _h = new();

    public AppServerThreadRecoveryTests() => _h.InitializeAsync().GetAwaiter().GetResult();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Export_ProjectsOpaquePackageDescriptor()
    {
        _h.Service.ExportThreadRecoveryHandler = (threadId, _) =>
            Task.FromResult(new ThreadRecoveryPackage(
                Path.Combine(_h.Identity.WorkspacePath, ".craft", "recovery-staging", "thread_1.json"),
                threadId,
                "turn_7",
                1,
                1234,
                new string('a', 64)));

        await _h.ExecuteRequestAsync(_h.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadRecoveryExport,
            new { threadId = "thread_1" }));

        using var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        Assert.Equal("thread_1", result.GetProperty("threadId").GetString());
        Assert.Equal("turn_7", result.GetProperty("terminalTurnId").GetString());
        Assert.Equal(1, result.GetProperty("formatVersion").GetInt32());
        Assert.Equal(1234, result.GetProperty("byteLength").GetInt64());
        Assert.Equal(new string('a', 64), result.GetProperty("sha256").GetString());
    }

    [Fact]
    public async Task Restore_ReturnsIdentityForOrdinaryResume()
    {
        _h.Service.RestoreThreadRecoveryHandler = (_, threadId, _) => Task.FromResult(threadId);

        await _h.ExecuteRequestAsync(_h.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadRecoveryRestore,
            new { packagePath = "thread_1.json", expectedThreadId = "thread_1" }));

        using var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        Assert.Equal("thread_1", result.GetProperty("threadId").GetString());
        Assert.False(result.TryGetProperty("thread", out _));
    }

    [Fact]
    public async Task Restore_MapsStableRecoveryError()
    {
        _h.Service.RestoreThreadRecoveryHandler = (_, _, _) =>
            throw new ThreadRecoveryException(
                ThreadRecoveryErrorCodes.WorkspaceMismatch,
                "Package belongs to another workspace.");

        await _h.ExecuteRequestAsync(_h.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadRecoveryRestore,
            new { packagePath = "thread_1.json", expectedThreadId = "thread_1" }));

        using var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.ThreadRecoveryFailedCode);
        var data = response.RootElement.GetProperty("error").GetProperty("data");
        Assert.Equal(ThreadRecoveryErrorCodes.WorkspaceMismatch, data.GetProperty("code").GetString());
        Assert.Equal("Thread recovery failed.", data.GetProperty("fallbackText").GetString());
    }
}
