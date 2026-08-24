using System.Globalization;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Sdk.AppBinding;
using ContractAppBinding = DotCraft.Protocol.AppServer.AppBinding;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Services;

namespace DotCraft.Oratorio.Integrations;

/// <summary>Coordinates the application principal and its binding-scoped MCP authorities.</summary>
public sealed class OratorioAppBindingService(
    IDotCraftAppServerClientFactory clientFactory,
    DotCraftStatusService dotCraftStatusService,
    OratorioDotCraftBindingStore bindingStore,
    IConfigurationSecretProtector secretProtector,
    OratorioBindingMcpRuntime mcpRuntime,
    OratorioBoardSurfaceRuntime boardSurfaceRuntime,
    ILogger<OratorioAppBindingService> logger)
{
    public async Task<DotCraftAppBindingStatusResponse> GetConnectionStatusAsync(CancellationToken ct)
    {
        var bridge = await dotCraftStatusService.GetStatusAsync(ct);
        if (!bridge.Connected || string.IsNullOrWhiteSpace(bridge.Endpoint))
            return Status(bridge, false, "notConnected", bridge.Reason ?? bridge.Message);
        if (!bindingStore.TryLoadForWorkspace(bridge.WorkspacePath, out var durable))
            return Status(bridge, true, "notConnected", "Connect Oratorio to DotCraft before enabling it in a thread.");

        try
        {
            await using var client = await clientFactory.ConnectAsync(
                bridge.Endpoint,
                ct,
                secretProtector.Unprotect(durable.ProtectedAppServerToken));
            await AuthenticateAsync(client.AppBindings, durable, ct);
            var status = await client.AppBindings.GetConnectionStatusAsync(
                new AppConnectionStatusParams { AppId = durable.AppId }, ct);
            var state = Require(status.State, "state");
            var expiresAt = status.Principal.IsSet && status.Principal.Value is { } principal && principal.ExpiresAt.IsSet
                ? principal.ExpiresAt.Value
                : (DateTimeOffset?)null;
            return new DotCraftAppBindingStatusResponse(
                durable.AppId, true, bridge.Configured, state == "connected", state,
                bridge.WorkspacePath, bridge.Endpoint, bridge.EndpointSource, durable.AccountLabel,
                null, expiresAt, null,
                state == "connected" ? "DotCraft is connected to Oratorio." : "DotCraft connection is unavailable.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Unable to authenticate the Oratorio App Binding principal.");
            return Status(bridge, true, "notConnected", "The saved Oratorio connection must be renewed.");
        }
    }

    public async Task<OratorioAppBindingInspection> InspectAsync(string handoffUrl, CancellationToken ct)
    {
        var handoff = ParseHandoff(handoffUrl);
        await using var client = await ConnectAsync(handoff, ct);
        if (handoff.Operation == OratorioAppBindingOperations.Connect)
        {
            var request = await client.AppBindings.GetConnectionRequestAsync(new AppConnectionRequestGetParams
            {
                ConnectionRequestId = handoff.RequestId,
                RequestToken = handoff.RequestToken
            }, ct);
            return new(handoff.Operation, request, null);
        }

        await AuthenticateStoredPrincipalAsync(
            client.AppBindings,
            Require(handoff.AppServerIdentity, "appServerIdentity"),
            ct);
        var binding = await client.AppBindings.GetBindingRequestAsync(new AppBindingRequestGetParams
        {
            BindingRequestId = handoff.RequestId,
            RequestToken = handoff.RequestToken
        }, ct);
        return new(handoff.Operation, null, binding);
    }

    public async Task<OratorioAppBindingApprovalResult> ApproveAsync(string handoffUrl, string? surfaceBaseUrl, CancellationToken ct)
    {
        var handoff = ParseHandoff(handoffUrl);
        await using var client = await ConnectAsync(handoff, ct);
        return handoff.Operation == OratorioAppBindingOperations.Connect
            ? await CompleteConnectionAsync(client.AppBindings, handoff, surfaceBaseUrl, ct)
            : await ActivateBindingAsync(client.AppBindings, handoff, surfaceBaseUrl, ct);
    }

    public async Task RebindPersistedAsync(string? surfaceBaseUrl, CancellationToken ct)
    {
        foreach (var durable in bindingStore.LoadAll().Where(item => item.Bindings is { Count: > 0 }))
        {
            try
            {
                await using var client = await ConnectAsync(durable, ct);
                await AuthenticateAsync(client.AppBindings, durable, ct);
                var current = bindingStore.TryLoad(durable.AppServerIdentity, out var refreshed)
                    ? refreshed
                    : durable;
                var listed = await client.AppBindings.ListBindingsAsync(ct);
                var authoritative = Require(listed.Bindings, "bindings");
                var byId = authoritative.ToDictionary(
                    binding => Require(binding.BindingId, "binding.bindingId"),
                    StringComparer.Ordinal);
                var reconciled = new List<OratorioBindingRebindHint>();

                foreach (var hint in current.Bindings ?? [])
                {
                    if (!byId.TryGetValue(hint.BindingId, out var binding))
                    {
                        mcpRuntime.Revoke(hint.BindingId);
                        continue;
                    }

                    var state = Require(binding.State, "binding.state");
                    if (IsTerminalBindingState(state))
                    {
                        mcpRuntime.Revoke(hint.BindingId);
                        continue;
                    }

                    var revision = Require(binding.AuthorityRevision, "binding.authorityRevision");
                    var threadId = Require(binding.ThreadId, "binding.threadId");
                    var authoritativeHint = new OratorioBindingRebindHint(hint.BindingId, threadId, revision);
                    if ((state == "active" && mcpRuntime.HasAuthority(hint.BindingId, revision)) ||
                        state is "connecting" or "syncing")
                    {
                        reconciled.Add(authoritativeHint);
                        continue;
                    }

                    if (!TryBuildMcpEndpoint(surfaceBaseUrl, hint.BindingId, out var endpoint))
                    {
                        reconciled.Add(authoritativeHint);
                        continue;
                    }

                    var bearer = mcpRuntime.Issue(hint.BindingId, revision);
                    try
                    {
                        var rebound = await client.AppBindings.RebindAsync(
                            new AppBindingRebindParams
                            {
                                BindingId = hint.BindingId,
                                AuthorityRevision = revision,
                                Endpoint = endpoint,
                                Bearer = bearer
                            }, ct);
                        var reboundState = Require(rebound.State, "binding.state");
                        if (!IsLiveBindingState(reboundState))
                        {
                            mcpRuntime.Revoke(hint.BindingId);
                            logger.LogWarning(
                                "Oratorio binding {BindingId} rebind ended in state {State} ({FailureReason}).",
                                hint.BindingId,
                                reboundState,
                                rebound.FailureReason.IsSet ? rebound.FailureReason.Value : null);
                            continue;
                        }

                        var reboundRevision = Require(rebound.AuthorityRevision, "binding.authorityRevision");
                        if (!mcpRuntime.Promote(hint.BindingId, bearer, reboundRevision))
                            throw new InvalidOperationException("The Oratorio binding authority changed during rebind.");
                        reconciled.Add(new OratorioBindingRebindHint(hint.BindingId, threadId, reboundRevision));
                    }
                    catch (OperationCanceledException)
                    {
                        mcpRuntime.Revoke(hint.BindingId);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        mcpRuntime.Revoke(hint.BindingId);
                        reconciled.Add(authoritativeHint);
                        logger.LogWarning(ex, "Could not rebind Oratorio authority {BindingId}; it will be retried.", hint.BindingId);
                    }
                }

                bindingStore.Save(current with { Bindings = reconciled });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not rebind Oratorio authorities for AppServer {AppServerIdentity}.", durable.AppServerIdentity);
            }
        }
    }

    /// <summary>Authenticates the saved application principal and republishes its board surface.</summary>
    public async Task PublishSurfacePersistedAsync(string surfaceBaseUrl, CancellationToken ct)
    {
        foreach (var durable in bindingStore.LoadAll())
        {
            try
            {
                await using var client = await ConnectAsync(durable, ct);
                await AuthenticateAsync(client.AppBindings, durable, ct);
                await PublishBoardSurfaceAsync(client.AppBindings, surfaceBaseUrl, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not publish the Oratorio surface for AppServer {AppServerIdentity}.", durable.AppServerIdentity);
            }
        }
    }

    private async Task<OratorioAppBindingApprovalResult> CompleteConnectionAsync(
        DotCraftAppBindingClient appBindings, AppBindingHandoff handoff, string? surfaceBaseUrl, CancellationToken ct)
    {
        var endpoint = BuildBoardSurfaceEndpoint(surfaceBaseUrl);
        var result = await appBindings.ConnectAsync(
            new AppConnectionConnectParams
            {
                ConnectionRequestId = handoff.RequestId,
                RequestToken = handoff.RequestToken,
                AccountLabel = "Oratorio"
            }, ct);
        var principal = Require(result.Principal, "principal");
        var credential = Require(result.Credential, "credential");
        var appServer = SplitAppServerEndpoint(ResolveAppServerUrl(handoff));
        bindingStore.Save(new OratorioDotCraftBinding(
            Require(handoff.AppServerIdentity, "appServerIdentity"), Require(handoff.WorkspacePath, "workspacePath"),
            appServer.Url, handoff.AppId, Require(principal.PrincipalId, "principal.principalId"),
            secretProtector.Protect(credential), Require(principal.ExpiresAt, "principal.expiresAt"), "Oratorio", [],
            string.IsNullOrWhiteSpace(appServer.Token) ? null : secretProtector.Protect(appServer.Token)));
        await appBindings.AuthenticateAsync(new AppConnectionAuthenticateParams
        {
            AppId = handoff.AppId,
            Credential = credential
        }, ct);
        await appBindings.PublishSurfaceAsync(new AppSurfacePublishParams
        {
            SurfaceId = OratorioBoardSurfaceRuntime.SurfaceId,
            Endpoint = endpoint,
            Bearer = boardSurfaceRuntime.Bearer
        }, ct);
        return new(handoff.Operation, "connected", null);
    }

    private async Task<OratorioAppBindingApprovalResult> ActivateBindingAsync(
        DotCraftAppBindingClient appBindings, AppBindingHandoff handoff, string? surfaceBaseUrl, CancellationToken ct)
    {
        var durable = await AuthenticateStoredPrincipalAsync(
            appBindings,
            Require(handoff.AppServerIdentity, "appServerIdentity"),
            ct);
        var request = await appBindings.GetBindingRequestAsync(new AppBindingRequestGetParams
        {
            BindingRequestId = handoff.RequestId,
            RequestToken = handoff.RequestToken
        }, ct);
        var activationKey = Require(request.BindingId, "bindingId");
        if (!TryBuildMcpEndpoint(surfaceBaseUrl, activationKey, out var endpoint))
            throw OratorioApiException.Validation("Oratorio must expose a loopback HTTP endpoint for App Binding.");

        const long initialRevision = 1;
        var bearer = mcpRuntime.Issue(activationKey, initialRevision);
        try
        {
            var activated = await appBindings.ActivateAsync(new AppBindingActivateParams
            {
                BindingRequestId = handoff.RequestId,
                Endpoint = endpoint,
                Bearer = bearer
            }, ct);

            var bindingId = Require(activated.BindingId, "bindingId");
            var revision = Require(activated.AuthorityRevision, "authorityRevision");
            var state = Require(activated.State, "state");
            if (!string.Equals(bindingId, activationKey, StringComparison.Ordinal) ||
                !IsLiveBindingState(state) ||
                !mcpRuntime.Promote(activationKey, bearer, revision))
            {
                var failureReason = activated.FailureReason.IsSet ? activated.FailureReason.Value : null;
                var suffix = string.IsNullOrWhiteSpace(failureReason) ? string.Empty : $" ({failureReason})";
                throw OratorioApiException.Validation($"DotCraft App Binding activation ended in state '{state}'{suffix}.");
            }

            var hints = (durable.Bindings ?? [])
                .Where(item => !string.Equals(item.BindingId, bindingId, StringComparison.Ordinal))
                .Append(new OratorioBindingRebindHint(bindingId, Require(request.ThreadId, "threadId"), revision))
                .ToArray();
            bindingStore.Save(durable with { Bindings = hints });
            return new OratorioAppBindingApprovalResult(handoff.Operation, state, bindingId);
        }
        catch
        {
            mcpRuntime.Revoke(activationKey);
            throw;
        }
    }

    private async Task<OratorioDotCraftBinding> AuthenticateStoredPrincipalAsync(
        DotCraftAppBindingClient appBindings, string appServerIdentity, CancellationToken ct)
    {
        if (!bindingStore.TryLoad(appServerIdentity, out var durable))
            throw OratorioApiException.Validation("Connect Oratorio to DotCraft before accepting a thread binding.");
        await AuthenticateAsync(appBindings, durable, ct);
        return bindingStore.TryLoad(appServerIdentity, out var refreshed) ? refreshed : durable;
    }

    private async Task AuthenticateAsync(
        DotCraftAppBindingClient appBindings, OratorioDotCraftBinding durable, CancellationToken ct)
    {
        var credential = secretProtector.Unprotect(durable.ProtectedCredential);
        if (string.IsNullOrWhiteSpace(credential))
            throw OratorioApiException.Validation("The saved DotCraft principal credential is unavailable.");
        await appBindings.AuthenticateAsync(new AppConnectionAuthenticateParams
        {
            AppId = durable.AppId,
            Credential = credential
        }, ct);
        if (durable.PrincipalExpiresAt <= DateTimeOffset.UtcNow.AddDays(7))
        {
            var refreshed = await appBindings.RefreshCredentialAsync(ct);
            var principal = Require(refreshed.Principal, "principal");
            bindingStore.Save(durable with
            {
                PrincipalId = Require(principal.PrincipalId, "principal.principalId"),
                ProtectedCredential = secretProtector.Protect(Require(refreshed.Credential, "credential")),
                PrincipalExpiresAt = Require(principal.ExpiresAt, "principal.expiresAt")
            });
        }
    }

    private async Task<IDotCraftAppServerClient> ConnectAsync(AppBindingHandoff handoff, CancellationToken ct)
    {
        var endpoint = SplitAppServerEndpoint(ResolveAppServerUrl(handoff));
        var client = await clientFactory.ConnectAsync(endpoint.Url, ct, endpoint.Token);
        return client;
    }

    private Task<IDotCraftAppServerClient> ConnectAsync(OratorioDotCraftBinding binding, CancellationToken ct) =>
        clientFactory.ConnectAsync(
            binding.AppServerUrl,
            ct,
            secretProtector.Unprotect(binding.ProtectedAppServerToken));

    private static string ResolveAppServerUrl(AppBindingHandoff handoff) => handoff.AppServerUrl!;

    private static AppServerEndpointParts SplitAppServerEndpoint(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            throw OratorioApiException.Validation("The handoff AppServer endpoint is invalid.");

        string? token = null;
        var retained = new List<string>();
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var name = Uri.UnescapeDataString(separator < 0 ? pair : pair[..separator]);
            if (string.Equals(name, "token", StringComparison.OrdinalIgnoreCase))
            {
                token = Uri.UnescapeDataString(separator < 0 ? string.Empty : pair[(separator + 1)..]);
            }
            else
            {
                retained.Add(pair);
            }
        }

        var builder = new UriBuilder(uri) { Query = string.Join('&', retained) };
        return new AppServerEndpointParts(builder.Uri.AbsoluteUri, token);
    }

    private sealed record AppServerEndpointParts(string Url, string? Token);

    private async Task PublishBoardSurfaceAsync(
        DotCraftAppBindingClient appBindings, string surfaceBaseUrl, CancellationToken ct)
    {
        await appBindings.PublishSurfaceAsync(new AppSurfacePublishParams
        {
            SurfaceId = OratorioBoardSurfaceRuntime.SurfaceId,
            Endpoint = BuildBoardSurfaceEndpoint(surfaceBaseUrl),
            Bearer = boardSurfaceRuntime.Bearer
        }, ct);
    }

    private string BuildBoardSurfaceEndpoint(string? surfaceBaseUrl)
    {
        try
        {
            return boardSurfaceRuntime.BuildEndpoint(surfaceBaseUrl ?? string.Empty);
        }
        catch (ArgumentException)
        {
            throw OratorioApiException.Validation("Oratorio must expose a loopback HTTP endpoint for the board surface.");
        }
    }

    private static bool TryBuildMcpEndpoint(string? surfaceBaseUrl, string bindingId, out string endpoint)
    {
        endpoint = string.Empty;
        if (string.IsNullOrWhiteSpace(surfaceBaseUrl) || !Uri.TryCreate(surfaceBaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp || !uri.IsLoopback) return false;
        endpoint = $"{uri.GetLeftPart(UriPartial.Authority).TrimEnd('/')}/dotcraft/bindings/{Uri.EscapeDataString(bindingId)}/mcp";
        return true;
    }

    private static AppBindingHandoff ParseHandoff(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, "dotcraft", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).Length != 1)
        {
            throw OratorioApiException.Validation("Invalid App Binding handoff URL.");
        }

        AppBindingHandoff handoff;
        try
        {
            handoff = AppBindingHandoff.Parse(
                url,
                expectedScheme: "oratorio",
                expectedAppId: OratorioBindingMcpCatalog.AppId);
        }
        catch (FormatException ex)
        {
            throw OratorioApiException.Validation(ex.Message);
        }

        if (handoff.Operation is not (OratorioAppBindingOperations.Connect or OratorioAppBindingOperations.Bind))
            throw OratorioApiException.Validation($"Unsupported App Binding operation '{handoff.Operation}'.");
        if (string.IsNullOrWhiteSpace(handoff.AppServerUrl))
            throw OratorioApiException.Validation("The handoff URL is missing the AppServer endpoint.");
        if (string.IsNullOrWhiteSpace(handoff.WorkspacePath) || string.IsNullOrWhiteSpace(handoff.AppServerIdentity))
            throw OratorioApiException.Validation("The handoff URL is missing its Workspace runtime identity.");
        ValidateRuntimeIdentity(handoff.WorkspacePath, handoff.AppServerIdentity);
        return handoff;
    }

    internal static void ValidateRuntimeIdentity(string workspacePath, string appServerIdentity)
    {
        if (appServerIdentity.StartsWith("remote:", StringComparison.Ordinal))
        {
            var suffix = $":{workspacePath}";
            var segments = appServerIdentity.Split(':', 4, StringSplitOptions.None);
            if (segments.Length == 4
                && !string.IsNullOrWhiteSpace(segments[1])
                && !string.IsNullOrWhiteSpace(segments[2])
                && appServerIdentity.EndsWith(suffix, StringComparison.Ordinal))
                return;
            throw OratorioApiException.Validation("The remote AppServer identity does not match its Workspace.");
        }

        string expected;
        try
        {
            expected = $"local:{Path.GetFullPath(workspacePath)}";
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw OratorioApiException.Validation("The handoff Workspace path is invalid.");
        }
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(expected, appServerIdentity, comparison))
            throw OratorioApiException.Validation("The handoff AppServer identity does not match its Workspace.");
    }

    private static string Require(string? value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw OratorioApiException.Validation($"DotCraft response is missing '{field}'.")
            : value;

    private static DateTimeOffset RequireTimestamp(string value) =>
        ParseTimestamp(value) ?? throw OratorioApiException.Validation("DotCraft returned an invalid App Binding expiry timestamp.");

    private static T Require<T>(Optional<T> optional, string field)
    {
        if (!optional.IsSet || optional.Value is null)
            throw OratorioApiException.Validation($"DotCraft response is missing '{field}'.");
        return optional.Value;
    }

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static bool IsLiveBindingState(string state) =>
        state is "active" or "needsConfirmation";

    private static bool IsTerminalBindingState(string state) =>
        state is "failed" or "cancelled" or "revoked";

    private static DotCraftAppBindingStatusResponse Status(DotCraftStatusResponse bridge, bool available, string state, string? message) =>
        new(OratorioBindingMcpCatalog.AppId, available, bridge.Configured, false, state,
            bridge.WorkspacePath, bridge.Endpoint, bridge.EndpointSource, null, null, null, state, message ?? "DotCraft connection is unavailable.");
}

public static class OratorioAppBindingOperations
{
    public const string Connect = "connect";
    public const string Bind = "bind";
}

public sealed record OratorioAppBindingInspection(
    string Operation,
    AppConnectionRequestGetResult? Connection,
    AppBindingRequestGetResult? Binding);
public sealed record OratorioAppBindingApprovalResult(string Operation, string State, string? BindingId);
