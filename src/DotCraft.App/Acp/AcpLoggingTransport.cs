using System.Text;
using DotHarness.Acp.Transport;

namespace DotCraft.Acp;

public sealed class AcpLoggingTransport(
    IAcpTransport inner,
    AcpLogger? logger = null,
    TimeSpan? writeTimeout = null) : IAcpTransport
{
    private readonly TimeSpan _writeTimeout = writeTimeout is { } timeout
        && (timeout > TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
            ? timeout
            : TimeSpan.FromSeconds(30);

    public async ValueTask<ReadOnlyMemory<byte>?> ReadMessageAsync(CancellationToken ct = default)
    {
        var frame = await inner.ReadMessageAsync(ct).ConfigureAwait(false);
        if (frame is { } bytes && logger != null)
            logger.LogIncoming(Encoding.UTF8.GetString(bytes.Span));
        return frame;
    }

    public async ValueTask WriteMessageAsync(ReadOnlyMemory<byte> message, CancellationToken ct = default)
    {
        string? raw = null;
        if (logger != null)
        {
            raw = Encoding.UTF8.GetString(message.Span);
            logger.LogOutgoing(raw);
        }

        if (_writeTimeout == Timeout.InfiniteTimeSpan)
        {
            await inner.WriteMessageAsync(message, ct).ConfigureAwait(false);
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_writeTimeout);
        try
        {
            await inner.WriteMessageAsync(message, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            raw ??= Encoding.UTF8.GetString(message.Span);
            var preview = raw.Length > 200 ? raw[..200] + "..." : raw;
            logger?.LogError($"Write blocked ({_writeTimeout.TotalSeconds:F0}s) on pipe: {preview}");
            throw new TimeoutException($"ACP transport write timed out after {_writeTimeout.TotalSeconds:F0}s.");
        }
    }

    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
