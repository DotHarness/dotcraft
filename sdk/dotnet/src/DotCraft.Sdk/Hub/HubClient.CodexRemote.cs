using System.Net.Http.Json;
using System.Text.Json;
using DotCraft.Sdk.Wire;

namespace DotCraft.Sdk.Hub;

public sealed partial class HubClient
{
    public async Task<HubCodexRemoteStatus> GetCodexRemoteAsync(CancellationToken cancellationToken = default)
    {
        var hub = await EnsureHubAsync(cancellationToken);
        using var http = CreateHttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{hub.ApiBaseUrl.TrimEnd('/')}/v1/codex-remote");
        ApplyAuthorization(request, hub);
        using var response = await http.Client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await ToClientExceptionAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<HubCodexRemoteStatus>(DotCraftJson.Options, cancellationToken)
               ?? throw new HubClientException("hubInvalidResponse", "Hub returned an empty Codex gateway response.");
    }

    public Task<HubCodexRemoteStatus> EnableCodexRemoteAsync(CancellationToken cancellationToken = default) =>
        PostCodexRemoteAsync<HubCodexRemoteStatus>("/enable", cancellationToken);

    public Task<HubCodexRemoteStatus> DisableCodexRemoteAsync(CancellationToken cancellationToken = default) =>
        PostCodexRemoteAsync<HubCodexRemoteStatus>("/disable", cancellationToken);

    public Task<HubCodexRemotePairing> CreateCodexRemotePairingAsync(CancellationToken cancellationToken = default) =>
        PostCodexRemoteAsync<HubCodexRemotePairing>("/pairings", cancellationToken);

    private async Task<T> PostCodexRemoteAsync<T>(string action, CancellationToken cancellationToken)
    {
        var hub = await EnsureHubAsync(cancellationToken);
        var result = await PostJsonAsync(hub, "/v1/codex-remote" + action, new { }, cancellationToken);
        return result.Deserialize<T>(DotCraftJson.Options)
               ?? throw new HubClientException("hubInvalidResponse", "Hub returned an empty Codex gateway response.");
    }
}
