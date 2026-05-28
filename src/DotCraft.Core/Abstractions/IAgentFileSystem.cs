namespace DotCraft.Abstractions;

/// <summary>
/// Abstracts file access for channel tools that need host-local files.
/// In host mode, paths are resolved directly on the local filesystem.
/// In sandbox mode, files are extracted from the container to a temporary host location.
/// </summary>
public interface IAgentFileSystem
{
    /// <summary>
    /// Resolves a file path to a host-accessible location.
    /// Returns a disposable handle; disposing cleans up any temporary files created during extraction.
    /// </summary>
    Task<HostFileHandle> ResolveHostFileAsync(string path);

    /// <summary>
    /// Reads a file and returns its content as a base64-encoded string.
    /// Useful for protocols that accept inline base64 data (e.g. QQ voice <c>base64://</c>).
    /// </summary>
    Task<string> ReadAsBase64Async(string path);
}

/// <summary>
/// A handle to a host-accessible file. Dispose to clean up temporary files (if any).
/// In host mode, Dispose is a no-op. In sandbox mode, Dispose deletes the extracted temp file.
/// </summary>
public sealed class HostFileHandle : IDisposable
{
    /// <summary>
    /// Absolute path on the host filesystem.
    /// </summary>
    public string HostPath { get; }

    /// <summary>
    /// The file name (without directory).
    /// </summary>
    public string FileName { get; }

    private readonly string? _tempPath;

    /// <summary>
    /// Creates a handle wrapping an existing host path. Dispose is a no-op.
    /// </summary>
    public HostFileHandle(string hostPath)
    {
        HostPath = hostPath;
        FileName = Path.GetFileName(hostPath);
    }

    /// <summary>
    /// Creates a handle wrapping a temporary file extracted from the sandbox.
    /// Dispose deletes <paramref name="tempPath"/>.
    /// </summary>
    internal HostFileHandle(string hostPath, string fileName, string tempPath)
    {
        HostPath = hostPath;
        FileName = fileName;
        _tempPath = tempPath;
    }

    public void Dispose()
    {
        if (_tempPath == null) return;
        try { File.Delete(_tempPath); }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[AgentFileSystem] Failed to clean up temp file '{_tempPath}': {ex.Message}");
        }
    }
}
