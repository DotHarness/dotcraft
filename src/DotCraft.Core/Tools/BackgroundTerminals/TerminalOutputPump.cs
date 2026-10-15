using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace DotCraft.Tools.BackgroundTerminals;

internal sealed class TerminalOutputPump(
    Process process,
    string outputPath,
    long maxLiveBytes,
    Action<string> publish)
{
    private const int MaxEvents = 10_000;
    private static readonly TimeSpan ExitDrainGrace = TimeSpan.FromSeconds(2);
    private readonly Channel<string> _input = Channel.CreateBounded<string>(new BoundedChannelOptions(64)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait
    });
    private readonly Channel<string> _notifications = Channel.CreateBounded<string>(new BoundedChannelOptions(64)
    {
        SingleReader = true,
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.Wait
    });
    private readonly CancellationTokenSource _readCancellation = new();
    private readonly TerminalOutputBuffer _buffer = new();
    private Task? _completion;
    private volatile bool _readsAbandoned;
    private bool _notificationsStopped;
    private long _publishedBytes;
    private int _publishedEvents;

    public void Start() => _completion = RunAsync();

    // A descendant that inherited the pipes can hold them open after the shell exits.
    public async Task DrainAsync()
    {
        if (_completion == null)
            return;
        if (await Task.WhenAny(_completion, Task.Delay(ExitDrainGrace)).ConfigureAwait(false) != _completion)
        {
            _readsAbandoned = true;
            _input.Writer.TryComplete();
        }
        await _completion.ConfigureAwait(false);
    }

    public (string Output, int OriginalChars, bool Truncated) Snapshot(int maxCharacters) =>
        _buffer.Snapshot(maxCharacters);

    public long Characters => _buffer.Characters;

    public TerminalOutputSlice Slice(long from, int maxCharacters, bool tailOnly = false) =>
        _buffer.Slice(from, maxCharacters, tailOnly);

    public (string Text, long End, bool Lossy) SliceRaw(long from) => _buffer.SliceRaw(from);

    public event Action? Appended;

    private async Task ReadAsync(Stream stream)
    {
        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            await foreach (var text in TerminalOutputBuffer.ReadChunksAsync(reader, _readCancellation.Token))
                await _input.Writer.WriteAsync(text, _readCancellation.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _input.Writer.TryComplete(ex);
            await _readCancellation.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task ReadBothAsync()
    {
        try
        {
            await Task.WhenAll(ReadAsync(process.StandardOutput.BaseStream), ReadAsync(process.StandardError.BaseStream)).ConfigureAwait(false);
            _input.Writer.TryComplete();
        }
        catch (Exception ex)
        {
            _input.Writer.TryComplete(ex);
            throw;
        }
    }

    private async Task RunAsync()
    {
        var notifications = Task.Run(PublishAsync);
        var reads = ReadBothAsync();
        try
        {
            await using var log = new StreamWriter(new FileStream(
                outputPath, FileMode.Append, FileAccess.Write, FileShare.Read,
                4096, FileOptions.Asynchronous), new UTF8Encoding(false));
            while (await _input.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                if (!_input.Reader.TryRead(out var first))
                    continue;
                var batch = new StringBuilder(first);
                if (_input.Reader.Count < 8)
                    await Task.Delay(50).ConfigureAwait(false);
                while (batch.Length < 32 * 1024 && _input.Reader.TryRead(out var next))
                    batch.Append(next);
                var text = batch.ToString();
                await log.WriteAsync(text).ConfigureAwait(false);
                await log.FlushAsync().ConfigureAwait(false);
                _buffer.Append(text);
                Appended?.Invoke();
                QueueNotifications(text);
            }
            if (_readsAbandoned)
            {
                _ = reads.ContinueWith(task => task.Exception, TaskContinuationOptions.OnlyOnFaulted);
                await _readCancellation.CancelAsync().ConfigureAwait(false);
            }
            else
            {
                await reads.ConfigureAwait(false);
            }
        }
        catch
        {
            await _readCancellation.CancelAsync().ConfigureAwait(false);
            try { process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            try { await reads.ConfigureAwait(false); }
            catch (Exception) { }
            throw;
        }
        finally
        {
            _notifications.Writer.TryComplete();
            await notifications.ConfigureAwait(false);
            _readCancellation.Dispose();
        }
    }

    private void QueueNotifications(string text)
    {
        if (_notificationsStopped)
            return;
        foreach (var frame in TerminalOutputBuffer.SplitFrames(text, 8192))
        {
            var bytes = Encoding.UTF8.GetByteCount(frame);
            if (_publishedEvents >= MaxEvents
                || bytes > Math.Max(0, maxLiveBytes) - _publishedBytes
                || !_notifications.Writer.TryWrite(frame))
            {
                _notificationsStopped = true;
                _notifications.Writer.TryComplete();
                return;
            }
            _publishedEvents++;
            _publishedBytes += bytes;
        }
    }

    private async Task PublishAsync()
    {
        await foreach (var frame in _notifications.Reader.ReadAllAsync().ConfigureAwait(false))
            publish(frame);
    }
}
