using System.Text;
using System.Text.Json;
using DotCraft.Processes;

namespace DotCraft.Hub;

internal sealed class ManagedLocalServiceProcess : IManagedLocalServiceProcess
{
    private const string ReadyType = "dotcraft.managed-service.ready";
    // Drain buffered output after exit without waiting for inherited pipe handles indefinitely.
    private static readonly TimeSpan ExitDrainTimeout = TimeSpan.FromSeconds(1);
    private readonly ManagedChildProcess _child;
    private readonly CancellationTokenSource _reading = new();
    private readonly TaskCompletionSource<ManagedServiceReady> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Queue<string> _stderr = new();
    private readonly Task _stdoutTask;
    private readonly Task _stderrTask;
    private readonly Task _exitTask;
    private Task? _disposeTask;
    private bool _disposing;

    public ManagedLocalServiceProcess(ManagedChildProcess child)
    {
        _child = child;
        ProcessId = child.Process.Id;
        _stdoutTask = ConsumeStdoutAsync();
        _stderrTask = CaptureStderrAsync();
        _exitTask = ObserveExitAsync();
    }

    public int ProcessId { get; }
    public bool IsRunning => !_disposing && !_child.Process.HasExited;
    public string? RecentStderr
    {
        get
        {
            lock (_stderr)
                return _stderr.Count == 0 ? null : string.Join(Environment.NewLine, _stderr);
        }
    }

    public Task<ManagedServiceReady> WaitForReadyAsync(CancellationToken cancellationToken) =>
        _ready.Task.WaitAsync(cancellationToken);

    public ValueTask DisposeAsync() => new(_disposeTask ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        _disposing = true;
        try
        {
            await _child.DisposeAsync();
        }
        finally
        {
            await _reading.CancelAsync();
            await Task.WhenAll(_stdoutTask, _stderrTask, _exitTask);
            _reading.Dispose();
        }
    }

    private async Task ConsumeStdoutAsync()
    {
        try
        {
            var output = _child.Process.StandardOutput;
            while (await output.ReadLineAsync(_reading.Token) is { } line)
            {
                if (!TryReadReady(line, out var ready))
                    continue;

                _ready.TrySetResult(ready!);
                var buffer = new char[4096];
                while (await output.ReadAsync(buffer.AsMemory(), _reading.Token) != 0) { }
                return;
            }
        }
        catch (Exception ex) when (IsExpectedReadFailure(ex)) { }
        catch (Exception ex)
        {
            RecordStderr($"Managed service stdout read failed: {ex.Message}");
            _ready.TrySetException(ex);
        }
        finally
        {
            _ready.TrySetException(new InvalidOperationException("Managed service exited before reporting readiness."));
            // A cancelled startup may leave no readiness waiter to observe this failure.
            _ = _ready.Task.Exception;
        }
    }

    private static bool TryReadReady(string line, out ManagedServiceReady? ready)
    {
        ready = null;
        JsonDocument document;
        try { document = JsonDocument.Parse(line); }
        catch (JsonException) { return false; }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || type.GetString() != ReadyType)
                return false;
            ready = new ManagedServiceReady(
                root.GetProperty("serviceId").GetString() ?? string.Empty,
                root.GetProperty("endpoint").GetString() ?? string.Empty,
                root.TryGetProperty("version", out var version) ? version.GetString() : null);
            return true;
        }
    }

    private async Task CaptureStderrAsync()
    {
        try
        {
            var buffer = new char[4096];
            var line = new StringBuilder();
            int count;
            while ((count = await _child.Process.StandardError.ReadAsync(buffer.AsMemory(), _reading.Token)) != 0)
            {
                for (var i = 0; i < count; i++)
                {
                    if (buffer[i] == '\n')
                    {
                        RecordStderr(line.ToString().TrimEnd('\r'));
                        line.Clear();
                    }
                    else if (line.Length < buffer.Length)
                    {
                        line.Append(buffer[i]);
                    }
                }
            }
            if (line.Length > 0)
                RecordStderr(line.ToString());
        }
        catch (Exception ex) when (IsExpectedReadFailure(ex)) { }
        catch (Exception ex)
        {
            RecordStderr($"Managed service stderr read failed: {ex.Message}");
        }
    }

    private async Task ObserveExitAsync()
    {
        try
        {
            await _child.Process.WaitForExitAsync(_reading.Token);
            _reading.CancelAfter(ExitDrainTimeout);
        }
        catch (Exception ex) when (IsExpectedReadFailure(ex)) { }
    }

    private bool IsExpectedReadFailure(Exception ex) =>
        (_disposing || _reading.IsCancellationRequested)
        && ex is OperationCanceledException or ObjectDisposedException or IOException;

    private void RecordStderr(string line)
    {
        lock (_stderr)
        {
            _stderr.Enqueue(line);
            while (_stderr.Count > 20)
                _stderr.Dequeue();
        }
    }
}
