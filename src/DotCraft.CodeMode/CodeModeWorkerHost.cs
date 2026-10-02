using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using DotCraft.Processes;
using DotCraft.Scripting;
using Microsoft.Extensions.Logging;

namespace DotCraft.CodeMode;

public sealed class CodeModeUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

public sealed class CodeModeWorkerHost(
    IManagedChildProcessFactory processFactory,
    string workspacePath,
    CodeModeLimits limits,
    ILogger? logger = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private volatile Worker? _worker;
    private volatile string? _unavailableReason;
    private bool _disposed;

    public CodeModeLimits Limits => limits;

    public string? UnavailableReason => _unavailableReason;

    public async Task<bool> TryStartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await EnsureWorkerAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (CodeModeUnavailableException)
        {
            return false;
        }
    }

    public async Task<CodeModeCellSession> StartCellAsync(
        string threadId,
        JsonObject payload,
        CancellationToken cancellationToken)
    {
        var worker = await EnsureWorkerAsync(cancellationToken).ConfigureAwait(false);
        var session = new CodeModeCellSession("cell_" + Guid.NewGuid().ToString("N"), threadId, worker);
        worker.Cells[session.Id] = session;
        try
        {
            await worker.Connection.WriteAsync(session.Id, CodeModeProtocol.CellStart, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await worker.FailAsync($"the worker could not accept the script ({ex.Message})").ConfigureAwait(false);
        }
        return session;
    }

    public void CancelThread(string threadId)
    {
        if (_worker is not { } worker)
            return;
        foreach (var session in worker.Cells.Values.Where(session => session.ThreadId == threadId))
            session.RequestStop("the thread was deleted");
    }

    public async ValueTask DisposeAsync()
    {
        await _startGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            if (_worker is { } worker)
                await worker.FailAsync("DotCraft is shutting down").ConfigureAwait(false);
        }
        finally
        {
            _startGate.Release();
        }
    }

    private async Task<Worker> EnsureWorkerAsync(CancellationToken cancellationToken)
    {
        if (_worker is { IsAlive: true } current)
            return current;
        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_worker is { IsAlive: true } existing)
                return existing;
            var worker = await StartWorkerAsync(cancellationToken).ConfigureAwait(false);
            _worker = worker;
            _unavailableReason = null;
            return worker;
        }
        finally
        {
            _startGate.Release();
        }
    }

    private async Task<Worker> StartWorkerAsync(CancellationToken cancellationToken)
    {
        ManagedChildProcess? process = null;
        try
        {
            process = ScriptWorkerProcess.Start(processFactory, CodeModeProtocol.WorkerKind, workspacePath);
            var connection = new ScriptProtocolConnection(
                process.Process.StandardOutput.BaseStream,
                process.Process.StandardInput.BaseStream,
                limits.MaxFrameBytes);
            using var startTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startTimeout.CancelAfter(limits.WorkerStartTimeout);
            var ready = await connection.ReadAsync(startTimeout.Token).ConfigureAwait(false);
            if (ready is not { Type: CodeModeProtocol.Ready })
                throw new ScriptProtocolException("protocol_ready_missing", "The worker did not report ready.");
            var worker = new Worker(this, process, connection);
            worker.Start();
            return worker;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (process != null) await process.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            if (process != null) await process.DisposeAsync().ConfigureAwait(false);
            var reason = ex is OperationCanceledException
                ? "The code mode worker did not start in time."
                : $"The code mode worker could not start: {ex.Message}";
            _unavailableReason = reason;
            logger?.LogWarning(ex, "Code mode worker failed to start.");
            throw new CodeModeUnavailableException(reason, ex);
        }
    }

    internal sealed class Worker(CodeModeWorkerHost host, ManagedChildProcess process, ScriptProtocolConnection connection)
    {
        private readonly CancellationTokenSource _lifetime = new();
        private int _failed;

        public ScriptProtocolConnection Connection => connection;
        public ConcurrentDictionary<string, CodeModeCellSession> Cells { get; } = new(StringComparer.Ordinal);
        public bool IsAlive => Volatile.Read(ref _failed) == 0;

        public void Start()
        {
            _ = ReadAsync();
            _ = ScriptWorkerProcess.DrainStderrAsync(
                process.Process,
                host.Limits.MaxStderrBytes,
                (line, _) =>
                {
                    host.Log(line);
                    return Task.CompletedTask;
                },
                _lifetime.Token).ContinueWith(static task => _ = task.Exception, TaskScheduler.Default);
            _ = ScriptWorkerProcess.MonitorRssAsync(
                process.Process,
                host.Limits.MaxWorkerRssBytes,
                () => _ = FailAsync($"the worker exceeded its {host.Limits.MaxWorkerRssBytes / (1024 * 1024)} MiB memory limit"),
                _lifetime.Token).ContinueWith(static task => _ = task.Exception, TaskScheduler.Default);
        }

        public void Release(CodeModeCellSession session) => Cells.TryRemove(session.Id, out _);

        public async Task FailAsync(string cause)
        {
            if (Interlocked.Exchange(ref _failed, 1) != 0)
                return;
            if (ReferenceEquals(host._worker, this))
                host._worker = null;
            await _lifetime.CancelAsync().ConfigureAwait(false);
            try { await process.DisposeAsync().ConfigureAwait(false); }
            catch { }
            foreach (var session in Cells.Values)
                session.Fail(cause);
            Cells.Clear();
            host.LogFailure(cause);
        }

        private async Task ReadAsync()
        {
            try
            {
                while (await connection.ReadAsync(_lifetime.Token).ConfigureAwait(false) is { } frame)
                {
                    if (frame.Type is not (CodeModeProtocol.CallRequest or CodeModeProtocol.CellOutput or CodeModeProtocol.CellDone))
                        throw new ScriptProtocolException("protocol_message_invalid", $"Unexpected worker message '{frame.Type}'.");
                    if (!Cells.TryGetValue(frame.Scope, out var session))
                        continue;
                    if (frame.Type == CodeModeProtocol.CellDone)
                        Cells.TryRemove(frame.Scope, out _);
                    session.Post(frame);
                }
                await FailAsync("the worker exited unexpectedly").ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch (ScriptProtocolException ex)
            {
                await FailAsync($"the worker violated the script protocol ({ex.Message})").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await FailAsync($"the worker connection failed ({ex.Message})").ConfigureAwait(false);
            }
        }
    }

    private void Log(string line) => logger?.LogDebug("Code mode worker: {Line}", line);

    private void LogFailure(string cause) => logger?.LogWarning("Code mode worker stopped: {Cause}.", cause);
}

public abstract record CodeModeCellEvent
{
    public sealed record Frame(ScriptProtocolFrame Value) : CodeModeCellEvent;
    public sealed record WorkerFailed(string Cause) : CodeModeCellEvent;
    public sealed record StopRequested(string Reason) : CodeModeCellEvent;
}

public sealed class CodeModeCellSession
{
    private readonly CodeModeWorkerHost.Worker _worker;
    private readonly Channel<CodeModeCellEvent> _events = Channel.CreateUnbounded<CodeModeCellEvent>();

    internal CodeModeCellSession(string id, string threadId, CodeModeWorkerHost.Worker worker)
    {
        Id = id;
        ThreadId = threadId;
        _worker = worker;
    }

    public string Id { get; }
    public string ThreadId { get; }
    public ChannelReader<CodeModeCellEvent> Events => _events.Reader;

    public async Task SendAsync(string type, JsonObject? payload)
    {
        if (!_worker.IsAlive)
            return;
        try
        {
            await _worker.Connection.WriteAsync(Id, type, payload, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _worker.FailAsync($"the worker connection failed ({ex.Message})").ConfigureAwait(false);
        }
    }

    public Task KillWorkerAsync(string cause) => _worker.FailAsync(cause);

    public Task ReportProtocolViolationAsync(string message) =>
        _worker.FailAsync($"the worker violated the script protocol ({message})");

    public void Release() => _worker.Release(this);

    internal void Post(ScriptProtocolFrame frame) => _events.Writer.TryWrite(new CodeModeCellEvent.Frame(frame));

    internal void Fail(string cause) => _events.Writer.TryWrite(new CodeModeCellEvent.WorkerFailed(cause));

    internal void RequestStop(string reason) => _events.Writer.TryWrite(new CodeModeCellEvent.StopRequested(reason));
}
