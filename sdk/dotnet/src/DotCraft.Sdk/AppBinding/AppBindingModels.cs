using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Sdk;
using DotCraft.Sdk.Wire;
using ContractAppBinding = DotCraft.Protocol.AppServer.AppBinding;

namespace DotCraft.Sdk.AppBinding;

/// <summary>Standard App Binding error codes for runtime dynamic tool results.</summary>
public static class AppBindingErrorCodes
{
    public const string Offline = "AppBindingOffline";
    public const string Expired = "AppBindingExpired";
    public const string Revoked = "AppBindingRevoked";
    public const string ScopeDenied = "AppBindingScopeDenied";
    public const string ToolUnavailable = "AppBindingToolUnavailable";
    public const string ProtocolViolation = "AppBindingProtocolViolation";
}

/// <summary>Parsed native app handoff URL.</summary>
public sealed record AppBindingHandoff(
    string Scheme,
    string Operation,
    string AppId,
    string RequestId,
    string RequestToken,
    string? AppServerUrl,
    string? WorkspacePath = null,
    string? AppServerIdentity = null)
{
    /// <summary>Parses and validates a native app handoff URL.</summary>
    public static AppBindingHandoff Parse(string url, string? expectedScheme = null, string? expectedAppId = null)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new FormatException("Invalid App Binding handoff URL.");
        if (!string.IsNullOrWhiteSpace(expectedScheme) &&
            !string.Equals(uri.Scheme, expectedScheme, StringComparison.OrdinalIgnoreCase))
            throw new FormatException($"Expected handoff scheme '{expectedScheme}', got '{uri.Scheme}'.");

        var query = ParseQuery(uri.Query);
        var appId = Get(query, "app") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(expectedAppId) && !string.Equals(appId, expectedAppId, StringComparison.Ordinal))
            throw new FormatException($"Unexpected App Binding app id '{appId}'.");
        var requestId = Get(query, "request");
        var requestToken = Get(query, "token");
        if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(requestToken))
            throw new FormatException("The handoff URL is missing request id or token.");

        return new AppBindingHandoff(
            uri.Scheme,
            uri.AbsolutePath.Trim('/').ToLowerInvariant(),
            appId,
            requestId,
            requestToken,
            Get(query, "endpoint"),
            Get(query, "workspace"),
            Get(query, "identity"));
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split('=', 2);
            result[Uri.UnescapeDataString(pieces[0])] = pieces.Length == 2
                ? Uri.UnescapeDataString(pieces[1])
                : string.Empty;
        }
        return result;
    }

    private static string? Get(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : null;
}

/// <summary>Typed App Binding client helpers backed by the generated RPC bindings.</summary>
public sealed class DotCraftAppBindingClient(DotCraftClient client)
{
    public Task<AppConnectionRequestGetResult> GetConnectionRequestAsync(
        AppConnectionRequestGetParams parameters,
        CancellationToken cancellationToken = default) =>
        client.Wire.AppConnectionRequestGetAsync(parameters, cancellationToken);

    public Task<AppConnectionConnectResult> ConnectAsync(
        AppConnectionConnectParams parameters,
        CancellationToken cancellationToken = default) =>
        client.Wire.AppConnectionConnectAsync(parameters, cancellationToken);

    public Task<AppConnectionStatusResult> GetConnectionStatusAsync(
        AppConnectionStatusParams parameters,
        CancellationToken cancellationToken = default) =>
        client.Wire.AppConnectionStatusAsync(parameters, cancellationToken);

    public Task<AppBindingRequestGetResult> GetBindingRequestAsync(
        AppBindingRequestGetParams parameters,
        CancellationToken cancellationToken = default) =>
        client.Wire.AppBindingRequestGetAsync(parameters, cancellationToken);

    public Task<AppListResult> ListAppsAsync(AppListParams parameters, CancellationToken cancellationToken = default) =>
        client.Wire.AppListAsync(parameters, cancellationToken);

    public Task<AppViewResult> ViewAppAsync(AppViewParams parameters, CancellationToken cancellationToken = default) =>
        client.Wire.AppViewAsync(parameters, cancellationToken);

    public Task<AppConnectionStartResult> StartConnectionAsync(
        AppConnectionStartParams parameters,
        CancellationToken cancellationToken = default) =>
        client.Wire.AppConnectionStartAsync(parameters, cancellationToken);

    public Task<AppConnectionRevokeResult> RevokeConnectionAsync(
        AppConnectionRevokeParams parameters,
        CancellationToken cancellationToken = default) =>
        client.Wire.AppConnectionRevokeAsync(parameters, cancellationToken);

    public Task<AppSurface> PublishSurfaceAsync(
        AppSurfacePublishParams parameters,
        CancellationToken cancellationToken = default) =>
        client.Wire.AppSurfacePublishAsync(parameters, cancellationToken);

    public Task<AppSurface> ResolveSurfaceAsync(
        AppSurfaceResolveParams parameters,
        CancellationToken cancellationToken = default) =>
        client.Wire.AppSurfaceResolveAsync(parameters, cancellationToken);

    public Task<ThreadAppBindingEnableResult> EnableBindingAsync(
        ThreadAppBindingEnableParams parameters,
        CancellationToken cancellationToken = default) =>
        client.Wire.ThreadAppBindingsEnableAsync(parameters, cancellationToken);

    public Task<ThreadAppBindingsListResult> ListThreadBindingsAsync(
        ThreadAppBindingsListParams parameters,
        CancellationToken cancellationToken = default) =>
        client.Wire.ThreadAppBindingsListAsync(parameters, cancellationToken);

    public Task<ContractAppBinding> RevokeThreadBindingAsync(
        ThreadAppBindingRevokeParams parameters,
        CancellationToken cancellationToken = default) =>
        client.Wire.ThreadAppBindingsRevokeAsync(parameters, cancellationToken);

    public Task<AppConnectionAuthenticateResult> AuthenticateAsync(
        AppConnectionAuthenticateParams parameters,
        CancellationToken cancellationToken = default) =>
        client.Wire.AppConnectionAuthenticateAsync(parameters, cancellationToken);

    public Task<AppConnectionRefreshResult> RefreshCredentialAsync(CancellationToken cancellationToken = default) =>
        client.Wire.AppConnectionRefreshAsync(new RpcEmpty(), cancellationToken);

    public Task<ContractAppBinding> ActivateAsync(
        AppBindingActivateParams parameters,
        CancellationToken cancellationToken = default) =>
        client.Wire.AppBindingActivateAsync(parameters, cancellationToken);

    public Task<ContractAppBinding> RebindAsync(
        AppBindingRebindParams parameters,
        CancellationToken cancellationToken = default) =>
        client.Wire.AppBindingRebindAsync(parameters, cancellationToken);

    public Task<ContractAppBinding> ConfirmCapabilitiesAsync(
        ThreadAppBindingConfirmCapabilitiesParams parameters,
        CancellationToken cancellationToken = default) =>
        client.Wire.ThreadAppBindingsConfirmCapabilitiesAsync(parameters, cancellationToken);

    /// <summary>Keeps a binding connection alive by draining notifications.</summary>
    public async Task KeepAliveAsync(
        Func<AppServerNotification, CancellationToken, Task>? onNotification = null,
        CancellationToken cancellationToken = default)
    {
        await foreach (var notification in client.ReadNotificationsAsync(cancellationToken).ConfigureAwait(false))
        {
            if (onNotification is not null)
                await onNotification(notification, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Creates a failed dynamic-tool result using a standard App Binding error code.</summary>
    public static DynamicToolCallResult ToolError(string code, string message, System.Text.Json.JsonElement? structuredContent = null) =>
        new()
        {
            Success = false,
            ContentItems = [new DynamicToolContentItem { Type = "text", Text = $"{code}: {message}" }],
            StructuredContent = structuredContent,
            ErrorCode = code,
            ErrorMessage = message
        };
}
