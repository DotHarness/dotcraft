using System.Text;
using DotCraft.Agents;
using Microsoft.Extensions.Logging;

namespace DotCraft.Heartbeat;

public sealed class HeartbeatService(
    string workspacePath,
    AgentRunSessionDelegate onHeartbeat,
    int intervalSeconds = 1800,
    bool enabled = true,
    ILogger<HeartbeatService>? logger = null)
    : IDisposable
{
    private CancellationTokenSource? _cts;
    
    private readonly SemaphoreSlim _triggerLock = new(1, 1);

    public Func<string, Task>? OnResult { get; set; }

    public void Start()
    {
        if (!enabled || _cts != null)
            return;

        _cts = new CancellationTokenSource();
        _ = RunLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    public async Task<string?> TriggerNowAsync()
    {
        await _triggerLock.WaitAsync();
        try
        {
            return await TickAsync();
        }
        finally
        {
            _triggerLock.Release();
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken);
                await _triggerLock.WaitAsync(cancellationToken);
                try
                {
                    var result = await TickAsync();
                    if (result != null && OnResult != null)
                        await OnResult(result);
                }
                finally
                {
                    _triggerLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Heartbeat tick failed");
            }
        }
    }

    private async Task<string?> TickAsync()
    {
        var heartbeatPath = Path.Combine(workspacePath, "HEARTBEAT.md");
        if (!File.Exists(heartbeatPath))
            return null;

        var content = await File.ReadAllTextAsync(heartbeatPath, Encoding.UTF8);
        if (IsHeartbeatEmpty(content))
            return null;

        var prompt = $"Heartbeat check. Current HEARTBEAT.md content:\n\n{content}\n\nExecute any actionable tasks described above. If there are no actionable tasks, respond with HEARTBEAT_OK.";
        var run = await onHeartbeat(prompt, "heartbeat", null, CancellationToken.None);
        return run?.Result;
    }

    private static bool IsHeartbeatEmpty(string content)
    {
        var lines = content.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;
            if (trimmed.StartsWith('#') || trimmed.StartsWith("<!--"))
                continue;
            return false;
        }
        return true;
    }

    public void Dispose()
    {
        Stop();
        _triggerLock.Dispose();
    }
}
