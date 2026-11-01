using System.Net;
using System.Text.Json;

namespace DotCraft.Agents;

internal sealed class OpenRouterCaptureContent : HttpContent
{
    private readonly HttpContent _inner;
    private readonly OpenRouterCall _call;
    private readonly bool _streaming;

    internal OpenRouterCaptureContent(HttpContent inner, OpenRouterCall call)
    {
        _inner = inner; _call = call;
        _streaming = inner.Headers.ContentType?.MediaType == "text/event-stream";
        foreach (var header in inner.Headers) Headers.TryAddWithoutValidation(header.Key, header.Value);
    }

    protected override bool TryComputeLength(out long length) { length = 0; return false; }
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    { await using var reader = await CreateContentReadStreamAsync().ConfigureAwait(false); await reader.CopyToAsync(stream).ConfigureAwait(false); }
    protected override async Task<Stream> CreateContentReadStreamAsync() => new CaptureStream(await _inner.ReadAsStreamAsync().ConfigureAwait(false), _call, _streaming);
    protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken ct) => new CaptureStream(await _inner.ReadAsStreamAsync(ct).ConfigureAwait(false), _call, _streaming);
    protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }

    private sealed class CaptureStream(Stream inner, OpenRouterCall call, bool streaming) : Stream
    {
        private readonly MemoryStream _pending = new();
        private void Observe(ReadOnlySpan<byte> bytes, bool final)
        {
            if (!streaming)
            {
                _pending.Write(bytes);
                if (final) ParsePending();
                return;
            }
            while (!bytes.IsEmpty)
            {
                var end = bytes.IndexOf((byte)'\n');
                if (end < 0) { _pending.Write(bytes); break; }
                _pending.Write(bytes[..end]);
                ParsePending();
                bytes = bytes[(end + 1)..];
            }
            if (final && _pending.Length > 0) ParsePending();
        }
        private void ParsePending()
        {
            var json = _pending.GetBuffer().AsSpan(0, (int)_pending.Length);
            _pending.SetLength(0);
            if (streaming)
            {
                if (!json.StartsWith("data:"u8)) return;
                json = json[5..].Trim(" \t\r"u8);
            }
            if (json.IsEmpty || json.SequenceEqual("[DONE]"u8)) return;
            try
            {
                if (JsonSerializer.Deserialize<OpenRouterResponse>(json, OpenRouterJson.Options) is { } body) call.Read(body);
            }
            catch (JsonException) { }
        }
        public override int Read(byte[] buffer, int offset, int count) { var read = inner.Read(buffer, offset, count); Observe(buffer.AsSpan(offset, read), read == 0); return read; }
        public override int Read(Span<byte> buffer) { var read = inner.Read(buffer); Observe(buffer[..read], read == 0); return read; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) { var read = await inner.ReadAsync(buffer, ct).ConfigureAwait(false); Observe(buffer.Span[..read], read == 0); return read; }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) { _pending.Dispose(); inner.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
