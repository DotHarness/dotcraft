using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DotCraft.Sdk.Hub;

internal sealed class HubStartedProcess
{
    private readonly string _stderrPath;
    private readonly long _stderrOffset;

    public int? Pid { get; }
    public int? ExitCode { get; private set; }
    public bool IsLauncher { get; }
    public Task Exited { get; }
    public bool Failed => Pid is not null && Exited.IsCompletedSuccessfully && (!IsLauncher || ExitCode != 0);

    private HubStartedProcess(Process? process, bool isLauncher, string stderrPath, long stderrOffset)
    {
        _stderrPath = stderrPath;
        _stderrOffset = stderrOffset;
        Pid = process?.Id;
        IsLauncher = isLauncher || process is null;
        Exited = process is null ? Task.CompletedTask : ObserveExitAsync(process);
    }

    public string RecentStderr
    {
        get
        {
            try
            {
                using var stream = new FileStream(_stderrPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                stream.Position = Math.Max(_stderrOffset, stream.Length - 4096);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException) { return ""; }
            catch (UnauthorizedAccessException) { return ""; }
        }
    }

    public static HubStartedProcess Start(DotCraftHubClientOptions options, string lockPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stderrPath = Path.Combine(Path.GetDirectoryName(lockPath)!, "hub-startup.stderr.log");
        var offset = File.Exists(stderrPath) ? new FileInfo(stderrPath).Length : 0;
        var executable = string.IsNullOrWhiteSpace(options.Executable) ? "dotcraft" : options.Executable;
        if (options.StartHubProcess is { } launch)
            return new HubStartedProcess(launch(executable), false, stderrPath, offset);

        var isDll = executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        var info = new ProcessStartInfo
        {
            FileName = isDll ? "dotnet" : executable,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (isDll)
            info.ArgumentList.Add(executable);
        info.ArgumentList.Add("hub");
        info.ArgumentList.Add("--detach");
        info.ArgumentList.Add("--stderr-log");
        info.ArgumentList.Add(stderrPath);
        cancellationToken.ThrowIfCancellationRequested();
        return new HubStartedProcess(Process.Start(info)
            ?? throw new InvalidOperationException("Hub launcher did not start."), true, stderrPath, offset);
    }

    private async Task ObserveExitAsync(Process process)
    {
        using (process)
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            ExitCode = process.ExitCode;
        }
    }

    public static string Sanitize(string value)
    {
        var safe = Regex.Replace(value, @"Bearer\s+[^\s""<>]+", "Bearer [redacted]", RegexOptions.IgnoreCase);
        safe = Regex.Replace(safe, @"(?:https?|wss?)://[^\s""<>]+", match =>
        {
            if (!Uri.TryCreate(match.Value, UriKind.Absolute, out var uri))
                return "[invalid URL]";
            return new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.ToString();
        }, RegexOptions.IgnoreCase);
        return Regex.Replace(safe,
            @"((?:token|api[_-]?key|authorization|secret|password)[""']?\s*[:=]\s*[""']?)[^\s""',;]+",
            "$1[redacted]", RegexOptions.IgnoreCase);
    }
}
