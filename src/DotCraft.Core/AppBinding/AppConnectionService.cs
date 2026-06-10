using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Protocol.AppServer;
using static DotCraft.AppBinding.AppBindingStoreAccessor;

namespace DotCraft.AppBinding;

internal sealed class AppConnectionService(
    AppBindingStoreAccessor stores,
    AppBindingAttachmentRegistry attachments)
{
    public AppConnectionStartResult StartConnection(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        string userId,
        AppConnectionStartParams p)
    {
        if (string.IsNullOrWhiteSpace(p.AppId))
            throw AppServerErrors.InvalidParams("'appId' is required.");

        var entry = FindEnabledApp(catalog, p.AppId);
        if (entry.ManagedRuntime?.RequiresExternalConnection == false)
            throw AppServerErrors.InvalidParams($"Managed app '{p.AppId}' does not use an external connection flow.");

        var token = AppBindingToken.NewToken();
        var requestId = $"appconn_req_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(10);
        var handoff = BuildHandoff(workspaceCraftPath, entry.Descriptor, p.HandoffMode, requestId, token, "connect");

        stores.GetStore(workspaceCraftPath).Update(state =>
        {
            state.ConnectionRequests.Add(new AppConnectionRequestRecord
            {
                ConnectionRequestId = requestId,
                AppId = entry.Descriptor.AppId,
                UserId = userId,
                RequestTokenHash = AppBindingToken.Hash(token),
                CreatedAt = now,
                ExpiresAt = expiresAt
            });
            AddAudit(state, "connection.start", null, null, entry.Descriptor.AppId, userId, null);
            return true;
        });

        return new AppConnectionStartResult
        {
            ConnectionRequestId = requestId,
            AppId = entry.Descriptor.AppId,
            State = AppConnectionStates.Connecting,
            ExpiresAt = expiresAt,
            Handoff = handoff
        };
    }

    public AppConnectionRequestGetResult GetConnectionRequest(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        AppConnectionRequestGetParams p)
    {
        if (string.IsNullOrWhiteSpace(p.AppId))
            throw AppServerErrors.InvalidParams("'appId' is required.");
        if (string.IsNullOrWhiteSpace(p.ConnectionRequestId))
            throw AppServerErrors.InvalidParams("'connectionRequestId' is required.");
        if (string.IsNullOrWhiteSpace(p.RequestToken))
            throw AppServerErrors.InvalidParams("'requestToken' is required.");

        var entry = FindEnabledApp(catalog, p.AppId);
        var state = stores.GetStore(workspaceCraftPath).Snapshot();
        var now = DateTimeOffset.UtcNow;
        var request = state.ConnectionRequests.FirstOrDefault(r =>
            string.Equals(r.ConnectionRequestId, p.ConnectionRequestId, StringComparison.Ordinal));
        if (request == null)
            throw AppServerErrors.InvalidParams($"Connection request '{p.ConnectionRequestId}' was not found.");
        if (!string.Equals(request.AppId, p.AppId, StringComparison.Ordinal))
            throw AppServerErrors.InvalidParams("Connection request appId mismatch.");
        if (request.Consumed)
            throw AppServerErrors.InvalidParams("Connection request token has already been consumed.");
        if (request.ExpiresAt <= now)
            throw AppServerErrors.InvalidParams("Connection request token has expired.");
        if (!AppBindingToken.Matches(p.RequestToken, request.RequestTokenHash))
            throw AppServerErrors.InvalidParams("Connection request token is invalid.");

        return new AppConnectionRequestGetResult
        {
            AppId = entry.Descriptor.AppId,
            ConnectionRequestId = request.ConnectionRequestId,
            DisplayName = entry.Descriptor.DisplayName,
            DeveloperName = entry.Descriptor.DeveloperName,
            WorkspaceLabel = WorkspaceLabel(workspaceCraftPath),
            UserLabel = request.UserId,
            ExpiresAt = request.ExpiresAt
        };
    }

    public AppConnectionStatusWire CompleteConnection(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        AppConnectionConnectParams p)
    {
        if (string.IsNullOrWhiteSpace(p.ConnectionRequestId))
            throw AppServerErrors.InvalidParams("'connectionRequestId' is required.");
        if (string.IsNullOrWhiteSpace(p.RequestToken))
            throw AppServerErrors.InvalidParams("'requestToken' is required.");
        if (string.IsNullOrWhiteSpace(p.AppId))
            throw AppServerErrors.InvalidParams("'appId' is required.");

        _ = FindEnabledApp(catalog, p.AppId);
        var now = DateTimeOffset.UtcNow;
        return stores.GetStore(workspaceCraftPath).Update(state =>
        {
            var request = state.ConnectionRequests.FirstOrDefault(r =>
                string.Equals(r.ConnectionRequestId, p.ConnectionRequestId, StringComparison.Ordinal));
            if (request == null)
                throw AppServerErrors.InvalidParams($"Connection request '{p.ConnectionRequestId}' was not found.");
            if (!string.Equals(request.AppId, p.AppId, StringComparison.Ordinal))
                throw AppServerErrors.InvalidParams("Connection request appId mismatch.");
            if (request.Consumed)
                throw AppServerErrors.InvalidParams("Connection request token has already been consumed.");
            if (request.ExpiresAt <= now)
                throw AppServerErrors.InvalidParams("Connection request token has expired.");
            if (!AppBindingToken.Matches(p.RequestToken, request.RequestTokenHash))
                throw AppServerErrors.InvalidParams("Connection request token is invalid.");

            request.Consumed = true;
            request.State = AppConnectionStates.Connected;

            var connection = FindConnection(state, request.UserId, p.AppId);
            if (connection == null)
            {
                connection = new AppConnectionRecord { AppId = p.AppId, UserId = request.UserId };
                state.Connections.Add(connection);
            }

            connection.State = AppConnectionStates.Connected;
            connection.ConnectedAt = now;
            connection.ExpiresAt = p.ExpiresAt;
            connection.AccountLabel = p.AccountLabel;
            connection.ConnectionProof = p.ConnectionProof?.DeepClone() as JsonObject;
            connection.PublicMetadata = SanitizePublicConnectionMetadata(p.PublicMetadata);
            connection.Diagnostic = null;
            AddAudit(state, "connection.connected", null, null, p.AppId, request.UserId, p.AccountLabel);

            return MapConnectionStatus(connection);
        });
    }

    public AppConnectionStatusWire GetConnectionStatus(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        string userId,
        string appId)
    {
        _ = FindApp(catalog, appId);
        var state = stores.GetStore(workspaceCraftPath).Snapshot();
        return MapConnectionStatus(state, userId, appId);
    }

    public AppConnectionStatusWire RefreshConnectionMetadata(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        AppConnectionMetadataRefreshParams p)
    {
        if (string.IsNullOrWhiteSpace(p.AppId))
            throw AppServerErrors.InvalidParams("'appId' is required.");
        if (p.ConnectionProof == null)
            throw AppServerErrors.InvalidParams("'connectionProof' is required.");

        _ = FindEnabledApp(catalog, p.AppId);

        return stores.GetStore(workspaceCraftPath).Update(state =>
        {
            var connection = state.Connections.FirstOrDefault(candidate =>
                string.Equals(candidate.AppId, p.AppId, StringComparison.Ordinal)
                && candidate.State == AppConnectionStates.Connected
                && ConnectionProofMatches(candidate.ConnectionProof, p.ConnectionProof));
            if (connection == null)
            {
                throw AppServerErrors.InvalidParams(
                    $"No connected '{p.AppId}' connection matches the supplied connection proof.");
            }

            connection.PublicMetadata = SanitizePublicConnectionMetadata(p.PublicMetadata);
            AddAudit(state, "connection.metadata.refreshed", null, null, p.AppId, connection.UserId, null);

            return MapConnectionStatus(connection);
        });
    }

    public AppConnectionStatusWire RevokeConnection(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        string userId,
        AppConnectionRevokeParams p)
    {
        if (string.IsNullOrWhiteSpace(p.AppId))
            throw AppServerErrors.InvalidParams("'appId' is required.");

        _ = FindApp(catalog, p.AppId);
        return stores.GetStore(workspaceCraftPath).Update(state =>
        {
            var connection = FindConnection(state, userId, p.AppId);
            if (connection == null)
            {
                connection = new AppConnectionRecord
                {
                    AppId = p.AppId,
                    UserId = userId,
                    State = AppConnectionStates.NotConnected
                };
                state.Connections.Add(connection);
            }
            else
            {
                connection.State = AppConnectionStates.NotConnected;
                connection.Diagnostic = p.Reason;
                connection.PublicMetadata = null;
            }

            foreach (var binding in state.Bindings.Where(b =>
                         string.Equals(b.AppId, p.AppId, StringComparison.Ordinal)
                         && string.Equals(b.UserId, userId, StringComparison.Ordinal)
                         && b.State == AppBindingStates.Active))
            {
                binding.State = AppBindingStates.Offline;
                binding.LastChangedAt = DateTimeOffset.UtcNow;
                binding.Diagnostic = "The app connection was revoked.";
                attachments.Remove(binding.BindingId);
            }

            AddAudit(state, "connection.revoked", null, null, p.AppId, userId, p.Reason);
            return MapConnectionStatus(connection);
        });
    }

    private static bool ConnectionProofMatches(JsonObject? stored, JsonObject? presented) =>
        stored != null && presented != null && JsonNode.DeepEquals(stored, presented);

    private static AppConnectionStatusWire MapConnectionStatus(AppConnectionRecord? connection, string? appId = null)
    {
        if (connection == null)
        {
            return new AppConnectionStatusWire
            {
                AppId = appId ?? string.Empty,
                State = AppConnectionStates.NotConnected
            };
        }

        var state = connection.State;
        if (state == AppConnectionStates.Connected
            && connection.ExpiresAt is { } expiresAt
            && expiresAt <= DateTimeOffset.UtcNow)
        {
            state = AppConnectionStates.NeedsAuth;
        }

        return new AppConnectionStatusWire
        {
            AppId = connection.AppId,
            State = state,
            ConnectedAt = connection.ConnectedAt,
            ExpiresAt = connection.ExpiresAt,
            AccountLabel = connection.AccountLabel,
            Diagnostic = connection.Diagnostic,
            PublicMetadata = state == AppConnectionStates.Connected
                ? connection.PublicMetadata?.DeepClone() as JsonObject
                : null
        };
    }

    private static AppConnectionStatusWire MapConnectionStatus(
        AppBindingStateDocument state,
        string userId,
        string appId)
    {
        var connection = FindConnection(state, userId, appId);
        var status = MapConnectionStatus(connection, appId);
        if (status.State != AppConnectionStates.NotConnected)
            return status;

        var pending = state.ConnectionRequests
            .Where(request => string.Equals(request.UserId, userId, StringComparison.Ordinal)
                              && string.Equals(request.AppId, appId, StringComparison.Ordinal)
                              && request.State == AppConnectionStates.Connecting
                              && !request.Consumed
                              && request.ExpiresAt > DateTimeOffset.UtcNow)
            .OrderByDescending(request => request.CreatedAt)
            .FirstOrDefault();
        if (pending == null)
            return status;

        return new AppConnectionStatusWire
        {
            AppId = appId,
            State = AppConnectionStates.Connecting,
            ExpiresAt = pending.ExpiresAt
        };
    }

    private static JsonObject? SanitizePublicConnectionMetadata(JsonObject? metadata)
    {
        if (metadata == null)
            return null;

        var result = new JsonObject();
        CopyStringProperty(metadata, result, "displayName", 160);
        CopyStringProperty(metadata, result, "message", 320);
        CopyLoopbackSurfaceEndpoints(metadata, result);
        return result.Count == 0 ? null : result;
    }

    private static void CopyStringProperty(JsonObject source, JsonObject target, string name, int maxLength)
    {
        if (source[name] is not JsonValue value || !value.TryGetValue<string>(out var text))
            return;

        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return;

        target[name] = trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static void CopyLoopbackSurfaceEndpoints(JsonObject source, JsonObject target)
    {
        if (source["surfaceEndpoints"] is not JsonObject endpoints)
            return;

        var accepted = new JsonObject();
        foreach (var (key, node) in endpoints.Take(32))
        {
            if (string.IsNullOrWhiteSpace(key)
                || node is not JsonValue value
                || !value.TryGetValue<string>(out var url)
                || !IsSafeLoopbackEndpoint(url))
            {
                continue;
            }

            accepted[key] = url.Trim();
        }

        if (accepted.Count > 0)
            target["surfaceEndpoints"] = accepted;
    }

    private static bool IsSafeLoopbackEndpoint(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme is not ("http" or "https" or "ws" or "wss"))
            return false;

        return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
               || string.Equals(uri.Host, "127.0.0.1", StringComparison.Ordinal)
               || string.Equals(uri.Host, "::1", StringComparison.Ordinal);
    }

    private static AppHandoffWire BuildHandoff(
        string workspaceCraftPath,
        AppDescriptor descriptor,
        string? preferredMode,
        string requestId,
        string requestToken,
        string operation,
        IReadOnlyList<string>? scopes = null)
    {
        var handoff = descriptor.Connection.HandoffModes.FirstOrDefault(mode =>
                          !string.IsNullOrWhiteSpace(preferredMode)
                          && string.Equals(mode.Mode, preferredMode, StringComparison.Ordinal))
                      ?? descriptor.Connection.HandoffModes.First();
        return new AppHandoffWire
        {
            Mode = handoff.Mode,
            Uri = string.IsNullOrWhiteSpace(handoff.UriTemplate)
                ? null
                : FillTemplate(
                    handoff.UriTemplate!,
                    descriptor.AppId,
                    requestId,
                    requestToken,
                    operation,
                    scopes,
                    ReadAppServerEndpoint(workspaceCraftPath),
                    escapeValues: true)
        };
    }

    private static string FillTemplate(
        string template,
        string appId,
        string requestId,
        string token,
        string operation,
        IReadOnlyList<string>? scopes,
        string endpoint,
        bool escapeValues)
    {
        var joinedScopes = string.Join(",", scopes ?? []);
        return template
            .Replace("{appId}", TemplateValue(appId, escapeValues), StringComparison.Ordinal)
            .Replace("{requestId}", TemplateValue(requestId, escapeValues), StringComparison.Ordinal)
            .Replace("{requestToken}", TemplateValue(token, escapeValues), StringComparison.Ordinal)
            .Replace("{request}", TemplateValue(requestId, escapeValues), StringComparison.Ordinal)
            .Replace("{operation}", TemplateValue(operation, escapeValues), StringComparison.Ordinal)
            .Replace("{endpoint}", TemplateValue(endpoint, escapeValues), StringComparison.Ordinal)
            .Replace("{scopes}", TemplateValue(joinedScopes, escapeValues), StringComparison.Ordinal);
    }

    private static string TemplateValue(string value, bool escapeValue) =>
        escapeValue ? Uri.EscapeDataString(value) : value;

    private static string ReadAppServerEndpoint(string workspaceCraftPath)
    {
        try
        {
            var lockPath = Path.Combine(workspaceCraftPath, "appserver.lock");
            if (!File.Exists(lockPath))
                return string.Empty;

            using var document = JsonDocument.Parse(File.ReadAllText(lockPath));
            if (!document.RootElement.TryGetProperty("endpoints", out var endpoints))
                return string.Empty;
            if (!endpoints.TryGetProperty("appServerWebSocket", out var endpoint))
                return string.Empty;
            return endpoint.GetString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string WorkspaceLabel(string workspaceCraftPath)
    {
        try
        {
            var workspace = Directory.GetParent(Path.GetFullPath(workspaceCraftPath))?.FullName;
            if (string.IsNullOrWhiteSpace(workspace))
                return "Workspace";
            var name = Path.GetFileName(workspace.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrWhiteSpace(name) ? workspace : name;
        }
        catch
        {
            return "Workspace";
        }
    }

    private static void AddAudit(
        AppBindingStateDocument state,
        string @event,
        string? threadId,
        string? bindingId,
        string? appId,
        string? userId,
        string? detail)
    {
        state.Audit.Add(new AppBindingAuditRecord
        {
            Timestamp = DateTimeOffset.UtcNow,
            Event = @event,
            ThreadId = threadId,
            BindingId = bindingId,
            AppId = appId,
            UserId = userId,
            Detail = detail
        });
    }
}
