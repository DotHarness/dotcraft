using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Scripting;

public sealed record ScriptProtocolFrame
{
    public int Version { get; init; } = 1;
    public required long Sequence { get; init; }
    public required string Scope { get; init; }
    public required string Type { get; init; }
    public JsonNode? Payload { get; init; }
}

public sealed class ScriptProtocolConnection(
    Stream input,
    Stream output,
    int maxFrameBytes) : IAsyncDisposable
{
    private readonly StreamReader _reader = new(input, new UTF8Encoding(false), false, 4096, leaveOpen: true);
    private readonly StreamWriter _writer = new(output, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private long _outgoingSequence;
    private long _incomingSequence;

    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public async Task<ScriptProtocolFrame?> ReadAsync(CancellationToken cancellationToken)
    {
        var line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (line == null) return null;
        if (Encoding.UTF8.GetByteCount(line) > maxFrameBytes)
            throw new ScriptProtocolException("protocol_frame_too_large", "Script protocol frame exceeds the configured limit.");
        ScriptProtocolFrame frame;
        try
        {
            frame = JsonSerializer.Deserialize<ScriptProtocolFrame>(line, JsonOptions)
                ?? throw new JsonException("Frame was null.");
        }
        catch (JsonException ex)
        {
            throw new ScriptProtocolException("protocol_invalid_json", ex.Message, ex);
        }
        if (frame.Version != 1)
            throw new ScriptProtocolException("protocol_version_unsupported", $"Unsupported script protocol version {frame.Version}.");
        if (frame.Sequence != ++_incomingSequence)
            throw new ScriptProtocolException("protocol_sequence_invalid", $"Expected sequence {_incomingSequence}, received {frame.Sequence}.");
        return frame;
    }

    public async Task WriteAsync(
        string scope,
        string type,
        JsonNode? payload,
        CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var frame = new ScriptProtocolFrame
            {
                Sequence = ++_outgoingSequence,
                Scope = scope,
                Type = type,
                Payload = payload?.DeepClone()
            };
            var json = JsonSerializer.Serialize(frame, JsonOptions);
            if (Encoding.UTF8.GetByteCount(json) > maxFrameBytes)
                throw new ScriptProtocolException("protocol_frame_too_large", "Script protocol frame exceeds the configured limit.");
            await _writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _writeLock.Dispose();
        _reader.Dispose();
        _writer.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class ScriptProtocolException(string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
}
