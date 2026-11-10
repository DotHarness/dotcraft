using System.Diagnostics;

namespace DotCraft.McpTestServer;

public sealed class McpFixtureProcess : IAsyncDisposable
{
    private readonly Process _process;

    private McpFixtureProcess(Process process, string endpoint)
    {
        _process = process;
        Endpoint = endpoint;
    }

    public static string AssemblyPath => typeof(McpFixtureProcess).Assembly.Location;
    public string Endpoint { get; }

    public static async Task<McpFixtureProcess> StartHttpAsync(bool legacy = false)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(AssemblyPath);
        start.ArgumentList.Add("http");
        if (legacy) start.ArgumentList.Add("legacy");
        var process = Process.Start(start)!;
        try
        {
            var endpoint = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (endpoint is null)
                throw new IOException(await process.StandardError.ReadToEndAsync());
            return new McpFixtureProcess(process, endpoint);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        await _process.WaitForExitAsync();
        _process.Dispose();
    }
}
