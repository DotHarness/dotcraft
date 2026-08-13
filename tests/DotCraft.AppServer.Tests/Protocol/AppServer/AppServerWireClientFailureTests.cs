using System.IO.Pipelines;
using DotCraft.AppServer;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerWireClientFailureTests
{
    [Fact]
    public async Task SendRequest_WhenTransportCloses_ReportsEndOfStream()
    {
        var input = new Pipe();
        await using var wire = new AppServerWireClient(
            input.Reader.AsStream(),
            new MemoryStream());
        wire.Start();

        var request = wire.SendRequestAsync("test/request", timeout: TimeSpan.FromSeconds(5));
        await input.Writer.CompleteAsync();

        var error = await Assert.ThrowsAsync<EndOfStreamException>(() => request);
        Assert.Contains("transport closed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendRequest_WhenInternalDeadlineExpires_ReportsTimeout()
    {
        var input = new Pipe();
        await using var wire = new AppServerWireClient(
            input.Reader.AsStream(),
            new MemoryStream());
        wire.Start();

        var error = await Assert.ThrowsAsync<TimeoutException>(() =>
            wire.SendRequestAsync("test/request", timeout: TimeSpan.FromMilliseconds(50)));

        Assert.Contains("test/request", error.Message, StringComparison.Ordinal);
        await input.Writer.CompleteAsync();
    }

    [Fact]
    public async Task SendRequest_WhenCallerCancels_PreservesCancellation()
    {
        var input = new Pipe();
        await using var wire = new AppServerWireClient(
            input.Reader.AsStream(),
            new MemoryStream());
        wire.Start();
        using var cts = new CancellationTokenSource();

        var request = wire.SendRequestAsync(
            "test/request",
            timeout: TimeSpan.FromSeconds(5),
            ct: cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await input.Writer.CompleteAsync();
    }
}
