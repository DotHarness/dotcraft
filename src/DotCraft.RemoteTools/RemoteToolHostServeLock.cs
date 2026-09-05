using System.Text;
using DotCraft.Processes;

namespace DotCraft.RemoteTools;

internal sealed class RemoteToolHostServeLock : IDisposable
{
    private readonly CrossProcessFileLock _fileLock;
    private bool _disposed;

    private RemoteToolHostServeLock(CrossProcessFileLock fileLock) => _fileLock = fileLock;

    public static bool TryAcquire(RemoteToolHostStorage storage, out RemoteToolHostServeLock? serveLock)
    {
        if (!CrossProcessFileLock.TryAcquire(storage.ServeLockPath, out var fileLock))
        {
            serveLock = null;
            return false;
        }

        fileLock!.Write(Encoding.UTF8.GetBytes(Environment.ProcessId.ToString()));
        serveLock = new RemoteToolHostServeLock(fileLock);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _fileLock.DeleteAfterDispose();
    }
}
