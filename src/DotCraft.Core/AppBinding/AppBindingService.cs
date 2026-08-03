using System.Collections.Concurrent;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.AppServer;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;

namespace DotCraft.AppBinding;

/// <summary>Owns App Binding durable authority, principal credentials, and state transitions.</summary>
public sealed class AppBindingService
{
    private static readonly TimeSpan SurfaceLeaseLifetime = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, AppBindingStateStore> _stores =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, AppSurfaceLease>> _surfaces =
        new(StringComparer.OrdinalIgnoreCase);

    internal AppBindingStateStore Store(string workspaceCraftPath) =>
        _stores.GetOrAdd(Path.GetFullPath(workspaceCraftPath), static path => new(path));

    internal AppConnectionStartOutcome StartConnection(
        string workspaceCraftPath,
        string appId,
        string userId)
    {
        Require(appId, "appId");
        var token = AppBindingSecrets.NewSecret();
        var now = DateTimeOffset.UtcNow;
        var record = new AppConnectionRequestRecord
        {
            ConnectionRequestId = $"appconn_{Guid.NewGuid():N}",
            AppId = appId.Trim(),
            UserId = NormalizeUser(userId),
            RequestTokenHash = AppBindingSecrets.HashRequestToken(token),
            CreatedAt = now,
            ExpiresAt = now.Add(AppBindingContract.HandoffLifetime)
        };
        Store(workspaceCraftPath).Update(state =>
        {
            state.ConnectionRequests.RemoveAll(candidate => candidate.ExpiresAt <= now || candidate.Consumed);
            state.ConnectionRequests.Add(record);
            Audit(state, "connection.requested", record.UserId, record.AppId);
            return 0;
        });
        return new()
        {
            ConnectionRequestId = record.ConnectionRequestId,
            RequestToken = token,
            ExpiresAt = record.ExpiresAt
        };
    }

    internal AppConnectionRequestRecord GetConnectionRequest(
        string workspaceCraftPath,
        AppConnectionRequestQuery parameters)
    {
        Require(parameters.ConnectionRequestId, "connectionRequestId");
        Require(parameters.RequestToken, "requestToken");
        var record = Store(workspaceCraftPath).Snapshot().ConnectionRequests.FirstOrDefault(candidate =>
            string.Equals(candidate.ConnectionRequestId, parameters.ConnectionRequestId, StringComparison.Ordinal));
        ValidateRequest(record, parameters.RequestToken);
        return record!;
    }

    internal AppConnectionConnectOutcome Connect(
        string workspaceCraftPath,
        AppConnectionConnectCommand parameters)
    {
        Require(parameters.ConnectionRequestId, "connectionRequestId");
        Require(parameters.RequestToken, "requestToken");
        var credential = AppBindingSecrets.NewSecret();
        var verifier = AppBindingSecrets.CreateVerifier(credential);
        var now = DateTimeOffset.UtcNow;
        return Store(workspaceCraftPath).Update(state =>
        {
            var request = state.ConnectionRequests.FirstOrDefault(candidate =>
                string.Equals(candidate.ConnectionRequestId, parameters.ConnectionRequestId, StringComparison.Ordinal));
            ValidateRequest(request, parameters.RequestToken);
            request!.Consumed = true;

            foreach (var existing in state.Principals.Where(candidate =>
                         string.Equals(candidate.AppId, request.AppId, StringComparison.Ordinal)
                         && string.Equals(candidate.UserId, request.UserId, StringComparison.Ordinal)
                         && candidate.RevokedAt == null))
            {
                existing.RevokedAt = now;
            }

            var principal = new AppPrincipalRecord
            {
                PrincipalId = $"apppr_{Guid.NewGuid():N}",
                AppId = request.AppId,
                UserId = request.UserId,
                CredentialSalt = verifier.Salt,
                CredentialVerifier = verifier.Verifier,
                ExpiresAt = now.Add(AppBindingContract.PrincipalCredentialLifetime),
                AccountLabel = string.IsNullOrWhiteSpace(parameters.AccountLabel)
                    ? null
                    : parameters.AccountLabel.Trim()
            };
            state.Principals.Add(principal);
            Audit(state, "connection.connected", principal.UserId, principal.AppId, principalId: principal.PrincipalId);
            return new AppConnectionConnectOutcome
            {
                Principal = ToWire(principal),
                Credential = credential
            };
        });
    }

    internal AppPrincipalSnapshot Authenticate(
        string workspaceCraftPath,
        string appId,
        string credential)
    {
        Require(appId, "appId");
        Require(credential, "credential");
        var now = DateTimeOffset.UtcNow;
        var principal = Store(workspaceCraftPath).Snapshot().Principals.FirstOrDefault(candidate =>
            string.Equals(candidate.AppId, appId, StringComparison.Ordinal)
            && candidate.RevokedAt == null
            && candidate.ExpiresAt > now
            && AppBindingSecrets.Verify(credential, candidate.CredentialSalt, candidate.CredentialVerifier));
        if (principal == null)
            throw AppServerErrors.AppPrincipalUnauthorized("The app credential is invalid, expired, or revoked.");
        return ToWire(principal);
    }

    internal AppPrincipalSnapshot? GetActivePrincipal(string workspaceCraftPath, string appId)
    {
        var now = DateTimeOffset.UtcNow;
        var principal = Store(workspaceCraftPath).Snapshot().Principals
            .Where(candidate => string.Equals(candidate.AppId, appId, StringComparison.Ordinal)
                                && candidate.RevokedAt == null
                                && candidate.ExpiresAt > now)
            .OrderByDescending(candidate => candidate.ExpiresAt)
            .FirstOrDefault();
        return principal == null ? null : ToWire(principal);
    }

    internal void RevokeApp(string workspaceCraftPath, string appId, string actor)
    {
        var principalIds = Store(workspaceCraftPath).Snapshot().Principals
            .Where(candidate => string.Equals(candidate.AppId, appId, StringComparison.Ordinal)
                                && candidate.RevokedAt == null)
            .Select(candidate => candidate.PrincipalId)
            .ToArray();
        foreach (var principalId in principalIds)
            RevokePrincipal(workspaceCraftPath, principalId, actor);
    }

    internal AppSurfaceSnapshot PublishSurface(
        string workspaceCraftPath,
        string principalId,
        AppSurfacePublishCommand parameters)
    {
        Require(parameters.SurfaceId, "surfaceId");
        Require(parameters.Endpoint, "endpoint");
        Require(parameters.Bearer, "bearer");
        ValidateSurfaceEndpoint(parameters.Endpoint);

        var now = DateTimeOffset.UtcNow;
        var state = Store(workspaceCraftPath).Snapshot();
        var principal = RequirePrincipal(state, principalId, now);
        var lease = new AppSurfaceLease(
            principal.PrincipalId,
            principal.AppId,
            parameters.SurfaceId.Trim(),
            NormalizeEndpointIdentity(parameters.Endpoint),
            parameters.Bearer,
            now.Add(SurfaceLeaseLifetime));
        SurfaceStore(workspaceCraftPath)[SurfaceKey(principal.AppId, lease.SurfaceId)] = lease;
        return ToWire(lease);
    }

    internal AppSurfaceSnapshot ResolveSurface(
        string workspaceCraftPath,
        string appId,
        string surfaceId)
    {
        Require(appId, "appId");
        Require(surfaceId, "surfaceId");
        var key = SurfaceKey(appId.Trim(), surfaceId.Trim());
        var surfaces = SurfaceStore(workspaceCraftPath);
        if (!surfaces.TryGetValue(key, out var lease))
            throw AppServerErrors.AppSurfaceUnavailable(appId, surfaceId);

        var now = DateTimeOffset.UtcNow;
        var principal = GetActivePrincipal(workspaceCraftPath, appId);
        if (lease.ExpiresAt <= now || principal == null
            || !string.Equals(principal.PrincipalId, lease.PrincipalId, StringComparison.Ordinal))
        {
            surfaces.TryRemove(key, out _);
            throw AppServerErrors.AppSurfaceUnavailable(appId, surfaceId);
        }

        return ToWire(lease);
    }

    internal AppConnectionRefreshOutcome Refresh(
        string workspaceCraftPath,
        string principalId)
    {
        var credential = AppBindingSecrets.NewSecret();
        var verifier = AppBindingSecrets.CreateVerifier(credential);
        var now = DateTimeOffset.UtcNow;
        return Store(workspaceCraftPath).Update(state =>
        {
            var principal = RequirePrincipal(state, principalId, now);
            principal.CredentialSalt = verifier.Salt;
            principal.CredentialVerifier = verifier.Verifier;
            principal.ExpiresAt = now.Add(AppBindingContract.PrincipalCredentialLifetime);
            Audit(state, "connection.refreshed", principal.UserId, principal.AppId, principalId: principal.PrincipalId);
            return new AppConnectionRefreshOutcome
            {
                Principal = ToWire(principal),
                Credential = credential
            };
        });
    }

    internal void RevokePrincipal(string workspaceCraftPath, string principalId, string actor)
    {
        var now = DateTimeOffset.UtcNow;
        Store(workspaceCraftPath).Update(state =>
        {
            var principal = state.Principals.FirstOrDefault(candidate =>
                string.Equals(candidate.PrincipalId, principalId, StringComparison.Ordinal));
            if (principal == null)
                throw AppServerErrors.AppPrincipalUnauthorized("The app principal was not found.");
            principal.RevokedAt ??= now;
            foreach (var binding in state.Bindings.Where(candidate =>
                         string.Equals(candidate.PrincipalId, principalId, StringComparison.Ordinal)
                         && candidate.State != AppBindingStates.Revoked))
            {
                binding.State = AppBindingStates.Revoked;
                binding.AuthorityRevision++;
                binding.RevokedAt = now;
                binding.UpdatedAt = now;
            }
            Audit(state, "connection.revoked", actor, principal.AppId, principalId: principalId);
            return 0;
        });
        if (_surfaces.TryGetValue(Path.GetFullPath(workspaceCraftPath), out var surfaces))
        {
            foreach (var entry in surfaces.Where(entry =>
                         string.Equals(entry.Value.PrincipalId, principalId, StringComparison.Ordinal)).ToArray())
                surfaces.TryRemove(entry.Key, out _);
        }
    }

    internal ThreadAppBindingEnableOutcome Enable(
        string workspaceCraftPath,
        string threadId,
        string appId,
        string userId)
    {
        Require(threadId, "threadId");
        Require(appId, "appId");
        var now = DateTimeOffset.UtcNow;
        var token = AppBindingSecrets.NewSecret();
        return Store(workspaceCraftPath).Update(state =>
        {
            if (state.Bindings.Any(candidate =>
                    candidate.State != AppBindingStates.Revoked
                    && string.Equals(candidate.ThreadId, threadId, StringComparison.Ordinal)
                    && string.Equals(candidate.AppId, appId, StringComparison.Ordinal)))
            {
                throw AppServerErrors.AppBindingConflict("This app is already enabled or enabling for the thread.");
            }

            var binding = new AppBindingRecord
            {
                BindingId = $"appbind_{Guid.NewGuid():N}",
                ThreadId = threadId,
                AppId = appId,
                UserId = NormalizeUser(userId),
                State = AppBindingStates.Connecting,
                CreatedAt = now,
                UpdatedAt = now
            };
            var request = new AppBindingRequestRecord
            {
                BindingRequestId = $"appbindreq_{Guid.NewGuid():N}",
                BindingId = binding.BindingId,
                ThreadId = threadId,
                AppId = appId,
                UserId = binding.UserId,
                RequestTokenHash = AppBindingSecrets.HashRequestToken(token),
                CreatedAt = now,
                ExpiresAt = now.Add(AppBindingContract.HandoffLifetime)
            };
            state.Bindings.Add(binding);
            state.BindingRequests.Add(request);
            Audit(state, "binding.enabled", binding.UserId, appId, threadId, binding.BindingId, binding.AuthorityRevision);
            return new ThreadAppBindingEnableOutcome
            {
                BindingRequestId = request.BindingRequestId,
                BindingId = binding.BindingId,
                State = binding.State,
                ExpiresAt = request.ExpiresAt,
                RequestToken = token,
                Handoff = new AppHandoffDescriptor { Mode = "bind" }
            };
        });
    }

    internal AppBindingRequestSnapshot GetBindingRequest(
        string workspaceCraftPath,
        AppBindingRequestQuery parameters,
        string? authenticatedPrincipalId)
    {
        var state = Store(workspaceCraftPath).Snapshot();
        var request = state.BindingRequests.FirstOrDefault(candidate =>
            string.Equals(candidate.BindingRequestId, parameters.BindingRequestId, StringComparison.Ordinal));
        if (request == null || request.Consumed || request.ExpiresAt <= DateTimeOffset.UtcNow)
            throw AppServerErrors.InvalidParams("Binding request is no longer available.");
        var principalAuthorized = !string.IsNullOrWhiteSpace(authenticatedPrincipalId)
                                  && state.Principals.Any(candidate =>
                                      string.Equals(candidate.PrincipalId, authenticatedPrincipalId, StringComparison.Ordinal)
                                      && string.Equals(candidate.AppId, request.AppId, StringComparison.Ordinal)
                                      && candidate.RevokedAt == null
                                      && candidate.ExpiresAt > DateTimeOffset.UtcNow);
        var tokenAuthorized = !string.IsNullOrWhiteSpace(parameters.RequestToken)
                              && string.Equals(
                                  AppBindingSecrets.HashRequestToken(parameters.RequestToken),
                                  request.RequestTokenHash,
                                  StringComparison.Ordinal);
        if (!principalAuthorized && !tokenAuthorized)
            throw AppServerErrors.AppPrincipalUnauthorized("The binding request does not belong to this app principal.");
        return ToWire(request);
    }

    internal IReadOnlyList<AppBindingSnapshot> ListThreadBindings(string workspaceCraftPath, string threadId) =>
        Store(workspaceCraftPath).Snapshot().Bindings
            .Where(candidate => string.Equals(candidate.ThreadId, threadId, StringComparison.Ordinal))
            .Select(ToWire)
            .ToArray();

    internal IReadOnlyList<AppBindingSnapshot> ListPrincipalBindings(string workspaceCraftPath, string principalId)
    {
        var state = Store(workspaceCraftPath).Snapshot();
        var principal = RequirePrincipal(state, principalId, DateTimeOffset.UtcNow);
        return state.Bindings
            .Where(candidate => string.Equals(candidate.AppId, principal.AppId, StringComparison.Ordinal)
                                && candidate.State != AppBindingStates.Revoked)
            .Select(ToWire)
            .ToArray();
    }

    internal IReadOnlyList<AppBindingSnapshot> ListAppBindings(string workspaceCraftPath, string appId) =>
        Store(workspaceCraftPath).Snapshot().Bindings
            .Where(binding => binding.State != AppBindingStates.Revoked
                              && string.Equals(binding.AppId, appId, StringComparison.Ordinal))
            .Select(ToWire).ToArray();

    internal AppBindingSnapshot RevokeBinding(
        string workspaceCraftPath,
        string threadId,
        string bindingId,
        string actor)
    {
        var now = DateTimeOffset.UtcNow;
        return Store(workspaceCraftPath).Update(state =>
        {
            var binding = state.Bindings.FirstOrDefault(candidate =>
                string.Equals(candidate.BindingId, bindingId, StringComparison.Ordinal)
                && string.Equals(candidate.ThreadId, threadId, StringComparison.Ordinal));
            if (binding == null)
                throw AppServerErrors.InvalidParams("Binding was not found for the thread.");
            binding.State = AppBindingStates.Revoked;
            binding.AuthorityRevision++;
            binding.RevokedAt = now;
            binding.UpdatedAt = now;
            binding.CandidateTools.Clear();
            binding.PendingChanges.Clear();
            binding.CandidateCapabilityRevision = null;
            Audit(state, "binding.revoked", actor, binding.AppId, threadId, bindingId, binding.AuthorityRevision);
            return ToWire(binding);
        });
    }

    internal IReadOnlyList<AppBindingSnapshot> RevokeThreadBindings(
        string workspaceCraftPath,
        string threadId,
        string actor)
    {
        var bindings = ListThreadBindings(workspaceCraftPath, threadId)
            .Where(binding => binding.State != AppBindingStates.Revoked)
            .ToArray();
        return bindings
            .Select(binding => RevokeBinding(workspaceCraftPath, threadId, binding.BindingId, actor))
            .ToArray();
    }

    internal AppBindingRecord BeginActivation(
        string workspaceCraftPath,
        string principalId,
        string bindingId,
        long? expectedAuthorityRevision,
        string endpoint,
        string? bindingRequestId = null)
    {
        ValidateBindingEndpoint(endpoint);
        var endpointIdentity = NormalizeEndpointIdentity(endpoint);
        var now = DateTimeOffset.UtcNow;
        return Store(workspaceCraftPath).Update(state =>
        {
            var principal = RequirePrincipal(state, principalId, now);
            var binding = state.Bindings.FirstOrDefault(candidate =>
                string.Equals(candidate.BindingId, bindingId, StringComparison.Ordinal))
                ?? throw AppServerErrors.InvalidParams("Binding was not found.");
            if (!string.Equals(binding.AppId, principal.AppId, StringComparison.Ordinal))
                throw AppServerErrors.AppPrincipalUnauthorized("The binding belongs to a different app principal.");
            if (binding.State == AppBindingStates.Revoked)
                throw AppServerErrors.AppBindingConflict("A revoked binding cannot be activated.");
            if (expectedAuthorityRevision.HasValue && binding.AuthorityRevision != expectedAuthorityRevision.Value)
                throw AppServerErrors.AppBindingConflict("The binding authority revision is stale.");
            if (!string.IsNullOrWhiteSpace(bindingRequestId))
            {
                var request = state.BindingRequests.FirstOrDefault(candidate =>
                    string.Equals(candidate.BindingRequestId, bindingRequestId, StringComparison.Ordinal)
                    && string.Equals(candidate.BindingId, bindingId, StringComparison.Ordinal));
                if (request == null || request.Consumed || request.ExpiresAt <= now)
                    throw AppServerErrors.InvalidParams("Binding request is invalid, expired, or consumed.");
                request.Consumed = true;
                request.State = AppBindingStates.Syncing;
            }
            binding.PrincipalId = principal.PrincipalId;
            binding.EndpointIdentity = endpointIdentity;
            binding.State = AppBindingStates.Syncing;
            binding.FailureReason = null;
            binding.AuthorityRevision++;
            binding.UpdatedAt = now;
            Audit(state, "binding.syncing", principal.PrincipalId, binding.AppId, binding.ThreadId,
                binding.BindingId, binding.AuthorityRevision);
            return Clone(binding);
        });
    }

    internal AppBindingSnapshot CompleteSync(
        string workspaceCraftPath,
        string bindingId,
        IReadOnlyList<AppBindingToolCapability> tools)
    {
        var normalized = tools.OrderBy(tool => tool.Namespace, StringComparer.Ordinal)
            .ThenBy(tool => tool.Name, StringComparer.Ordinal).ToList();
        var now = DateTimeOffset.UtcNow;
        return Store(workspaceCraftPath).Update(state =>
        {
            var binding = RequireLiveBinding(state, bindingId);
            var nextRevision = Math.Max(binding.ApprovedCapabilityRevision,
                binding.CandidateCapabilityRevision ?? 0) + 1;
            if (binding.ApprovedCapabilityRevision == 0)
            {
                binding.ApprovedTools = normalized;
                binding.ApprovedCapabilityRevision = nextRevision;
                binding.State = AppBindingStates.Active;
                binding.PendingChanges.Clear();
                Audit(state, "capabilities.initial-approved", binding.PrincipalId, binding.AppId,
                    binding.ThreadId, binding.BindingId, binding.AuthorityRevision, nextRevision);
            }
            else
            {
                var changes = AppBindingCapabilityDiffer.FindExpansions(binding.ApprovedTools, normalized);
                if (changes.Count == 0)
                {
                    binding.ApprovedTools = normalized;
                    binding.ApprovedCapabilityRevision = nextRevision;
                    binding.CandidateTools.Clear();
                    binding.CandidateCapabilityRevision = null;
                    binding.PendingChanges.Clear();
                    binding.State = AppBindingStates.Active;
                    Audit(state, "capabilities.auto-accepted", binding.PrincipalId, binding.AppId,
                        binding.ThreadId, binding.BindingId, binding.AuthorityRevision, nextRevision);
                }
                else
                {
                    binding.CandidateTools = normalized;
                    binding.CandidateCapabilityRevision = nextRevision;
                    binding.PendingChanges = changes;
                    binding.State = AppBindingStates.NeedsConfirmation;
                    Audit(state, "capabilities.confirmation-required", binding.PrincipalId, binding.AppId,
                        binding.ThreadId, binding.BindingId, binding.AuthorityRevision, nextRevision);
                }
            }
            binding.FailureReason = null;
            binding.UpdatedAt = now;
            return ToWire(binding);
        });
    }

    internal AppBindingSnapshot ConfirmCapabilities(
        string workspaceCraftPath,
        ThreadAppBindingConfirmCapabilitiesCommand parameters,
        string actor)
    {
        var accept = string.Equals(parameters.Decision, "accept", StringComparison.OrdinalIgnoreCase);
        var reject = string.Equals(parameters.Decision, "reject", StringComparison.OrdinalIgnoreCase);
        if (!accept && !reject)
            throw AppServerErrors.InvalidParams("'decision' must be 'accept' or 'reject'.");
        return Store(workspaceCraftPath).Update(state =>
        {
            var binding = RequireLiveBinding(state, parameters.BindingId);
            if (!string.Equals(binding.ThreadId, parameters.ThreadId, StringComparison.Ordinal)
                || binding.State != AppBindingStates.NeedsConfirmation
                || binding.CandidateCapabilityRevision != parameters.CandidateRevision)
                throw AppServerErrors.AppBindingConflict("The candidate capability revision is stale.");
            if (accept)
            {
                binding.ApprovedTools = binding.CandidateTools;
                binding.ApprovedCapabilityRevision = parameters.CandidateRevision;
                binding.State = AppBindingStates.Active;
            }
            else
            {
                binding.State = AppBindingStates.Active;
            }
            binding.CandidateTools = [];
            binding.CandidateCapabilityRevision = null;
            binding.PendingChanges = [];
            binding.UpdatedAt = DateTimeOffset.UtcNow;
            Audit(state, accept ? "capabilities.accepted" : "capabilities.rejected", actor, binding.AppId,
                binding.ThreadId, binding.BindingId, binding.AuthorityRevision, parameters.CandidateRevision);
            return ToWire(binding);
        });
    }

    internal AppBindingSnapshot MarkUnavailable(
        string workspaceCraftPath,
        string bindingId,
        string reason,
        bool failed = false) =>
        Store(workspaceCraftPath).Update(state =>
        {
            var binding = RequireLiveBinding(state, bindingId);
            binding.State = failed && binding.ApprovedCapabilityRevision == 0
                ? AppBindingStates.Failed
                : AppBindingStates.Offline;
            binding.FailureReason = reason;
            binding.UpdatedAt = DateTimeOffset.UtcNow;
            Audit(state, "binding.unavailable", binding.PrincipalId, binding.AppId, binding.ThreadId,
                binding.BindingId, binding.AuthorityRevision);
            return ToWire(binding);
        });

    internal AppBindingRecord GetBinding(string workspaceCraftPath, string bindingId) =>
        Clone(Store(workspaceCraftPath).Snapshot().Bindings.FirstOrDefault(candidate =>
                  string.Equals(candidate.BindingId, bindingId, StringComparison.Ordinal))
              ?? throw AppServerErrors.InvalidParams("Binding was not found."));

    internal AppBindingRecord AuthorizeThreadInput(
        string workspaceCraftPath, string bindingId, string principalId)
    {
        var state = Store(workspaceCraftPath).Snapshot();
        if (!principalId.StartsWith("channel:", StringComparison.Ordinal))
            _ = RequirePrincipal(state, principalId, DateTimeOffset.UtcNow);
        var binding = Clone(state.Bindings.FirstOrDefault(candidate => candidate.BindingId == bindingId)
                            ?? throw AppServerErrors.InvalidParams("Binding was not found."));
        if (binding.State != AppBindingStates.Active
            || !string.Equals(binding.PrincipalId, principalId, StringComparison.Ordinal))
            throw AppServerErrors.AppPrincipalUnauthorized("The caller does not own an active binding.");
        return binding;
    }

    internal ThreadSocialBindingRequestCreateOutcome CreateSocialRequest(
        string workspaceCraftPath,
        string threadId,
        string channelName,
        string userId)
    {
        Require(threadId, "threadId");
        Require(channelName, "channelName");
        var normalizedChannel = channelName.Trim().ToLowerInvariant();
        var appId = AppIdForChannel(normalizedChannel);
        var code = AppBindingSecrets.NewSecret()[..12];
        var now = DateTimeOffset.UtcNow;
        return Store(workspaceCraftPath).Update(state =>
        {
            if (state.Bindings.Any(binding => binding.State != AppBindingStates.Revoked
                                              && binding.Kind == "social"
                                              && binding.ThreadId == threadId
                                              && binding.AppId == appId))
                throw AppServerErrors.AppBindingConflict("This social channel is already binding to the thread.");
            var binding = new AppBindingRecord
            {
                BindingId = $"socialbind_{Guid.NewGuid():N}", ThreadId = threadId, AppId = appId,
                UserId = NormalizeUser(userId), Kind = "social", State = AppBindingStates.Connecting,
                CreatedAt = now, UpdatedAt = now
            };
            var request = new AppBindingRequestRecord
            {
                BindingRequestId = $"socialreq_{Guid.NewGuid():N}", BindingId = binding.BindingId,
                ThreadId = threadId, AppId = appId, UserId = binding.UserId,
                RequestTokenHash = AppBindingSecrets.HashRequestToken(code), CreatedAt = now,
                ExpiresAt = now.Add(AppBindingContract.HandoffLifetime)
            };
            state.Bindings.Add(binding); state.BindingRequests.Add(request);
            Audit(state, "social.requested", binding.UserId, appId, threadId, binding.BindingId, binding.AuthorityRevision);
            return new ThreadSocialBindingRequestCreateOutcome
            {
                BindingRequestId = request.BindingRequestId,
                BindingId = binding.BindingId,
                Code = code,
                ChannelName = normalizedChannel,
                ExpiresAt = request.ExpiresAt
            };
        });
    }

    internal AppBindingRequestSnapshot GetSocialRequest(string workspaceCraftPath, string code, string channelName)
    {
        Require(code, "code");
        var state = Store(workspaceCraftPath).Snapshot();
        var hash = AppBindingSecrets.HashRequestToken(code);
        var request = state.BindingRequests.FirstOrDefault(candidate => candidate.ExpiresAt > DateTimeOffset.UtcNow
            && !candidate.Consumed && candidate.RequestTokenHash == hash);
        if (request == null) throw AppServerErrors.InvalidParams("Social binding code is invalid or expired.");
        var expected = AppIdForChannel(channelName);
        if (!string.Equals(request.AppId, expected, StringComparison.Ordinal))
            throw AppServerErrors.AppPrincipalUnauthorized("The social binding request belongs to another channel.");
        return ToWire(request);
    }

    internal AppBindingSnapshot AcceptSocial(
        string workspaceCraftPath, string channelName, SocialBindingAcceptCommand parameters)
    {
        ValidateSocialTarget(channelName, parameters.Target);
        var now = DateTimeOffset.UtcNow;
        var hash = AppBindingSecrets.HashRequestToken(parameters.Code);
        return Store(workspaceCraftPath).Update(state =>
        {
            var request = state.BindingRequests.FirstOrDefault(candidate => !candidate.Consumed
                && candidate.ExpiresAt > now && candidate.RequestTokenHash == hash)
                ?? throw AppServerErrors.InvalidParams("Social binding code is invalid or expired.");
            var binding = RequireLiveBinding(state, request.BindingId);
            if (!string.Equals(binding.AppId, AppIdForChannel(channelName), StringComparison.Ordinal))
                throw AppServerErrors.AppPrincipalUnauthorized("The binding request belongs to another channel.");
            EnsureUniqueSocialTarget(state, binding.BindingId, parameters.Target);
            request.Consumed = true; request.State = AppBindingStates.Active;
            binding.PrincipalId = $"channel:{channelName.ToLowerInvariant()}";
            binding.SocialTarget = parameters.Target;
            binding.State = AppBindingStates.Active;
            binding.AuthorityRevision++;
            binding.ApprovedCapabilityRevision = Math.Max(1, binding.ApprovedCapabilityRevision + 1);
            binding.UpdatedAt = now;
            Audit(state, "social.accepted", binding.PrincipalId, binding.AppId, binding.ThreadId,
                binding.BindingId, binding.AuthorityRevision, binding.ApprovedCapabilityRevision);
            return ToWire(binding);
        });
    }

    internal AppBindingSnapshot RebindSocial(
        string workspaceCraftPath, string channelName, SocialBindingRebindCommand parameters)
    {
        ValidateSocialTarget(channelName, parameters.Target);
        return Store(workspaceCraftPath).Update(state =>
        {
            var binding = RequireLiveBinding(state, parameters.BindingId);
            if (binding.Kind != "social" || binding.AuthorityRevision != parameters.AuthorityRevision
                || !string.Equals(binding.AppId, AppIdForChannel(channelName), StringComparison.Ordinal))
                throw AppServerErrors.AppBindingConflict("The social binding authority is stale or owned by another channel.");
            EnsureUniqueSocialTarget(state, binding.BindingId, parameters.Target);
            binding.SocialTarget = parameters.Target;
            binding.State = AppBindingStates.Active;
            binding.AuthorityRevision++;
            binding.UpdatedAt = DateTimeOffset.UtcNow;
            Audit(state, "social.rebound", $"channel:{channelName}", binding.AppId, binding.ThreadId,
                binding.BindingId, binding.AuthorityRevision);
            return ToWire(binding);
        });
    }

    internal AppBindingSnapshot? ResolveSocial(
        string workspaceCraftPath, string channelName, string? accountId, string conversationKind, string conversationId) =>
        Store(workspaceCraftPath).Snapshot().Bindings
            .Where(binding => binding.Kind == "social" && binding.State == AppBindingStates.Active
                              && string.Equals(binding.SocialTarget?.ChannelName, channelName, StringComparison.OrdinalIgnoreCase)
                              && string.Equals(binding.SocialTarget?.AccountId ?? string.Empty, accountId ?? string.Empty, StringComparison.Ordinal)
                              && string.Equals(binding.SocialTarget?.ConversationKind, conversationKind, StringComparison.OrdinalIgnoreCase)
                              && string.Equals(binding.SocialTarget?.ConversationId, conversationId, StringComparison.Ordinal))
            .Select(ToWire).SingleOrDefault();

    private static void ValidateSocialTarget(string channelName, SocialChannelTarget target)
    {
        if (!string.Equals(target.ChannelName, channelName, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(target.ConversationKind)
            || string.IsNullOrWhiteSpace(target.ConversationId)
            || string.IsNullOrWhiteSpace(target.DeliveryTarget))
            throw AppServerErrors.InvalidParams("The social target is incomplete or belongs to another channel.");
    }

    private static void EnsureUniqueSocialTarget(
        AppBindingStateDocument state,
        string bindingId,
        SocialChannelTarget target)
    {
        if (state.Bindings.Any(candidate => candidate.Kind == "social"
            && candidate.State == AppBindingStates.Active
            && !string.Equals(candidate.BindingId, bindingId, StringComparison.Ordinal)
            && string.Equals(candidate.SocialTarget?.ChannelName, target.ChannelName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.SocialTarget?.AccountId ?? string.Empty, target.AccountId ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(candidate.SocialTarget?.ConversationKind, target.ConversationKind, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.SocialTarget?.ConversationId, target.ConversationId, StringComparison.Ordinal)))
        {
            throw AppServerErrors.AppBindingConflict("This social conversation is already bound to another thread.");
        }
    }

    internal static void ValidateBindingEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            throw AppServerErrors.AppBindingPolicyDenied("Binding MCP endpoint must be an absolute URI.");
        var allowed = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                      || (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                          && uri.IsLoopback);
        if (!allowed)
            throw AppServerErrors.AppBindingPolicyDenied("Binding MCP permits only remote HTTPS or loopback HTTP.");
        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
            throw AppServerErrors.AppBindingPolicyDenied("Binding MCP endpoint must not contain user information.");
    }

    internal static void ValidateSurfaceEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || !uri.IsLoopback
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw AppServerErrors.AppBindingPolicyDenied("App surfaces permit only loopback HTTP or HTTPS endpoints.");
        if (!string.IsNullOrWhiteSpace(uri.UserInfo) || !string.IsNullOrWhiteSpace(uri.Fragment))
            throw AppServerErrors.AppBindingPolicyDenied("App surface endpoints must not contain user information or fragments.");
    }

    private ConcurrentDictionary<string, AppSurfaceLease> SurfaceStore(string workspaceCraftPath) =>
        _surfaces.GetOrAdd(Path.GetFullPath(workspaceCraftPath), static _ =>
            new ConcurrentDictionary<string, AppSurfaceLease>(StringComparer.Ordinal));

    private static string SurfaceKey(string appId, string surfaceId) => $"{appId}\n{surfaceId}";

    private static AppSurfaceSnapshot ToWire(AppSurfaceLease lease) => new()
    {
        AppId = lease.AppId,
        SurfaceId = lease.SurfaceId,
        Endpoint = lease.Endpoint,
        Bearer = lease.Bearer,
        ExpiresAt = lease.ExpiresAt
    };

    private static string NormalizeEndpointIdentity(string endpoint)
    {
        var uri = new Uri(endpoint, UriKind.Absolute);
        return new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty, Fragment = string.Empty }
            .Uri.AbsoluteUri;
    }

    private static AppBindingRecord RequireLiveBinding(AppBindingStateDocument state, string bindingId)
    {
        var binding = state.Bindings.FirstOrDefault(candidate =>
            string.Equals(candidate.BindingId, bindingId, StringComparison.Ordinal));
        if (binding == null || binding.State == AppBindingStates.Revoked)
            throw AppServerErrors.AppBindingConflict("Binding authority is no longer live.");
        return binding;
    }

    private static AppBindingRecord Clone(AppBindingRecord binding) =>
        System.Text.Json.JsonSerializer.Deserialize<AppBindingRecord>(
            System.Text.Json.JsonSerializer.Serialize(binding, SessionWireJsonOptions.Default),
            SessionWireJsonOptions.Default)!;

    internal static AppBindingSnapshot ToWire(AppBindingRecord binding) => new()
    {
        BindingId = binding.BindingId,
        ThreadId = binding.ThreadId,
        AppId = binding.AppId,
        State = binding.State,
        AuthorityRevision = binding.AuthorityRevision,
        ApprovedCapabilityRevision = binding.ApprovedCapabilityRevision,
        CandidateCapabilityRevision = binding.CandidateCapabilityRevision,
        ApprovedTools = binding.ApprovedTools,
        PendingChanges = binding.PendingChanges,
        SocialTarget = binding.SocialTarget,
        FailureReason = binding.FailureReason,
        UpdatedAt = binding.UpdatedAt
    };

    private static AppPrincipalRecord RequirePrincipal(
        AppBindingStateDocument state,
        string principalId,
        DateTimeOffset now) =>
        state.Principals.FirstOrDefault(candidate =>
            string.Equals(candidate.PrincipalId, principalId, StringComparison.Ordinal)
            && candidate.RevokedAt == null
            && candidate.ExpiresAt > now)
        ?? throw AppServerErrors.AppPrincipalUnauthorized("The app principal is invalid, expired, or revoked.");

    private static AppPrincipalSnapshot ToWire(AppPrincipalRecord principal) => new()
    {
        PrincipalId = principal.PrincipalId,
        AppId = principal.AppId,
        UserId = principal.UserId,
        ExpiresAt = principal.ExpiresAt
    };

    private static AppBindingRequestSnapshot ToWire(AppBindingRequestRecord request) => new()
    {
        BindingRequestId = request.BindingRequestId,
        BindingId = request.BindingId,
        ThreadId = request.ThreadId,
        AppId = request.AppId,
        State = request.State,
        ExpiresAt = request.ExpiresAt
    };

    private static void ValidateRequest(AppConnectionRequestRecord? request, string token)
    {
        if (request == null || request.Consumed || request.ExpiresAt <= DateTimeOffset.UtcNow
            || !string.Equals(
                AppBindingSecrets.HashRequestToken(token),
                request.RequestTokenHash,
                StringComparison.Ordinal))
        {
            throw AppServerErrors.InvalidParams("Connection request is invalid, expired, or consumed.");
        }
    }

    private static void Audit(
        AppBindingStateDocument state,
        string @event,
        string actor,
        string appId,
        string? threadId = null,
        string? bindingId = null,
        long? authorityRevision = null,
        long? capabilityRevision = null,
        string? principalId = null) =>
        state.Audit.Add(new AppBindingAuditRecord
        {
            Timestamp = DateTimeOffset.UtcNow,
            Event = @event,
            Actor = string.IsNullOrWhiteSpace(principalId) ? actor : principalId,
            AppId = appId,
            ThreadId = threadId,
            BindingId = bindingId,
            AuthorityRevision = authorityRevision,
            CapabilityRevision = capabilityRevision
        });

    private static string NormalizeUser(string userId) =>
        string.IsNullOrWhiteSpace(userId) ? "appserver" : userId.Trim();

    private static string AppIdForChannel(string channelName) =>
        $"com.dotharness.channel.{channelName.Trim().ToLowerInvariant()}";

    private sealed record AppSurfaceLease(
        string PrincipalId,
        string AppId,
        string SurfaceId,
        string Endpoint,
        string Bearer,
        DateTimeOffset ExpiresAt);

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw AppServerErrors.InvalidParams($"'{name}' is required.");
    }
}
