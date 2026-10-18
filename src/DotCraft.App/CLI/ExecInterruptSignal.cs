namespace DotCraft.CLI;

internal sealed class ExecInterruptSignal : IDisposable
{
    private readonly CancellationTokenSource _requested = new();

    public ExecInterruptSignal() => Console.CancelKeyPress += OnCancelKeyPress;

    public CancellationToken Token => _requested.Token;

    public bool IsRequested => _requested.IsCancellationRequested;

    public void Dispose()
    {
        Console.CancelKeyPress -= OnCancelKeyPress;
        _requested.Dispose();
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        if (e.SpecialKey != ConsoleSpecialKey.ControlC)
            return;

        e.Cancel = true;
        try
        {
            _requested.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
