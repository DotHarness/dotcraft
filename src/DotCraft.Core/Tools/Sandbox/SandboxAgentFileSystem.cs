using DotCraft.Abstractions;

namespace DotCraft.Tools.Sandbox;

/// <summary>
/// Sandbox-mode implementation of <see cref="IAgentFileSystem"/>.
/// Extracts files from the sandbox container to temporary host locations
/// using base64 encoding to preserve binary content.
/// </summary>
public sealed class SandboxAgentFileSystem(SandboxSessionManager sandboxManager) : IAgentFileSystem
{
    public async Task<HostFileHandle> ResolveHostFileAsync(string path)
    {
        var sandboxPath = ResolveSandboxPath(path);
        var base64 = await ReadBase64FromSandboxAsync(sandboxPath);

        var bytes = Convert.FromBase64String(base64);
        if (bytes.Length > HostAgentFileSystem.MaxTransferSize)
            throw new IOException(
                $"File too large for transfer ({bytes.Length} bytes). Maximum: {HostAgentFileSystem.MaxTransferSize} bytes.");

        var fileName = Path.GetFileName(sandboxPath);
        var ext = Path.GetExtension(fileName);
        var tempPath = Path.Combine(Path.GetTempPath(), $"dotcraft_{Guid.NewGuid():N}{ext}");
        await File.WriteAllBytesAsync(tempPath, bytes);

        return new HostFileHandle(tempPath, fileName, tempPath);
    }

    public async Task<string> ReadAsBase64Async(string path)
    {
        var base64 = await ReadBase64FromSandboxAsync(ResolveSandboxPath(path));

        // Infer binary size from base64 length to avoid decoding a huge string.
        // base64 encodes 3 bytes as 4 chars, so decoded_size ≈ length * 3 / 4.
        var approxBytes = (long)base64.Length * 3 / 4;
        if (approxBytes > HostAgentFileSystem.MaxTransferSize)
            throw new IOException(
                $"File too large for transfer (~{approxBytes} bytes). Maximum: {HostAgentFileSystem.MaxTransferSize} bytes.");

        return base64;
    }

    private async Task<string> ReadBase64FromSandboxAsync(string sandboxPath)
    {
        var sandbox = await sandboxManager.GetOrCreateAsync();
        var escaped = "'" + sandboxPath.Replace("'", "'\\''") + "'";
        var result = await sandbox.Commands.RunAsync($"base64 -w0 {escaped}");
        var output = string.Join("", result.Logs.Stdout.Select(l => l.Text?.Trim()));

        if (string.IsNullOrEmpty(output))
            throw new FileNotFoundException($"File not found in sandbox: {sandboxPath}");

        return output;
    }

    private static string ResolveSandboxPath(string path)
    {
        // Normalize Windows-style separators (host may be Windows)
        path = path.Replace('\\', '/');

        // Strip Windows drive letter prefix (e.g., "C:/workspace/file" -> "/workspace/file")
        if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && path[2] == '/')
            path = path[2..];

        if (path.StartsWith('/'))
            return path;
        if (path.StartsWith("./"))
            return "/workspace/" + path[2..];
        return "/workspace/" + path;
    }
}
