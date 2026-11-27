using System.Net.Http.Headers;
using System.Net.Http.Json;
using DotCraft.Common;
using SdkHub = DotCraft.Sdk.Hub;

namespace DotCraft.Hub;

public sealed class HubClient
{
    private readonly HubPaths _paths;
    private readonly SdkHub.HubClient _client;
    private readonly string _executable;

    public HubClient(string? dotcraftBin = null, HubPaths? paths = null)
    {
        _paths = paths ?? HubPaths.ForCurrentUser();
        _executable = string.IsNullOrWhiteSpace(dotcraftBin) ? HubLauncher.CurrentBinary() : dotcraftBin;
        _client = new SdkHub.HubClient(new SdkHub.DotCraftHubClientOptions
        {
            Executable = _executable,
            HubLockPath = _paths.LockFilePath,
            StartHubProcess = value =>
            {
                HubLauncher.StartLauncher(value, _paths);
                return null;
            }
        });
    }

    public Task<HubAppServerResponse> EnsureAppServerAsync(
        string workspacePath,
        string clientName,
        CancellationToken cancellationToken = default) =>
        ForwardAsync(() => _client.EnsureAppServerAsync(workspacePath, new SdkHub.HubEnsureAppServerOptions
        {
            Client = new SdkHub.HubClientInfo { Name = clientName, Version = AppVersion.Informational }
        }, cancellationToken));

    public Task<HubAppServerResponse> EnsureDefaultChatAppServerAsync(
        string clientName,
        CancellationToken cancellationToken = default) =>
        EnsureAppServerAsync(DefaultChatWorkspace.Ensure(_paths), clientName, cancellationToken);

    public Task<HubAppServerResponse> RestartAppServerAsync(
        string workspacePath,
        CancellationToken cancellationToken = default) =>
        ForwardAsync(() => _client.RestartAppServerAsync(workspacePath, cancellationToken: cancellationToken));

    public async Task<HubManagedServiceResponse> EnsureManagedServiceAsync(
        string serviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        try
        {
            var hub = await _client.EnsureHubAsync(cancellationToken).ConfigureAwait(false);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{hub.ApiBaseUrl.TrimEnd('/')}/v1/services/ensure")
            {
                Content = JsonContent.Create(
                    new EnsureManagedServiceRequest
                    {
                        ServiceId = serviceId,
                        StartIfMissing = true,
                        Executable = _executable
                    },
                    options: HubJson.Options)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hub.Token);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new HubClientException(
                    "managedServiceUnavailable",
                    string.IsNullOrWhiteSpace(detail)
                        ? $"Hub returned {(int)response.StatusCode} while ensuring service '{serviceId}'."
                        : detail);
            }

            var service = await response.Content
                .ReadFromJsonAsync<HubManagedServiceResponse>(HubJson.Options, cancellationToken)
                .ConfigureAwait(false);
            return service ?? throw new HubClientException("managedServiceUnavailable", $"Hub returned an empty response for service '{serviceId}'.");
        }
        catch (SdkHub.HubClientException error)
        {
            throw new HubClientException(error.Code, error.Message, error);
        }
    }

    private static async Task<HubAppServerResponse> ForwardAsync(Func<Task<SdkHub.HubAppServerResponse>> operation)
    {
        try
        {
            var response = await operation().ConfigureAwait(false);
            return new HubAppServerResponse(
                response.WorkspacePath,
                response.CanonicalWorkspacePath,
                response.State,
                response.Pid,
                response.Endpoints,
                response.ServiceStatus.ToDictionary(pair => pair.Key,
                    pair => new HubServiceStatus(pair.Value.State, pair.Value.Url, pair.Value.Reason)),
                response.ServerVersion,
                response.StartedByHub,
                response.ExitCode,
                response.LastError,
                response.RecentStderr);
        }
        catch (SdkHub.HubClientException error)
        {
            throw new HubClientException(error.Code, error.Message, error);
        }
    }
}

public sealed class HubClientException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
