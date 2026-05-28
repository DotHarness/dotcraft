using System.Text;
using System.Text.Json;

namespace DotCraft.Sdk.Wire;

/// <summary>
/// JSONL transport for DotCraft AppServer stdio connections.
/// </summary>
public sealed class StreamJsonRpcTransport : IJsonRpcTransport
{
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Creates a line-delimited JSON-RPC transport over input and output streams.
    /// </summary>
    public StreamJsonRpcTransport(Stream input, Stream output)
    {
        _reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        _writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
        {
            AutoFlush = true
        };
    }

    /// <inheritdoc />
    public async Task<JsonDocument?> ReadAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await _reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            return JsonDocument.Parse(line);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task WriteAsync(object message, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message, DotCraftJson.Options);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _writer.WriteLineAsync(json.AsMemory(), cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _reader.Dispose();
        await _writeLock.WaitAsync();
        try
        {
            await _writer.DisposeAsync();
        }
        finally
        {
            _writeLock.Release();
            _writeLock.Dispose();
        }
    }
}
