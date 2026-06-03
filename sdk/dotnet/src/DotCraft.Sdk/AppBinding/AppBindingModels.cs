using System.Text.Json;
using DotCraft.Sdk.AppServer;
using DotCraft.Sdk.Wire;

namespace DotCraft.Sdk.AppBinding;

/// <summary>
/// Standard App Binding error codes for runtime dynamic tool results.
/// </summary>
public static class AppBindingErrorCodes
{
    public const string Offline = "AppBindingOffline";
    public const string Expired = "AppBindingExpired";
    public const string Revoked = "AppBindingRevoked";
    public const string ScopeDenied = "AppBindingScopeDenied";
    public const string ToolUnavailable = "AppBindingToolUnavailable";
    public const string ProtocolViolation = "AppBindingProtocolViolation";
}

/// <summary>
/// Parsed app handoff URL.
/// </summary>
public sealed record AppBindingHandoff(
    string Scheme,
    string Operation,
    string AppId,
    string RequestId,
    string RequestToken,
    string? AppServerUrl)
{
    /// <summary>
    /// Parses a native app handoff URL.
    /// </summary>
    public static AppBindingHandoff Parse(string url, string? expectedScheme = null, string? expectedAppId = null)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new FormatException("Invalid App Binding handoff URL.");
        }

        if (!string.IsNullOrWhiteSpace(expectedScheme) &&
            !string.Equals(uri.Scheme, expectedScheme, StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException($"Expected handoff scheme '{expectedScheme}', got '{uri.Scheme}'.");
        }

        var operation = uri.AbsolutePath.Trim('/').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(operation))
        {
            operation = uri.Host.ToLowerInvariant();
        }

        var query = ParseQuery(uri.Query);
        var appId = FirstNonEmpty(Get(query, "app"), Get(query, "appId")) ?? "";
        if (!string.IsNullOrWhiteSpace(expectedAppId) &&
            !string.Equals(appId, expectedAppId, StringComparison.Ordinal))
        {
            throw new FormatException($"Unexpected App Binding app id '{appId}'.");
        }

        var requestId = FirstNonEmpty(Get(query, "request"), Get(query, "requestId"));
        var requestToken = FirstNonEmpty(Get(query, "token"), Get(query, "requestToken"));
        if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(requestToken))
        {
            throw new FormatException("The handoff URL is missing request id or token.");
        }

        return new AppBindingHandoff(
            uri.Scheme,
            operation,
            appId,
            requestId,
            requestToken,
            FirstNonEmpty(Get(query, "endpoint"), Get(query, "appServer")));
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return result;
        }

        foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split('=', 2);
            var key = Uri.UnescapeDataString(pieces[0]);
            var value = pieces.Length == 2 ? Uri.UnescapeDataString(pieces[1]) : string.Empty;
            result[key] = value;
        }

        return result;
    }

    private static string? Get(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : null;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

/// <summary>
/// App Binding client helpers.
/// </summary>
public sealed class DotCraftAppBindingClient(DotCraftClient client)
{
    /// <summary>
    /// Gets an app connection request.
    /// </summary>
    public Task<T> GetConnectionRequestAsync<T>(
        object request,
        CancellationToken cancellationToken = default) =>
        client.RequestAsync<T>("app/connection/request/get", request, cancellationToken);

    /// <summary>
    /// Completes an app connection request.
    /// </summary>
    public Task<T> ConnectAsync<T>(
        object request,
        CancellationToken cancellationToken = default) =>
        client.RequestAsync<T>("app/connection/connect", request, cancellationToken);

    /// <summary>
    /// Gets app connection status.
    /// </summary>
    public Task<T> GetConnectionStatusAsync<T>(
        object request,
        CancellationToken cancellationToken = default) =>
        client.RequestAsync<T>("app/connection/status", request, cancellationToken);

    /// <summary>
    /// Gets an app binding request.
    /// </summary>
    public Task<T> GetBindingRequestAsync<T>(
        object request,
        CancellationToken cancellationToken = default) =>
        client.RequestAsync<T>("app/binding/request/get", request, cancellationToken);

    /// <summary>
    /// Accepts an app binding request.
    /// </summary>
    public Task<T> AcceptBindingAsync<T>(
        object request,
        CancellationToken cancellationToken = default) =>
        client.RequestAsync<T>("app/binding/accept", request, cancellationToken);

    /// <summary>
    /// Attaches runtime dynamic tools to an accepted binding.
    /// </summary>
    public Task<T> AttachToolsAsync<T>(
        object request,
        CancellationToken cancellationToken = default) =>
        client.RequestAsync<T>("app/binding/attachTools", request, cancellationToken);

    // ---- Typed App Binding surface (parallel to the TypeScript AppBindingManager) ----

    /// <summary>Lists installed/visible apps (app/list).</summary>
    public async Task<IReadOnlyList<AppInfo>> ListAppsAsync(
        string? threadId = null,
        bool includeDisabled = true,
        bool includeCatalog = true,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var result = await client.RequestAsync<AppListResult>(
            "app/list",
            new { includeCatalog, includeDisabled, threadId, forceRefresh },
            cancellationToken);
        return result.Apps ?? [];
    }

    /// <summary>Reads one app (app/view).</summary>
    public async Task<AppInfo> ViewAppAsync(string appId, string? threadId = null, CancellationToken cancellationToken = default)
    {
        var result = await client.RequestAsync<AppViewResult>("app/view", new { appId, threadId }, cancellationToken);
        return result.App ?? throw new InvalidOperationException($"App '{appId}' was not returned by app/view.");
    }

    /// <summary>Starts an app connection (app/connection/start).</summary>
    public Task<AppConnectionStartResult> StartConnectionAsync(
        string appId,
        string? handoffMode = null,
        string? returnTo = null,
        CancellationToken cancellationToken = default) =>
        client.RequestAsync<AppConnectionStartResult>(
            "app/connection/start",
            new { appId, handoffMode, returnTo },
            cancellationToken);

    /// <summary>Completes an app connection handoff (app/connection/connect).</summary>
    public Task<AppConnectionStatus> CompleteConnectionAsync(CompleteConnectionRequest request, CancellationToken cancellationToken = default) =>
        client.RequestAsync<AppConnectionStatus>("app/connection/connect", request, cancellationToken);

    /// <summary>Reads app connection status (app/connection/status).</summary>
    public Task<AppConnectionStatus> GetConnectionStatusAsync(string appId, CancellationToken cancellationToken = default) =>
        client.RequestAsync<AppConnectionStatus>("app/connection/status", new { appId }, cancellationToken);

    /// <summary>Revokes an app connection (app/connection/revoke).</summary>
    public Task<AppConnectionStatus> RevokeConnectionAsync(string appId, string? reason = null, CancellationToken cancellationToken = default) =>
        client.RequestAsync<AppConnectionStatus>("app/connection/revoke", new { appId, reason }, cancellationToken);

    /// <summary>Creates a thread binding request (app/binding/request/create).</summary>
    public Task<AppBindingRequestCreateResult> CreateBindingRequestAsync(
        string threadId,
        string appId,
        IReadOnlyList<string> requestedScopes,
        string source = "sdk",
        IReadOnlyList<string>? requestedTools = null,
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        client.RequestAsync<AppBindingRequestCreateResult>(
            "app/binding/request/create",
            new { threadId, appId, requestedScopes, requestedTools, reason, source },
            cancellationToken);

    /// <summary>Inspects a pending binding request (app/binding/request/get).</summary>
    public Task<AppBindingRequestInfo> GetBindingRequestAsync(
        string appId,
        string bindingRequestId,
        string requestToken,
        CancellationToken cancellationToken = default) =>
        client.RequestAsync<AppBindingRequestInfo>(
            "app/binding/request/get",
            new { appId, bindingRequestId, requestToken },
            cancellationToken);

    /// <summary>Cancels a pending binding request (app/binding/request/cancel).</summary>
    public Task CancelBindingRequestAsync(string bindingRequestId, string? reason = null, CancellationToken cancellationToken = default) =>
        client.RequestAsync("app/binding/request/cancel", new { bindingRequestId, reason }, cancellationToken);

    /// <summary>Accepts a pending binding request (app/binding/accept).</summary>
    public Task<AppBindingAcceptResult> AcceptBindingAsync(AcceptBindingRequest request, CancellationToken cancellationToken = default) =>
        client.RequestAsync<AppBindingAcceptResult>("app/binding/accept", request, cancellationToken);

    /// <summary>Attaches concrete runtime dynamic tools to an accepted binding (app/binding/attachTools).</summary>
    public Task<AppBindingAttachToolsResult> AttachToolsAsync(AttachToolsRequest request, CancellationToken cancellationToken = default) =>
        client.RequestAsync<AppBindingAttachToolsResult>("app/binding/attachTools", request, cancellationToken);

    /// <summary>Lists a thread's app bindings (thread/appBindings/list).</summary>
    public async Task<IReadOnlyList<ThreadAppBinding>> ListThreadBindingsAsync(string threadId, bool includeRevoked = false, CancellationToken cancellationToken = default)
    {
        var result = await client.RequestAsync<ThreadBindingsListResult>(
            "thread/appBindings/list",
            new { threadId, includeRevoked },
            cancellationToken);
        return result.Bindings ?? [];
    }

    /// <summary>Revokes a thread app binding (thread/appBindings/revoke).</summary>
    public Task RevokeThreadBindingAsync(string threadId, string bindingId, string? reason = null, CancellationToken cancellationToken = default) =>
        client.RequestAsync("thread/appBindings/revoke", new { threadId, bindingId, reason }, cancellationToken);

    /// <summary>Refreshes a thread's app bindings (thread/appBindings/refresh).</summary>
    public Task RefreshThreadBindingsAsync(string threadId, string? bindingId = null, CancellationToken cancellationToken = default) =>
        client.RequestAsync("thread/appBindings/refresh", new { threadId, bindingId }, cancellationToken);

    private sealed record AppListResult(IReadOnlyList<AppInfo>? Apps);

    private sealed record AppViewResult(AppInfo? App);

    private sealed record ThreadBindingsListResult(IReadOnlyList<ThreadAppBinding>? Bindings);

    /// <summary>
    /// Keeps a binding connection alive by draining notifications until cancellation or disconnect.
    /// </summary>
    public async Task KeepAliveAsync(
        Func<AppServerNotification, CancellationToken, Task>? onNotification = null,
        CancellationToken cancellationToken = default)
    {
        await foreach (var notification in client.ReadNotificationsAsync(cancellationToken))
        {
            if (onNotification is not null)
            {
                await onNotification(notification, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Creates a failed dynamic tool result using a standard App Binding error code.
    /// </summary>
    public static DynamicToolResult ToolError(string code, string message, object? structuredResult = null) =>
        new(
            false,
            [new ToolContentItem("text", $"{code}: {message}")],
            structuredResult,
            code,
            message);

    /// <summary>
    /// Deserializes raw JSON as an App Binding DTO.
    /// </summary>
    public static T Deserialize<T>(JsonElement value) =>
        value.Deserialize<T>(DotCraftJson.Options)
        ?? throw new InvalidOperationException("App Binding payload could not be deserialized.");
}
