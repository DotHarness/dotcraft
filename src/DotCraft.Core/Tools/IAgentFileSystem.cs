namespace DotCraft.Tools;

/// <summary>
/// Abstracts file access for channel tools that need host-local files.
/// Paths are resolved directly on the local filesystem.
/// </summary>
public interface IAgentFileSystem
{
    /// <summary>
    /// Resolves a file path to a host-accessible location.
    /// Returns a handle to the local file.
    /// </summary>
    Task<HostFileHandle> ResolveHostFileAsync(string path);

    /// <summary>
    /// Reads a file and returns its content as a base64-encoded string.
    /// Useful for protocols that accept inline base64 data (e.g. QQ voice <c>base64://</c>).
    /// </summary>
    Task<string> ReadAsBase64Async(string path);
}

/// <summary>
/// A handle to a host-accessible file.
/// </summary>
public sealed class HostFileHandle
{
    /// <summary>
    /// Absolute path on the host filesystem.
    /// </summary>
    public string HostPath { get; }

    /// <summary>
    /// The file name (without directory).
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// Creates a handle wrapping an existing host path.
    /// </summary>
    public HostFileHandle(string hostPath)
    {
        HostPath = hostPath;
        FileName = Path.GetFileName(hostPath);
    }
}
