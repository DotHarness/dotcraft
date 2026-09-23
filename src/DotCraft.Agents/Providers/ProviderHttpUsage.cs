using System.Text.Json;

namespace DotCraft.Agents;

public sealed record ProviderHttpUsage(
    long InputTokens = 0,
    long OutputTokens = 0,
    long CachedInputTokens = 0,
    long CacheWriteTokens = 0,
    long ReasoningTokens = 0);

public interface IProviderHttpUsageObserver
{
    ProviderHttpUsage? Usage { get; }
    void Append(ReadOnlySpan<byte> bytes);
    void Complete();
}

public sealed class JsonHttpUsageObserver(
    bool eventStream,
    Func<ProviderHttpUsage, string, long, ProviderHttpUsage> update) : IProviderHttpUsageObserver
{
    private byte[] _buffer = new byte[4096];
    private int _length;
    private JsonReaderState _state;
    private readonly List<string> _path = [];
    private string _property = "";
    private bool _failed;

    public ProviderHttpUsage? Usage { get; private set; }

    public void Append(ReadOnlySpan<byte> bytes)
    {
        if (_failed)
            return;
        if (_length + bytes.Length > _buffer.Length)
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + bytes.Length));
        bytes.CopyTo(_buffer.AsSpan(_length));
        _length += bytes.Length;
        try
        {
            if (eventStream)
                ReadEvents(false);
            else
                ReadJson(_buffer.AsSpan(0, _length), false, reset: false);
        }
        catch (JsonException)
        {
            _failed = true;
        }
    }

    public void Complete()
    {
        if (_failed)
            return;
        try
        {
            if (eventStream)
                ReadEvents(true);
            else
                ReadJson(_buffer.AsSpan(0, _length), true, reset: false);
        }
        catch (JsonException) { _failed = true; }
    }

    private void ReadEvents(bool complete)
    {
        var consumed = 0;
        while (consumed < _length)
        {
            var remaining = _buffer.AsSpan(consumed, _length - consumed);
            var newline = remaining.IndexOf((byte)'\n');
            if (newline < 0 && !complete)
                break;
            var line = (newline < 0 ? remaining : remaining[..newline]).TrimEnd((byte)'\r');
            if (line.StartsWith("data:"u8))
            {
                var data = line[5..].TrimStart((byte)' ');
                if (!data.SequenceEqual("[DONE]"u8))
                    ReadJson(data, true, reset: true);
            }
            consumed += newline < 0 ? remaining.Length : newline + 1;
        }
        _buffer.AsSpan(consumed, _length - consumed).CopyTo(_buffer);
        _length -= consumed;
    }

    private void ReadJson(ReadOnlySpan<byte> bytes, bool complete, bool reset)
    {
        if (reset)
        {
            _state = default;
            _path.Clear();
            _property = "";
        }
        var reader = new Utf8JsonReader(bytes, complete, _state);
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    _property = reader.GetString()!;
                    break;
                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    _path.Add(_property);
                    _property = "";
                    break;
                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    _path.RemoveAt(_path.Count - 1);
                    break;
                case JsonTokenType.Number:
                    if (IsUsagePath() && reader.TryGetInt64(out var value))
                        Usage = update(Usage ?? new ProviderHttpUsage(), _property, value);
                    break;
            }
        }
        if (!reset)
        {
            _state = reader.CurrentState;
            var consumed = (int)reader.BytesConsumed;
            _buffer.AsSpan(consumed, _length - consumed).CopyTo(_buffer);
            _length -= consumed;
        }
    }

    private bool IsUsagePath() =>
        _path.Count >= 2 && (_path[1] == "usage"
            || (_path.Count >= 3 && (_path[1] is "message" or "response") && _path[2] == "usage"));
}
