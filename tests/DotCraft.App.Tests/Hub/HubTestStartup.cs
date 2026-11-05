using DotCraft.Hub;

namespace DotCraft.Tests.Hub;

internal static class HubTestStartup
{
    internal static async Task<HubLockInfo> WaitForReadyAsync(string lockFilePath, Task running, CancellationToken ct)
    {
        using var http = new HttpClient();
        while (true)
        {
            if (running.IsCompleted)
            {
                await running;
                throw new InvalidOperationException("Hub exited before its local API became ready.");
            }
            if (HubLockFile.TryRead(lockFilePath) is { } info)
            {
                try
                {
                    using var response = await http.GetAsync($"{info.ApiBaseUrl}/v1/status", ct);
                    if (response.IsSuccessStatusCode) return info;
                }
                catch (HttpRequestException) { }
            }
            await Task.Delay(20, ct);
        }
    }
}
