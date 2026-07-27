using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace DotCraft.Protocol;

internal sealed record RolloutWriteReceipt(
    long ConfirmedOffset,
    int RecordCount,
    IReadOnlyDictionary<string, long> BytesByKind);

internal sealed class RolloutPersistenceException(string message, Exception innerException)
    : IOException(message, innerException);

internal interface IRolloutWriter
{
    Task AddBatchAsync(
        string threadId,
        string path,
        IReadOnlyList<ThreadRolloutRecord> records,
        CancellationToken ct = default);

    Task<RolloutWriteReceipt> FlushAsync(string threadId, CancellationToken ct = default);

    Task CloseAsync(string threadId, CancellationToken ct = default);

    Task ShutdownAllAsync(CancellationToken ct = default);
}

internal sealed class OrderedRolloutWriter : IRolloutWriter
{
    internal const int Capacity = 256;
    private const int WriteRetryCount = 5;

    private static readonly Meter Meter = new("DotCraft.Protocol.Rollout");
    private static readonly Counter<long> RecordsWritten = Meter.CreateCounter<long>("dotcraft.rollout.records.written");
    private static readonly Counter<long> BytesWritten = Meter.CreateCounter<long>("dotcraft.rollout.bytes.written");
    private static readonly Histogram<double> FlushDuration = Meter.CreateHistogram<double>("dotcraft.rollout.flush.duration", "ms");

    private readonly ConcurrentDictionary<string, ThreadWriter> _writers = new(StringComparer.Ordinal);
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Func<string, IReadOnlyList<ThreadRolloutRecord>, CancellationToken, Task<RolloutWriteReceipt>>? _testFlush;

    public OrderedRolloutWriter(JsonSerializerOptions? jsonOptions = null)
    {
        _jsonOptions = jsonOptions ?? SessionJsonOptions.Default;
    }

    internal OrderedRolloutWriter(
        Func<string, IReadOnlyList<ThreadRolloutRecord>, CancellationToken, Task<RolloutWriteReceipt>> testFlush)
        : this()
    {
        _testFlush = testFlush;
    }

    public async Task AddBatchAsync(
        string threadId,
        string path,
        IReadOnlyList<ThreadRolloutRecord> records,
        CancellationToken ct = default)
    {
        if (records.Count == 0)
            return;

        var writer = _writers.GetOrAdd(threadId, _ => new ThreadWriter(_jsonOptions, _testFlush));
        await writer.AddBatchAsync(path, records, ct);
    }

    public async Task<RolloutWriteReceipt> FlushAsync(string threadId, CancellationToken ct = default)
    {
        if (!_writers.TryGetValue(threadId, out var writer))
            return new RolloutWriteReceipt(0, 0, new Dictionary<string, long>(StringComparer.Ordinal));

        var started = Stopwatch.GetTimestamp();
        try
        {
            var receipt = await writer.FlushAsync(ct);
            FlushDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, new KeyValuePair<string, object?>("outcome", "success"));
            foreach (var (kind, bytes) in receipt.BytesByKind)
            {
                RecordsWritten.Add(writer.LastFlushedRecordCounts.GetValueOrDefault(kind), new KeyValuePair<string, object?>("record.kind", kind));
                BytesWritten.Add(bytes, new KeyValuePair<string, object?>("record.kind", kind));
            }
            return receipt;
        }
        catch
        {
            FlushDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, new KeyValuePair<string, object?>("outcome", "failure"));
            throw;
        }
    }

    public async Task CloseAsync(string threadId, CancellationToken ct = default)
    {
        if (!_writers.TryGetValue(threadId, out var writer))
            return;

        await writer.CloseAsync(ct);
        _writers.TryRemove(new KeyValuePair<string, ThreadWriter>(threadId, writer));
    }

    public async Task ShutdownAllAsync(CancellationToken ct = default)
    {
        List<Exception>? errors = null;
        foreach (var (threadId, writer) in _writers.ToArray())
        {
            try
            {
                await writer.CloseAsync(ct);
                _writers.TryRemove(new KeyValuePair<string, ThreadWriter>(threadId, writer));
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }
        }

        if (errors is { Count: 1 })
            throw errors[0];
        if (errors is { Count: > 1 })
            throw new AggregateException(errors);
    }

    private sealed class ThreadWriter
    {
        private readonly Channel<WriterCommand> _channel = Channel.CreateBounded<WriterCommand>(
            new BoundedChannelOptions(Capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
        private readonly SemaphoreSlim _enqueueGate = new(1, 1);
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly Func<string, IReadOnlyList<ThreadRolloutRecord>, CancellationToken, Task<RolloutWriteReceipt>>? _testFlush;
        private readonly Task _pump;
        private readonly List<PendingRecord> _pending = [];
        private FileStream? _stream;
        private string? _path;
        private long? _pendingStartOffset;
        private int _writtenPendingCount;

        public ThreadWriter(
            JsonSerializerOptions jsonOptions,
            Func<string, IReadOnlyList<ThreadRolloutRecord>, CancellationToken, Task<RolloutWriteReceipt>>? testFlush)
        {
            _jsonOptions = jsonOptions;
            _testFlush = testFlush;
            _pump = Task.Run(PumpAsync);
        }

        public IReadOnlyDictionary<string, long> LastFlushedRecordCounts { get; private set; } =
            new Dictionary<string, long>(StringComparer.Ordinal);

        public async Task AddBatchAsync(
            string path,
            IReadOnlyList<ThreadRolloutRecord> records,
            CancellationToken ct)
        {
            await _enqueueGate.WaitAsync(ct);
            try
            {
                foreach (var record in records)
                    await _channel.Writer.WriteAsync(new AddRecordCommand(path, record), ct);
            }
            finally
            {
                _enqueueGate.Release();
            }
        }

        public async Task<RolloutWriteReceipt> FlushAsync(CancellationToken ct)
        {
            await _enqueueGate.WaitAsync(ct);
            try
            {
                var completion = new TaskCompletionSource<RolloutWriteReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
                await _channel.Writer.WriteAsync(new FlushCommand(completion), CancellationToken.None);
                return await completion.Task.WaitAsync(ct);
            }
            finally
            {
                _enqueueGate.Release();
            }
        }

        public async Task CloseAsync(CancellationToken ct)
        {
            await _enqueueGate.WaitAsync(ct);
            var closed = false;
            try
            {
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await _channel.Writer.WriteAsync(new CloseCommand(completion), CancellationToken.None);
                await completion.Task.WaitAsync(ct);
                await _pump.WaitAsync(ct);
                closed = true;
            }
            finally
            {
                _enqueueGate.Release();
                if (closed)
                    _enqueueGate.Dispose();
            }
        }

        private async Task PumpAsync()
        {
            await foreach (var command in _channel.Reader.ReadAllAsync())
            {
                switch (command)
                {
                    case AddRecordCommand add:
                        if (_path != null && !string.Equals(_path, add.Path, StringComparison.OrdinalIgnoreCase))
                        {
                            _pending.Add(new PendingRecord(
                                add.Record,
                                [],
                                new InvalidOperationException("A live rollout recorder cannot change paths before it is closed.")));
                            break;
                        }

                        _path ??= add.Path;
                        _pending.Add(new PendingRecord(add.Record, SerializeRecord(add.Record), null));
                        break;

                    case FlushCommand flush:
                        try
                        {
                            flush.Completion.TrySetResult(await WritePendingWithRecoveryAsync());
                        }
                        catch (Exception ex)
                        {
                            flush.Completion.TrySetException(ex);
                        }
                        break;

                    case CloseCommand close:
                        try
                        {
                            await WritePendingWithRecoveryAsync();
                            await DisposeStreamAsync();
                            close.Completion.TrySetResult();
                            _channel.Writer.TryComplete();
                            return;
                        }
                        catch (Exception ex)
                        {
                            close.Completion.TrySetException(ex);
                        }
                        break;
                }
            }
        }

        private byte[] SerializeRecord(ThreadRolloutRecord record) =>
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record, _jsonOptions) + "\n");

        private async Task<RolloutWriteReceipt> WritePendingWithRecoveryAsync()
        {
            if (_pending.FirstOrDefault(static record => record.Error != null) is { Error: { } pendingError })
                throw pendingError;

            IOException? lastError = null;
            for (var attempt = 0; attempt <= WriteRetryCount; attempt++)
            {
                try
                {
                    return await WritePendingOnceAsync();
                }
                catch (IOException ex)
                {
                    lastError = ex;
                    await DisposeStreamAsync();
                    if (attempt == WriteRetryCount)
                        break;
                    await Task.Delay(TimeSpan.FromMilliseconds(20 << Math.Min(attempt, 4)));
                }
            }

            throw new RolloutPersistenceException(
                $"Rollout flush failed after {WriteRetryCount + 1} attempts.",
                lastError ?? new IOException("Rollout flush failed."));
        }

        private async Task<RolloutWriteReceipt> WritePendingOnceAsync()
        {
            if (_path == null)
                return new RolloutWriteReceipt(0, 0, new Dictionary<string, long>(StringComparer.Ordinal));
            if (_pending.Count == 0)
            {
                LastFlushedRecordCounts = new Dictionary<string, long>(StringComparer.Ordinal);
                var offset = _testFlush == null && File.Exists(_path)
                    ? new FileInfo(_path).Length
                    : 0;
                return new RolloutWriteReceipt(offset, 0, new Dictionary<string, long>(StringComparer.Ordinal));
            }

            if (_testFlush != null)
            {
                var records = _pending.Select(static pending => pending.Record).ToList();
                var receipt = await _testFlush(_path, records, CancellationToken.None);
                _pending.Clear();
                LastFlushedRecordCounts = records
                    .GroupBy(static record => record.Kind, StringComparer.Ordinal)
                    .ToDictionary(static group => group.Key, static group => (long)group.Count(), StringComparer.Ordinal);
                return receipt;
            }

            await EnsureStreamOpenAsync(_path);
            await RecoverWrittenPrefixAsync();
            var bytesByKind = new Dictionary<string, long>(StringComparer.Ordinal);
            var countsByKind = new Dictionary<string, long>(StringComparer.Ordinal);
            for (var i = _writtenPendingCount; i < _pending.Count; i++)
            {
                var record = _pending[i];
                _stream!.Seek(0, SeekOrigin.End);
                await _stream!.WriteAsync(record.Bytes);
                _writtenPendingCount = i + 1;
            }

            await _stream!.FlushAsync();
            _stream.Flush(flushToDisk: true);
            var confirmedOffset = _stream.Position;
            foreach (var record in _pending)
            {
                var kind = record.Record.Kind;
                bytesByKind[kind] = bytesByKind.GetValueOrDefault(kind) + record.Bytes.Length;
                countsByKind[kind] = countsByKind.GetValueOrDefault(kind) + 1;
            }
            var written = _pending.Count;
            _pending.Clear();
            _pendingStartOffset = null;
            _writtenPendingCount = 0;
            await DisposeStreamAsync();
            LastFlushedRecordCounts = countsByKind;
            return new RolloutWriteReceipt(confirmedOffset, written, bytesByKind);
        }

        private async Task RecoverWrittenPrefixAsync()
        {
            if (_pending.Count == 0)
            {
                _pendingStartOffset = null;
                _writtenPendingCount = 0;
                return;
            }

            _pendingStartOffset ??= _stream!.Length;
            var position = _pendingStartOffset.Value;
            var matched = 0;
            foreach (var pending in _pending)
            {
                if (position + pending.Bytes.Length > _stream!.Length)
                    break;

                var actual = new byte[pending.Bytes.Length];
                _stream.Seek(position, SeekOrigin.Begin);
                var read = 0;
                while (read < actual.Length)
                {
                    var count = await _stream.ReadAsync(actual.AsMemory(read));
                    if (count == 0)
                        break;
                    read += count;
                }

                if (read != actual.Length || !actual.AsSpan().SequenceEqual(pending.Bytes))
                    break;

                position += pending.Bytes.Length;
                matched++;
            }

            // Any bytes after the last complete, matching record are an unconfirmed
            // partial suffix from this recorder and can be replaced safely.
            if (_stream!.Length != position)
            {
                _stream.SetLength(position);
                await _stream.FlushAsync();
            }

            _stream.Seek(0, SeekOrigin.End);
            _writtenPendingCount = matched;
        }

        private async Task EnsureStreamOpenAsync(string path)
        {
            if (_stream != null)
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var stream = new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous);
            if (_pendingStartOffset == null && stream.Length > 0)
            {
                stream.Seek(-1, SeekOrigin.End);
                var last = stream.ReadByte();
                stream.Seek(0, SeekOrigin.End);
                if (last != (byte)'\n')
                {
                    await stream.WriteAsync("\n"u8.ToArray());
                    await stream.FlushAsync();
                }
            }
            else
            {
                stream.Seek(0, SeekOrigin.End);
            }

            _stream = stream;
        }

        private async Task DisposeStreamAsync()
        {
            if (_stream == null)
                return;
            await _stream.DisposeAsync();
            _stream = null;
        }
    }

    private abstract record WriterCommand;

    private sealed record AddRecordCommand(string Path, ThreadRolloutRecord Record) : WriterCommand;

    private sealed record FlushCommand(TaskCompletionSource<RolloutWriteReceipt> Completion) : WriterCommand;

    private sealed record CloseCommand(TaskCompletionSource Completion) : WriterCommand;

    private sealed record PendingRecord(ThreadRolloutRecord Record, byte[] Bytes, Exception? Error);
}

internal static class RolloutTelemetry
{
    private static readonly Meter Meter = new("DotCraft.Protocol.Rollout.Resume");
    private static readonly Histogram<long> ResumeBytesRead = Meter.CreateHistogram<long>("dotcraft.rollout.resume.bytes_read", "By");
    private static readonly Histogram<long> ResumeRecordsDecoded = Meter.CreateHistogram<long>("dotcraft.rollout.resume.records.decoded");
    private static readonly Histogram<long> ResumeRecordsRejected = Meter.CreateHistogram<long>("dotcraft.rollout.resume.records.rejected");

    public static void RecordResume(long bytesRead, int recordsDecoded, int recordsRejected = 0)
    {
        ResumeBytesRead.Record(bytesRead);
        ResumeRecordsDecoded.Record(recordsDecoded);
        ResumeRecordsRejected.Record(recordsRejected);
    }
}
