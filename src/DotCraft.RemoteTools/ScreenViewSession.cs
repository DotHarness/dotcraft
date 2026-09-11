using System.Net.WebSockets;
using System.Text.Json;
using DotCraft.Protocol.ScreenView;
using DotCraft.Screen;

namespace DotCraft.RemoteTools;

internal static class ScreenViewSession
{
    private const int FailuresBeforeReport = 3;
    private const int OversizeQuality = 40;

    private static readonly TimeSpan UnavailablePoll = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CloseGrace = TimeSpan.FromSeconds(1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task RunAsync(
        Uri dataUri,
        string credential,
        Func<IScreenCaptureSource> captureFactory,
        Func<string> closeReason,
        CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        socket.Options.SetRequestHeader("Authorization", "Bearer " + credential);
        await socket.ConnectAsync(dataUri, cancellationToken).ConfigureAwait(false);

        using var source = captureFactory();
        var demand = new Demand();
        // The reader keeps its own token: cancelling a receive aborts the socket, and the close frame must leave first.
        using var viewerGone = new CancellationTokenSource();
        using var streaming = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, viewerGone.Token);
        var control = ReceiveControlAsync(socket, demand, viewerGone);
        try
        {
            await StreamAsync(socket, source, demand, streaming.Token).ConfigureAwait(false);
        }
        finally
        {
            await CloseAsync(socket, closeReason()).ConfigureAwait(false);
            await Task.WhenAny(control, Task.Delay(CloseGrace)).ConfigureAwait(false);
            await viewerGone.CancelAsync().ConfigureAwait(false);
            try { await control.ConfigureAwait(false); }
            catch (Exception) { }
        }
    }

    private static async Task StreamAsync(
        WebSocket socket,
        IScreenCaptureSource source,
        Demand demand,
        CancellationToken cancellationToken)
    {
        var header = new byte[ScreenViewProtocol.FrameHeaderBytes];
        var sequence = 0u;
        var reported = new ScreenViewCapability(true, source.Probe().UnavailableReason);
        await SendCapabilityAsync(socket, reported, cancellationToken).ConfigureAwait(false);
        var failures = 0;

        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var (control, changed) = demand.Read();
            if (control.Watchers <= 0)
            {
                await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            var cadence = TimeSpan.FromMilliseconds(1000d / control.Fps);
            var result = await CaptureAsync(
                source,
                new ScreenCaptureRequest(control.MaxWidth, control.Quality),
                cancellationToken).ConfigureAwait(false);
            if (result.Frame is null)
            {
                failures = result.UnavailableReason == ScreenCaptureReasons.CaptureFailed ? failures + 1 : 0;
                // A machine that just locked fails a frame or two before it recovers; the viewer hears about the third.
                var retrying = failures is > 0 and < FailuresBeforeReport;
                if (!retrying)
                {
                    reported = await ReportAsync(
                        socket,
                        reported,
                        new ScreenViewCapability(true, result.UnavailableReason, result.Detail),
                        cancellationToken).ConfigureAwait(false);
                }
                await Task.Delay(retrying ? cadence : UnavailablePoll, cancellationToken).ConfigureAwait(false);
                continue;
            }

            failures = 0;
            reported = await ReportAsync(socket, reported, new ScreenViewCapability(true, null), cancellationToken)
                .ConfigureAwait(false);
            if (result.Frame.Jpeg.Length <= ScreenViewProtocol.MaximumFrameBytes)
            {
                ScreenViewProtocol.WriteFrameHeader(header, new ScreenViewFrameHeader(
                    sequence++,
                    (ushort)result.Frame.Width,
                    (ushort)result.Frame.Height,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                await socket.SendAsync(header, WebSocketMessageType.Binary, false, cancellationToken).ConfigureAwait(false);
                await socket.SendAsync(result.Frame.Jpeg, WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
            }
            await Task.Delay(cadence, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<ScreenCaptureResult> CaptureAsync(
        IScreenCaptureSource source,
        ScreenCaptureRequest request,
        CancellationToken cancellationToken)
    {
        var result = await Task.Run(() => source.Capture(request), cancellationToken).ConfigureAwait(false);
        if (!Oversize(result))
            return result;
        var reduced = request with { Quality = OversizeQuality };
        result = await Task.Run(() => source.Capture(reduced), cancellationToken).ConfigureAwait(false);
        if (!Oversize(result))
            return result;
        var narrowed = reduced with
        {
            MaxWidth = Math.Max(ScreenViewProtocol.MinimumWidth, reduced.MaxWidth / 2)
        };
        return await Task.Run(() => source.Capture(narrowed), cancellationToken).ConfigureAwait(false);
    }

    private static bool Oversize(ScreenCaptureResult result) =>
        result.Frame is not null && result.Frame.Jpeg.Length > ScreenViewProtocol.MaximumFrameBytes;

    private static async Task<ScreenViewCapability> ReportAsync(
        WebSocket socket,
        ScreenViewCapability reported,
        ScreenViewCapability next,
        CancellationToken cancellationToken)
    {
        if (next == reported)
            return reported;
        await SendCapabilityAsync(socket, next, cancellationToken).ConfigureAwait(false);
        return next;
    }

    private static async Task ReceiveControlAsync(WebSocket socket, Demand demand, CancellationTokenSource viewerGone)
    {
        var buffer = new byte[4 * 1024];
        try
        {
            while (socket.State is WebSocketState.Open or WebSocketState.CloseSent)
            {
                var received = await socket.ReceiveAsync(buffer, viewerGone.Token).ConfigureAwait(false);
                if (received.MessageType == WebSocketMessageType.Close)
                    break;
                if (received.MessageType != WebSocketMessageType.Text || !received.EndOfMessage)
                    continue;
                var control = JsonSerializer.Deserialize<ScreenViewControl>(buffer.AsSpan(0, received.Count), Json);
                if (control is not null)
                    demand.Set(ScreenViewProtocol.Clamp(control));
            }
        }
        catch (Exception) when (viewerGone.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (ex is WebSocketException or JsonException)
        {
        }
        await viewerGone.CancelAsync().ConfigureAwait(false);
    }

    private static Task SendCapabilityAsync(WebSocket socket, ScreenViewCapability capability, CancellationToken cancellationToken) =>
        socket.SendAsync(
            JsonSerializer.SerializeToUtf8Bytes(capability, Json),
            WebSocketMessageType.Text,
            true,
            cancellationToken);

    private static async Task CloseAsync(WebSocket socket, string reason)
    {
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, reason, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
        }
    }

    private sealed class Demand
    {
        private readonly object _gate = new();
        private ScreenViewControl _control = new(
            0,
            ScreenViewProtocol.DefaultFps,
            ScreenViewProtocol.MaximumWidth,
            ScreenViewProtocol.MaximumQuality);
        private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public (ScreenViewControl Control, Task Changed) Read()
        {
            lock (_gate)
                return (_control, _changed.Task);
        }

        public void Set(ScreenViewControl control)
        {
            TaskCompletionSource signal;
            lock (_gate)
            {
                _control = control;
                signal = _changed;
                _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            signal.TrySetResult();
        }
    }
}
