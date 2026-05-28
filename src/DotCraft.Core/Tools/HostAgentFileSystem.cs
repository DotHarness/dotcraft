using DotCraft.Abstractions;

namespace DotCraft.Tools;

/// <summary>
/// Host-mode implementation of <see cref="IAgentFileSystem"/>.
/// Resolves paths directly on the local filesystem, relative to the workspace root.
/// </summary>
public sealed class HostAgentFileSystem(string workspacePath) : IAgentFileSystem
{
    internal const long MaxTransferSize = 50 * 1024 * 1024; // 50 MB

    public Task<HostFileHandle> ResolveHostFileAsync(string path)
    {
        var resolved = ResolveAndValidate(path);
        return Task.FromResult(new HostFileHandle(resolved));
    }

    public async Task<string> ReadAsBase64Async(string path)
    {
        var resolved = ResolveAndValidate(path);

        var fileInfo = new FileInfo(resolved);
        if (fileInfo.Length > MaxTransferSize)
            throw new IOException(
                $"File too large for transfer ({fileInfo.Length} bytes). Maximum: {MaxTransferSize} bytes.");

        var bytes = await File.ReadAllBytesAsync(resolved);
        return Convert.ToBase64String(bytes);
    }

    private string ResolveAndValidate(string path)
    {
        var resolved = ResolvePath(path);
        if (!File.Exists(resolved))
            throw new FileNotFoundException($"File not found: {path}", resolved);
        return resolved;
    }

    private string ResolvePath(string path)
    {
        try
        {
            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(workspacePath, path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw new ArgumentException($"Invalid file path: {path}", nameof(path), ex);
        }
    }
}
