using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace DotCraft.Satellite.Services;

[SupportedOSPlatform("windows")]
internal sealed class SingleInstanceGate : IDisposable
{
    private const string Prefix = "DotCraft.Satellite.";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stopping = new();
    private Task? _listening;
    private bool _disposed;

    private SingleInstanceGate(Mutex mutex) => _mutex = mutex;

    public event EventHandler<InstanceMessage>? MessageReceived;

    public static string PipeName => Prefix + CurrentUserSid();

    public static SingleInstanceGate? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: false, @"Local\" + PipeName);
        try
        {
            if (mutex.WaitOne(TimeSpan.Zero))
                return new SingleInstanceGate(mutex);
        }
        catch (AbandonedMutexException)
        {
            return new SingleInstanceGate(mutex);
        }
        mutex.Dispose();
        return null;
    }

    public static bool TrySend(InstanceMessage message)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(TimeSpan.FromMilliseconds(500));
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            writer.WriteLine(message.Encode());
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void StartListening() => _listening ??= Task.Run(() => ListenAsync(_stopping.Token));

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _stopping.Cancel();
        try { _mutex.ReleaseMutex(); }
        catch (ApplicationException) { }
        _mutex.Dispose();
        _stopping.Dispose();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server);
                var line = await reader.ReadLineAsync(cancellationToken);
                if (InstanceMessage.Decode(line) is { } message)
                    MessageReceived?.Invoke(this, message);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // A client that disconnected mid-line only costs this iteration.
            }
        }
    }

    private static string CurrentUserSid()
    {
        try
        {
            return WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        }
        catch (Exception)
        {
            return Environment.UserName;
        }
    }
}
