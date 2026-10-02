using System.Diagnostics;
using System.Reflection;
using System.Text;
using DotCraft.Processes;

namespace DotCraft.Scripting;

public static class ScriptWorkerProcess
{
    public static ManagedChildProcess Start(
        IManagedChildProcessFactory processFactory,
        string kind,
        string workingDirectory) =>
        processFactory.Start(CreateStartInfo(kind, workingDirectory));

    private static ProcessStartInfo CreateStartInfo(string kind, string workingDirectory)
    {
        var processPath = Environment.ProcessPath;
        var entryAssembly = Assembly.GetEntryAssembly()?.Location;
        var loadedAppAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, "dotcraft", StringComparison.OrdinalIgnoreCase))
            ?.Location;
        var binary = !string.IsNullOrWhiteSpace(processPath)
                     && string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotcraft", StringComparison.OrdinalIgnoreCase)
            ? processPath
            : !string.IsNullOrWhiteSpace(entryAssembly)
              && string.Equals(Path.GetFileNameWithoutExtension(entryAssembly), "dotcraft", StringComparison.OrdinalIgnoreCase)
                ? entryAssembly
                : loadedAppAssembly;
        if (string.IsNullOrWhiteSpace(binary)) throw new InvalidOperationException("Cannot resolve the current DotCraft executable.");
        var startInfo = new ProcessStartInfo
        {
            FileName = binary.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? "dotnet" : binary,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        if (binary.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) startInfo.ArgumentList.Add(binary);
        startInfo.ArgumentList.Add("script-worker");
        startInfo.ArgumentList.Add(kind);
        return startInfo;
    }

    public static async Task DrainStderrAsync(
        Process process,
        int maxBytes,
        Func<string, CancellationToken, Task> onLine,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (!cancellationToken.IsCancellationRequested && await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            var bytes = Encoding.UTF8.GetByteCount(line);
            if (total + bytes > maxBytes) continue;
            total += bytes;
            await onLine(line, cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task MonitorRssAsync(
        Process process,
        long maxBytes,
        Action onExceeded,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (process.HasExited) return;
            process.Refresh();
            if (process.WorkingSet64 <= maxBytes) continue;
            onExceeded();
            return;
        }
    }
}
