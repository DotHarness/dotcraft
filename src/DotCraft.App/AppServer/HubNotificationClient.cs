using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace DotCraft.AppServer;

/// <summary>
/// Sends best-effort notifications from a Hub-managed AppServer back to Hub.
/// </summary>
internal static class HubNotificationClient
{
    public static async Task RequestAsync(
        string workspacePath,
        string kind,
        string title,
        string? body,
        string severity,
        string? threadId = null,
        string? actionUrl = null,
        bool? openDesktopOnClick = null,
        CancellationToken cancellationToken = default)
    {
        if (!ManagedAppServerEnvironment.IsManaged)
            return;

        var apiBaseUrl = Environment.GetEnvironmentVariable(ManagedAppServerEnvironment.HubApiBaseUrl);
        var token = Environment.GetEnvironmentVariable(ManagedAppServerEnvironment.HubToken);
        if (string.IsNullOrWhiteSpace(apiBaseUrl) || string.IsNullOrWhiteSpace(token))
            return;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{apiBaseUrl.TrimEnd('/')}/v1/notifications/request")
            {
                Content = JsonContent.Create(new
                {
                    workspacePath,
                    kind,
                    title,
                    body,
                    severity,
                    source = "appserver",
                    threadId,
                    actionUrl,
                    openDesktopOnClick
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, cancellationToken);
        }
        catch
        {
            // Notifications must never affect turn completion or AppServer lifetime.
        }
    }
}
