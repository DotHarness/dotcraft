using System.Threading.Channels;

namespace DotCraft.Sessions;

/// <summary>
/// Serializes short Thread state transitions. Long-running Turn and maintenance work
/// must be started by a command and complete out of band; they must never occupy the
/// dispatcher while waiting on a model or tool.
/// </summary>
internal sealed class ThreadCommandDispatcher : IAsyncDisposable
{
    private readonly Channel<IThreadCommand> _commands = Channel.CreateUnbounded<IThreadCommand>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _pump;
    private int _disposed;

    public ThreadCommandDispatcher(string threadId)
    {
        ThreadId = threadId;
        _pump = PumpAsync();
    }

    public string ThreadId { get; }

    public Task InvokeAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<object?>(
            async ct =>
            {
                await action(ct).ConfigureAwait(false);
                return null;
            },
            cancellationToken);

    public Task<TResult> InvokeAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var command = new ThreadCommand<TResult>(action, cancellationToken, ExecutionContext.Capture());
        if (!_commands.Writer.TryWrite(command))
            command.Fail(new ObjectDisposedException(nameof(ThreadCommandDispatcher)));
        return command.Completion;
    }

    private async Task PumpAsync()
    {
        Exception? terminalFailure = null;
        try
        {
            await foreach (var command in _commands.Reader.ReadAllAsync(_stopping.Token).ConfigureAwait(false))
                await command.ExecuteAsync(_stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // Normal runtime shutdown.
        }
        catch (Exception ex)
        {
            terminalFailure = ex;
        }
        finally
        {
            while (_commands.Reader.TryRead(out var pending))
            {
                pending.Fail(terminalFailure ?? new ObjectDisposedException(nameof(ThreadCommandDispatcher)));
            }
        }

        if (terminalFailure != null)
            throw new InvalidOperationException($"Thread command dispatcher '{ThreadId}' stopped unexpectedly.", terminalFailure);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _commands.Writer.TryComplete();
        _stopping.Cancel();
        try
        {
            await _pump.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // Normal runtime shutdown.
        }
        finally
        {
            _stopping.Dispose();
        }
    }

    private interface IThreadCommand
    {
        Task ExecuteAsync(CancellationToken dispatcherToken);

        void Fail(Exception exception);
    }

    private sealed class ThreadCommand<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken callerToken,
        ExecutionContext? executionContext) : IThreadCommand
    {
        private readonly TaskCompletionSource<TResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TResult> Completion => _completion.Task;

        public async Task ExecuteAsync(CancellationToken dispatcherToken)
        {
            if (callerToken.IsCancellationRequested)
            {
                _completion.TrySetCanceled(callerToken);
                return;
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, dispatcherToken);
            try
            {
                Task<TResult>? task = null;
                if (executionContext == null)
                {
                    task = action(linked.Token);
                }
                else
                {
                    ExecutionContext.Run(
                        executionContext,
                        _ => task = action(linked.Token),
                        null);
                }

                var result = await task!.ConfigureAwait(false);
                _completion.TrySetResult(result);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                _completion.TrySetCanceled(callerToken.IsCancellationRequested ? callerToken : dispatcherToken);
            }
            catch (Exception ex)
            {
                _completion.TrySetException(ex);
            }
        }

        public void Fail(Exception exception) => _completion.TrySetException(exception);
    }
}
