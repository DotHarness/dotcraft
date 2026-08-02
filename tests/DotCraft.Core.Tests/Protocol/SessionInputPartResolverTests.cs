using System.Net;
using System.Net.Sockets;
using System.Text;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionInputPartResolverTests
{
    [Fact]
    public async Task ResolvePersistedAsync_LegacyRemoteImage_DoesNotOpenNetworkConnection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var serverCts = new CancellationTokenSource();
        var requestSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = RespondIfConnectedAsync(listener, requestSeen, serverCts.Token);
        var endpoint = (IPEndPoint)listener.LocalEndpoint;

        var content = await SessionInputPartResolver.ResolvePersistedAsync(
            [
                new SessionWireInputPart { Type = "text", Text = "before" },
                new SessionWireInputPart
                {
                    Type = "image",
                    Url = $"http://127.0.0.1:{endpoint.Port}/image.png"
                },
                new SessionWireInputPart { Type = "text", Text = "after" }
            ],
            CancellationToken.None);

        Assert.Collection(
            content,
            part => Assert.Equal("before", Assert.IsType<TextContent>(part).Text),
            part => Assert.Equal(
                SessionInputPartResolver.RemoteImageOmittedText,
                Assert.IsType<TextContent>(part).Text),
            part => Assert.Equal("after", Assert.IsType<TextContent>(part).Text));
        await Task.Delay(100);
        Assert.False(requestSeen.Task.IsCompleted);

        await serverCts.CancelAsync();
        listener.Stop();
        await serverTask;
    }

    [Fact]
    public async Task ResolvePersistedAsync_InlineImage_DecodesDataContent()
    {
        var content = await SessionInputPartResolver.ResolvePersistedAsync(
            [new SessionWireInputPart { Type = "image", Url = "data:image/png;base64,AQID" }],
            CancellationToken.None);

        var image = Assert.IsType<DataContent>(Assert.Single(content));
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal([1, 2, 3], image.Data.ToArray());
    }

    [Fact]
    public async Task ResolveStrictAsync_OversizedDecodedPayload_IsRejectedBeforeDecode()
    {
        var exception = await Assert.ThrowsAsync<SessionInputPartValidationException>(() =>
            SessionInputPartResolver.ResolveStrictAsync(
                [new SessionWireInputPart { Type = "image", Url = "data:image/png;base64,AQID" }],
                maxInlineImageBytes: 2,
                CancellationToken.None));

        Assert.Contains("64 MiB", exception.Message, StringComparison.Ordinal);
    }

    private static async Task RespondIfConnectedAsync(
        TcpListener listener,
        TaskCompletionSource requestSeen,
        CancellationToken ct)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            requestSeen.TrySetResult();
            await using var stream = client.GetStream();
            var response = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: image/png\r\nContent-Length: 3\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response, ct);
            await stream.WriteAsync(new byte[] { 1, 2, 3 }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (SocketException) when (ct.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (ct.IsCancellationRequested)
        {
        }
    }
}
