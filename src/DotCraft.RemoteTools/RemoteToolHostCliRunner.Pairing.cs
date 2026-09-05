using System.IO.Pipes;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Principal;
using System.Text.Json;
using DotCraft.Tools;

namespace DotCraft.RemoteTools;

internal static partial class RemoteToolHostCliRunner
{
    private const string SatellitePipePrefix = "DotCraft.Satellite.";

    internal static async Task<int> InviteAsync(
        string? name,
        string? host,
        int? expiresHours,
        bool json,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        using var hub = new HubCliClient();
        var response = await hub.PostAsync<CreatedInvite>(
            "/v1/satellites/invites",
            new { name, host, ttlHours = expiresHours },
            cancellationToken).ConfigureAwait(false);
        if (json)
        {
            await WriteJsonAsync(output, response).ConfigureAwait(false);
            return 0;
        }

        await output.WriteLineAsync(response.Url).ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Single use, valid until {response.ExpiresAt.ToLocalTime():yyyy-MM-dd HH:mm}.")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            "The other machine runs: dotcraft tool-host join " + response.Url).ConfigureAwait(false);
        return 0;
    }

    internal static async Task<int> JoinAsync(
        string inviteUrl,
        string? workspacePath,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        if (await TryForwardToTrayAsync(inviteUrl, cancellationToken).ConfigureAwait(false))
        {
            await output.WriteLineAsync(
                "The DotCraft tray client is running; it will show the invitation on this machine.")
                .ConfigureAwait(false);
            return 0;
        }

        var invite = RemoteToolHostRuntime.ParseInvite(inviteUrl);
        if (string.IsNullOrWhiteSpace(workspacePath))
            invite = await RemoteToolHostRuntime.ResolveInviteAsync(invite, cancellationToken).ConfigureAwait(false);
        var folder = string.IsNullOrWhiteSpace(workspacePath)
            ? invite.SuggestedWorkspacePath
            : workspacePath.Trim();
        if (string.IsNullOrWhiteSpace(folder))
            throw new ArgumentException(
                "The invitation proposes no folder. Pass --workspace <absolute-path> to choose one.");

        await using var runtime = RemoteToolHostRuntime.Create();
        var peer = await runtime.AcceptInviteAsync(new RemoteToolJoinDecision(invite, folder), cancellationToken)
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Joined {peer.DisplayName} as {peer.PeerId}.").ConfigureAwait(false);
        await output.WriteLineAsync($"Sharing {peer.WorkspacePath} as workspace '{peer.WorkspaceId}'.")
            .ConfigureAwait(false);
        await output.WriteLineAsync("Run 'dotcraft tool-host serve' to stay available.").ConfigureAwait(false);
        return 0;
    }

    internal static async Task<int> RevokeAsync(
        string id,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var storage = new RemoteToolHostStorage();
        var local = storage.LoadHostState()?.Peers.Any(
            peer => string.Equals(peer.PeerId, id, StringComparison.Ordinal)) == true;
        if (local)
        {
            await using var runtime = RemoteToolHostRuntime.Create();
            await runtime.RevokeAsync(id).ConfigureAwait(false);
            await output.WriteLineAsync($"Removed the pairing with {id} on this machine.").ConfigureAwait(false);
            return 0;
        }

        using var hub = new HubCliClient();
        if (!await hub.DeleteAsync($"/v1/satellites/{Uri.EscapeDataString(id)}", cancellationToken)
                .ConfigureAwait(false))
        {
            await output.WriteLineAsync($"No pairing with id '{id}' on this machine or its Hub.")
                .ConfigureAwait(false);
            return 1;
        }
        await output.WriteLineAsync($"Revoked {id}.").ConfigureAwait(false);
        return 0;
    }

    internal static async Task<int> ListAsync(
        bool json,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var directory = new HubRemoteToolHostDirectory(new HubEndpointProvider());
        try
        {
            var hosts = await directory.ListAsync(cancellationToken).ConfigureAwait(false);
            if (json)
            {
                await WriteJsonAsync(output, hosts).ConfigureAwait(false);
                return 0;
            }

            foreach (var host in hosts)
            {
                await output.WriteLineAsync(
                    $"{host.HostId}\t{host.DisplayName}\t{(host.Online ? "online" : "offline")}")
                    .ConfigureAwait(false);
                foreach (var workspace in host.Workspaces)
                    await output.WriteLineAsync(
                        $"  {workspace.WorkspaceId}\t{workspace.DisplayName}\t{(workspace.Available ? "available" : "busy")}")
                        .ConfigureAwait(false);
            }
            return 0;
        }
        finally
        {
            directory.Dispose();
        }
    }

    internal static async Task<int> TestAsync(
        string hostId,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var directory = new HubRemoteToolHostDirectory(new HubEndpointProvider());
        try
        {
            var hosts = await directory.ListAsync(cancellationToken).ConfigureAwait(false);
            var host = hosts.FirstOrDefault(item => string.Equals(item.HostId, hostId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Machine '{hostId}' is not paired with this Hub.");
            await output.WriteLineAsync(host.Online
                ? $"{host.DisplayName} is online; {host.Workspaces.Count} workspace(s)."
                : $"{host.DisplayName} is offline.").ConfigureAwait(false);
            return host.Online ? 0 : 1;
        }
        finally
        {
            directory.Dispose();
        }
    }

    /// <summary>The tray client owns the consent window, so a running instance handles the invitation.</summary>
    private static async Task<bool> TryForwardToTrayAsync(string inviteUrl, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            return false;
        string pipeName;
        try
        {
            pipeName = SatellitePipePrefix + WindowsIdentity.GetCurrent().User?.Value;
        }
        catch (Exception)
        {
            return false;
        }

        try
        {
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            await pipe.ConnectAsync(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(false);
            await using var writer = new StreamWriter(pipe) { AutoFlush = true };
            // The tray client reads one line per message, so this payload must not be indented.
            await writer.WriteLineAsync(
                JsonSerializer.Serialize(new { kind = "join", url = inviteUrl }, JsonSerializerOptions.Web))
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private sealed record CreatedInvite(string InviteId, string Url, DateTimeOffset ExpiresAt);

    private sealed class HubCliClient : IDisposable
    {
        private readonly HttpClient _http = new();
        private readonly HubEndpoint _hub;

        public HubCliClient()
        {
            _hub = new HubEndpointProvider().TryResolve()
                ?? throw new RemoteToolHostException(
                    RemoteToolErrorCodes.HubUnavailable,
                    "No local Hub was found. Start DotCraft Hub with 'dotcraft hub' first.");
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _hub.Token);
        }

        public async Task<T> PostAsync<T>(string path, object body, CancellationToken cancellationToken)
        {
            using var response = await _http.PostAsJsonAsync(
                new Uri(_hub.BaseUrl, path),
                body,
                RemoteToolHostProtocol.JsonOptions,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(await DescribeAsync(response, cancellationToken)
                    .ConfigureAwait(false));
            return await response.Content
                .ReadFromJsonAsync<T>(RemoteToolHostProtocol.JsonOptions, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("The Hub returned an empty response.");
        }

        public async Task<bool> DeleteAsync(string path, CancellationToken cancellationToken)
        {
            using var response = await _http.DeleteAsync(new Uri(_hub.BaseUrl, path), cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return false;
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(await DescribeAsync(response, cancellationToken)
                    .ConfigureAwait(false));
            return true;
        }

        public void Dispose() => _http.Dispose();

        private static async Task<string> DescribeAsync(
            HttpResponseMessage response,
            CancellationToken cancellationToken)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("error", out var error)
                    && error.TryGetProperty("message", out var message))
                {
                    return message.GetString() ?? body;
                }
            }
            catch (JsonException)
            {
                // Fall through to the raw body.
            }
            return $"The Hub returned {(int)response.StatusCode}. {body}";
        }
    }
}
