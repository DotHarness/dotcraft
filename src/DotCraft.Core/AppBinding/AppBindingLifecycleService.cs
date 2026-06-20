using DotCraft.Protocol.AppServer;
using System.Security.Cryptography;
using static DotCraft.AppBinding.AppBindingStoreAccessor;

namespace DotCraft.AppBinding;

internal sealed class AppBindingLifecycleService(
    AppBindingService owner,
    AppBindingStoreAccessor stores,
    AppBindingAttachmentRegistry attachments,
    AppToolAttachmentService tools,
    IReadOnlyDictionary<string, IManagedAppBindingRuntime> managedRuntimesByAppId)
{
    public AppBindingRequestCreateResult CreateBindingRequest(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        string userId,
        AppBindingRequestCreateParams p)
    {
        if (string.IsNullOrWhiteSpace(p.ThreadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");
        if (string.IsNullOrWhiteSpace(p.AppId))
            throw AppServerErrors.InvalidParams("'appId' is required.");
        if (p.RequestedScopes.Count == 0)
            throw AppServerErrors.InvalidParams("'requestedScopes' must not be empty.");
        if (string.IsNullOrWhiteSpace(p.Source))
            throw AppServerErrors.InvalidParams("'source' is required.");

        var entry = FindEnabledApp(catalog, p.AppId);
        var bindingKind = NormalizeBindingKind(p.BindingKind);
        var socialIntent = NormalizeSocialIntent(bindingKind, p.SocialIntent);

        if (entry.ManagedRuntime != null
            && !string.Equals(bindingKind, AppBindingKinds.SocialChannel, StringComparison.Ordinal)
            && managedRuntimesByAppId.TryGetValue(p.AppId, out var managedRuntime))
        {
            return CreateManagedThreadBindingRequest(
                workspaceCraftPath,
                userId,
                p,
                managedRuntime,
                managedRuntime.GetCatalogDescriptor(AppBindingCatalogSurfaces.ThreadBinding));
        }

        AppBindingService.ValidateRequestedScopes(entry.Descriptor, p.RequestedScopes);
        AppBindingService.ValidateRequestedTools(entry.Descriptor, p.RequestedTools);

        var state = stores.GetStore(workspaceCraftPath).Snapshot();
        if (!owner.IsAppConnectionUsable(state, userId, p.AppId))
            throw AppServerErrors.InvalidParams($"App '{p.AppId}' is not connected for this workspace user.");

        var token = string.Equals(bindingKind, AppBindingKinds.SocialChannel, StringComparison.Ordinal)
            ? NewBindCode()
            : AppBindingToken.NewToken();
        var requestId = $"bind_req_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(10);
        var handoff = string.Equals(bindingKind, AppBindingKinds.SocialChannel, StringComparison.Ordinal)
            ? BuildSocialHandoff(entry.Descriptor, socialIntent!, token)
            : AppBindingService.BuildHandoff(workspaceCraftPath, entry.Descriptor, null, requestId, token, "bind", p.RequestedScopes);
        var risk = AppBindingService.HighestRisk(entry.Descriptor, p.RequestedScopes);

        stores.GetStore(workspaceCraftPath).Update(writeState =>
        {
            writeState.BindingRequests.Add(new AppBindingRequestRecord
            {
                BindingRequestId = requestId,
                ThreadId = p.ThreadId,
                AppId = p.AppId,
                UserId = userId,
                RequestedScopes = p.RequestedScopes.Distinct(StringComparer.Ordinal).ToList(),
                RequestedTools = p.RequestedTools?.Distinct(StringComparer.Ordinal).ToList(),
                Reason = p.Reason,
                Source = p.Source,
                RequestTokenHash = AppBindingToken.Hash(token),
                BindingKind = bindingKind,
                SocialIntent = socialIntent,
                CreatedAt = now,
                ExpiresAt = expiresAt
            });
            AppBindingService.AddAudit(writeState, "binding.request.created", p.ThreadId, null, p.AppId, userId, p.Source);
            return true;
        });

        return new AppBindingRequestCreateResult
        {
            BindingRequestId = requestId,
            ThreadId = p.ThreadId,
            AppId = entry.Descriptor.AppId,
            RequestedScopes = p.RequestedScopes.Distinct(StringComparer.Ordinal).ToList(),
            TokenExpiresAt = expiresAt,
            Handoff = handoff,
            Confirmation = new AppBindingConfirmationWire
            {
                Required = true,
                Risk = risk,
                Message = $"Grant {entry.Descriptor.DisplayName} access to this thread?"
            }
        };
    }

    private AppBindingRequestCreateResult CreateManagedThreadBindingRequest(
        string workspaceCraftPath,
        string userId,
        AppBindingRequestCreateParams p,
        IManagedAppBindingRuntime runtime,
        AppDescriptor descriptor)
    {
        if (string.Equals(p.Source, AppBindingCatalogSurfaces.PluginDetail, StringComparison.Ordinal))
            throw AppServerErrors.InvalidParams("Managed runtimes cannot be bound from the plugin detail surface.");

        AppBindingService.ValidateRequestedScopes(descriptor, p.RequestedScopes);
        AppBindingService.ValidateRequestedTools(descriptor, p.RequestedTools);

        var state = stores.GetStore(workspaceCraftPath).Snapshot();
        if (!owner.IsAppConnectionUsable(state, userId, p.AppId))
            throw AppServerErrors.InvalidParams($"App '{p.AppId}' is not connected for this workspace user.");

        var toolSpecs = runtime.GetToolSpecsForSurface(ManagedAppBindingToolSurfaces.ThreadBinding).ToList();
        if (p.RequestedTools is { Count: > 0 } requestedTools)
        {
            var requested = requestedTools.ToHashSet(StringComparer.Ordinal);
            toolSpecs = toolSpecs.Where(tool => requested.Contains(tool.Name)).ToList();
        }

        if (toolSpecs.Count == 0)
            throw AppServerErrors.InvalidParams("The managed app did not expose any tools for this thread binding.");

        var now = DateTimeOffset.UtcNow;
        var binding = EnsureManagedBinding(
            workspaceCraftPath,
            p.ThreadId,
            p.AppId,
            userId,
            $"managed_grant_{Guid.NewGuid():N}",
            p.RequestedScopes.Distinct(StringComparer.Ordinal).ToList(),
            toolSpecs,
            descriptor);

        return new AppBindingRequestCreateResult
        {
            BindingRequestId = binding.BindingId,
            ThreadId = p.ThreadId,
            AppId = p.AppId,
            RequestedScopes = p.RequestedScopes.Distinct(StringComparer.Ordinal).ToList(),
            State = AppBindingStates.Active,
            TokenExpiresAt = now,
            Handoff = new AppHandoffWire { Mode = "managed" },
            Confirmation = new AppBindingConfirmationWire
            {
                Required = false,
                Risk = AppBindingService.HighestRisk(descriptor, p.RequestedScopes),
                Message = string.Empty
            }
        };
    }

    public AppBindingRequestGetResult GetBindingRequest(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        AppBindingRequestGetParams p,
        string? threadTitle = null,
        string? channelAdapterName = null,
        bool requireSocialAuthorization = false)
    {
        if (string.IsNullOrWhiteSpace(p.AppId))
            throw AppServerErrors.InvalidParams("'appId' is required.");
        var token = string.IsNullOrWhiteSpace(p.RequestToken) ? p.BindCode : p.RequestToken;
        if (string.IsNullOrWhiteSpace(p.BindingRequestId) && string.IsNullOrWhiteSpace(token))
            throw AppServerErrors.InvalidParams("'bindingRequestId' or 'bindCode' is required.");
        if (string.IsNullOrWhiteSpace(token))
            throw AppServerErrors.InvalidParams("'requestToken' is required.");

        var entry = FindEnabledApp(catalog, p.AppId);
        var state = stores.GetStore(workspaceCraftPath).Snapshot();
        var now = DateTimeOffset.UtcNow;
        var request = ResolvePendingBindingRequest(state, p.AppId, p.BindingRequestId, token!);
        if (request == null)
            throw AppServerErrors.InvalidParams("Binding request was not found.");
        if (!string.Equals(request.AppId, p.AppId, StringComparison.Ordinal))
            throw AppServerErrors.InvalidParams("Binding request appId mismatch.");
        if (request.State != AppBindingStates.Pending || request.Consumed)
            throw AppServerErrors.InvalidParams("Binding request is no longer pending.");
        if (request.ExpiresAt <= now)
            throw AppServerErrors.InvalidParams("Binding request token has expired.");
        if (!AppBindingToken.Matches(token!, request.RequestTokenHash))
            throw AppServerErrors.InvalidParams("Binding request token is invalid.");
        AuthorizeSocialBindingRequestGet(request, channelAdapterName, requireSocialAuthorization);

        var requestedScopeSet = request.RequestedScopes.ToHashSet(StringComparer.Ordinal);
        var requestedTools = request.RequestedTools?.ToHashSet(StringComparer.Ordinal);
        return new AppBindingRequestGetResult
        {
            AppId = entry.Descriptor.AppId,
            BindingRequestId = request.BindingRequestId,
            ThreadId = request.ThreadId,
            ThreadTitle = threadTitle,
            DisplayName = entry.Descriptor.DisplayName,
            DeveloperName = entry.Descriptor.DeveloperName,
            Source = request.Source,
            Reason = request.Reason,
            RequestedScopes = request.RequestedScopes.ToList(),
            ScopeCatalog = entry.Descriptor.Scopes
                .Where(scope => requestedScopeSet.Contains(scope.Id))
                .ToList(),
            RequestedTools = request.RequestedTools?.ToList() ?? [],
            ToolCatalog = entry.Descriptor.ToolCatalog
                .Where(tool => requestedScopeSet.Contains(tool.Scope)
                               && (requestedTools == null || requestedTools.Contains(tool.Name)))
                .ToList(),
            DynamicToolCatalog = new AppDynamicToolCatalogDescriptor
            {
                Enabled = entry.Descriptor.DynamicToolCatalog.Enabled,
                Description = entry.Descriptor.DynamicToolCatalog.Description
            },
            ExpiresAt = request.ExpiresAt,
            BindingKind = request.BindingKind,
            SocialIntent = request.SocialIntent
        };
    }

    public AppBindingRequestCancelResult CancelBindingRequest(
        string workspaceCraftPath,
        AppBindingRequestCancelParams p)
    {
        if (string.IsNullOrWhiteSpace(p.BindingRequestId))
            throw AppServerErrors.InvalidParams("'bindingRequestId' is required.");

        return stores.GetStore(workspaceCraftPath).Update(state =>
        {
            var request = state.BindingRequests.FirstOrDefault(r =>
                string.Equals(r.BindingRequestId, p.BindingRequestId, StringComparison.Ordinal));
            if (request == null)
                throw AppServerErrors.InvalidParams($"Binding request '{p.BindingRequestId}' was not found.");

            request.State = AppBindingStates.Cancelled;
            request.Consumed = true;
            AppBindingService.AddAudit(state, "binding.request.cancelled", request.ThreadId, null, request.AppId, request.UserId, p.Reason);
            return new AppBindingRequestCancelResult
            {
                BindingRequestId = p.BindingRequestId,
                ThreadId = request.ThreadId,
                AppId = request.AppId,
                State = AppBindingStates.Cancelled
            };
        });
    }

    public AppBindingAcceptResult AcceptBinding(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        AppBindingAcceptParams p)
    {
        if (string.IsNullOrWhiteSpace(p.RequestToken))
            throw AppServerErrors.InvalidParams("'requestToken' is required.");
        if (string.IsNullOrWhiteSpace(p.GrantId))
            throw AppServerErrors.InvalidParams("'grantId' is required.");
        if (p.GrantedScopes.Count == 0)
            throw AppServerErrors.InvalidParams("'grantedScopes' must not be empty.");
        if (string.IsNullOrWhiteSpace(p.ApprovalMode))
            throw AppServerErrors.InvalidParams("'approvalMode' is required.");

        var now = DateTimeOffset.UtcNow;
        return stores.GetStore(workspaceCraftPath).Update(state =>
        {
            var request = ResolvePendingBindingRequest(state, appId: null, p.BindingRequestId, p.RequestToken);
            if (request == null)
                throw AppServerErrors.InvalidParams("Binding request was not found.");
            if (request.State != AppBindingStates.Pending || request.Consumed)
                throw AppServerErrors.InvalidParams("Binding request is no longer pending.");
            if (request.ExpiresAt <= now)
                throw AppServerErrors.InvalidParams("Binding request token has expired.");
            if (!AppBindingToken.Matches(p.RequestToken, request.RequestTokenHash))
                throw AppServerErrors.InvalidParams("Binding request token is invalid.");

            var entry = FindEnabledApp(catalog, request.AppId);
            AppBindingService.ValidateGrantedScopes(entry.Descriptor, request.RequestedScopes, p.GrantedScopes);
            var socialTarget = NormalizeAcceptedSocialTarget(request, p.SocialTarget);
            if (socialTarget != null)
                EnsureNoActiveSocialTargetConflict(state, request.AppId, socialTarget);
            if (!owner.IsAppConnectionUsable(state, request.UserId, request.AppId))
                throw AppServerErrors.InvalidParams($"App '{request.AppId}' is not connected for this workspace user.");

            request.Consumed = true;
            request.State = AppBindingStates.Active;
            var binding = new AppBindingRecord
            {
                BindingId = $"bind_{Guid.NewGuid():N}",
                ThreadId = request.ThreadId,
                AppId = request.AppId,
                UserId = request.UserId,
                State = AppBindingStates.Active,
                BindingKind = request.BindingKind,
                GrantId = p.GrantId,
                RequestedScopes = request.RequestedScopes.ToList(),
                GrantedScopes = p.GrantedScopes.Distinct(StringComparer.Ordinal).ToList(),
                CreatedAt = now,
                LastChangedAt = now,
                ExpiresAt = p.ExpiresAt,
                ApprovalMode = p.ApprovalMode,
                ApprovedBy = p.ApprovedBy,
                AuditRef = p.AuditRef,
                GrantProof = p.GrantProof?.DeepClone() as System.Text.Json.Nodes.JsonObject,
                SocialTarget = socialTarget,
                ExposureRevision = 1
            };
            if (managedRuntimesByAppId.TryGetValue(binding.AppId, out var managedRuntime))
            {
                var requestedTools = request.RequestedTools?.ToHashSet(StringComparer.Ordinal);
                var toolSpecs = managedRuntime
                    .GetToolSpecsForSurface(ManagedAppBindingToolSurfaces.ThreadBinding)
                    .Where(tool => requestedTools == null || requestedTools.Contains(tool.Name))
                    .ToList();
                if (toolSpecs.Count > 0)
                {
                    var warnings = new List<string>();
                    var attach = new AppBindingAttachToolsParams
                    {
                        BindingId = binding.BindingId,
                        ThreadId = binding.ThreadId,
                        AppId = binding.AppId,
                        GrantId = binding.GrantId,
                        Tools = toolSpecs,
                        DeferredToolNames = toolSpecs.Select(tool => tool.Name).ToList(),
                        GrantProof = p.GrantProof
                    };
                    var accepted = AppBindingService.ValidateAttachedTools(
                        entry.Descriptor,
                        binding,
                        attach,
                        warnings,
                        managedRuntime.AllowDirectMutatingToolExposure);
                    binding.AttachedTools = accepted;
                    binding.DirectToolNames = accepted
                        .Where(tool => tool.DeferLoading != true)
                        .Select(tool => tool.Name)
                        .Distinct(StringComparer.Ordinal)
                        .ToList();
                    binding.DeferredToolNames = accepted
                        .Where(tool => tool.DeferLoading == true)
                        .Select(tool => tool.Name)
                        .Distinct(StringComparer.Ordinal)
                        .ToList();
                    binding.ExposureRevision++;
                }
            }
            state.Bindings.Add(binding);
            AppBindingService.AddAudit(state, "binding.accepted", binding.ThreadId, binding.BindingId, binding.AppId, binding.UserId, p.AuditRef);
            return new AppBindingAcceptResult
            {
                Binding = owner.MapBinding(binding, entry.Descriptor, AppBindingService.MapConnectionStatus(state, binding.UserId, binding.AppId))
            };
        });
    }

    public AppSocialBindingResolveResult ResolveSocialBinding(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        AppSocialBindingResolveParams p)
    {
        if (string.IsNullOrWhiteSpace(p.AppId))
            throw AppServerErrors.InvalidParams("'appId' is required.");
        if (string.IsNullOrWhiteSpace(p.ChannelName))
            throw AppServerErrors.InvalidParams("'channelName' is required.");
        if (string.IsNullOrWhiteSpace(p.ConversationKind))
            throw AppServerErrors.InvalidParams("'conversationKind' is required.");
        if (string.IsNullOrWhiteSpace(p.ConversationId))
            throw AppServerErrors.InvalidParams("'conversationId' is required.");

        var entry = FindEnabledApp(catalog, p.AppId);
        var state = stores.GetStore(workspaceCraftPath).Snapshot();
        var now = DateTimeOffset.UtcNow;
        var binding = state.Bindings
            .Where(candidate => candidate.State == AppBindingStates.Active
                                && string.Equals(candidate.AppId, p.AppId.Trim(), StringComparison.Ordinal)
                                && string.Equals(candidate.BindingKind, AppBindingKinds.SocialChannel, StringComparison.Ordinal)
                                && candidate.SocialTarget != null
                                && (candidate.ExpiresAt == null || candidate.ExpiresAt > now))
            .FirstOrDefault(candidate =>
                owner.IsBindingConnectionUsable(state, candidate)
                && SocialTargetMatches(candidate.SocialTarget!, p));
        return new AppSocialBindingResolveResult
        {
            Binding = binding == null
                ? null
                : owner.MapBinding(binding, entry.Descriptor, AppBindingService.MapConnectionStatus(state, binding.UserId, binding.AppId))
        };
    }

    public ThreadAppBindingWire EnsureManagedBinding(
        string workspaceCraftPath,
        string threadId,
        string appId,
        string userId,
        string grantId,
        IReadOnlyList<string> grantedScopes,
        IReadOnlyList<DynamicToolSpec>? toolSpecs = null,
        AppDescriptor? descriptorOverride = null)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");
        if (string.IsNullOrWhiteSpace(appId))
            throw AppServerErrors.InvalidParams("'appId' is required.");
        if (string.IsNullOrWhiteSpace(userId))
            throw AppServerErrors.InvalidParams("'userId' is required.");
        if (string.IsNullOrWhiteSpace(grantId))
            throw AppServerErrors.InvalidParams("'grantId' is required.");
        if (grantedScopes.Count == 0)
            throw AppServerErrors.InvalidParams("'grantedScopes' must not be empty.");
        if (!managedRuntimesByAppId.TryGetValue(appId, out var runtime))
            throw AppServerErrors.InvalidParams($"Managed app '{appId}' was not found.");

        var descriptor = descriptorOverride ?? runtime.Descriptor;
        AppBindingService.ValidateRequestedScopes(descriptor, grantedScopes);
        var specs = (toolSpecs ?? runtime.ToolSpecs).ToList();
        if (specs.Count == 0)
            throw AppServerErrors.InvalidParams("'tools' must not be empty.");
        if (!WireDynamicToolProxy.TryValidateSpecs(specs, out var dynamicToolError))
            throw AppServerErrors.InvalidParams(dynamicToolError);

        var warnings = new List<string>();
        var now = DateTimeOffset.UtcNow;
        return stores.GetStore(workspaceCraftPath).Update(state =>
        {
            var connection = FindConnection(state, userId, appId);
            if (connection == null)
            {
                connection = new AppConnectionRecord
                {
                    AppId = appId,
                    UserId = userId,
                    ConnectedAt = now
                };
                state.Connections.Add(connection);
                AppBindingService.AddAudit(state, "connection.managed.connected", null, null, appId, userId, descriptor.DisplayName);
            }

            connection.State = AppConnectionStates.Connected;
            connection.ConnectedAt ??= now;
            connection.ExpiresAt = null;
            connection.AccountLabel = descriptor.DisplayName;
            connection.Diagnostic = null;

            var binding = state.Bindings
                .Where(candidate => string.Equals(candidate.ThreadId, threadId, StringComparison.Ordinal)
                                    && string.Equals(candidate.AppId, appId, StringComparison.Ordinal)
                                    && string.Equals(candidate.UserId, userId, StringComparison.Ordinal)
                                    && candidate.State != AppBindingStates.Revoked)
                .OrderByDescending(candidate => candidate.LastChangedAt)
                .FirstOrDefault();
            var created = false;
            if (binding == null)
            {
                binding = new AppBindingRecord
                {
                    BindingId = $"bind_{Guid.NewGuid():N}",
                    ThreadId = threadId,
                    AppId = appId,
                    UserId = userId,
                    BindingKind = AppBindingKinds.ManagedApp,
                    CreatedAt = now
                };
                state.Bindings.Add(binding);
                created = true;
            }

            binding.State = AppBindingStates.Active;
            binding.GrantId = grantId.Trim();
            binding.RequestedScopes = grantedScopes.Distinct(StringComparer.Ordinal).ToList();
            binding.GrantedScopes = grantedScopes.Distinct(StringComparer.Ordinal).ToList();
            binding.ExpiresAt = null;
            binding.ApprovalMode = "managed";
            binding.ApprovedBy = userId;
            binding.AuditRef = "managed:first-party";
            binding.Diagnostic = null;
            binding.LastChangedAt = now;
            binding.ExposureRevision++;

            var attach = new AppBindingAttachToolsParams
            {
                BindingId = binding.BindingId,
                ThreadId = binding.ThreadId,
                AppId = binding.AppId,
                GrantId = binding.GrantId,
                Tools = specs,
                DirectToolNames = specs.Select(tool => tool.Name).ToList()
            };
            var accepted = AppBindingService.ValidateAttachedTools(
                descriptor,
                binding,
                attach,
                warnings,
                runtime.AllowDirectMutatingToolExposure);
            binding.AttachedTools = accepted;
            binding.DirectToolNames = accepted
                .Where(tool => tool.DeferLoading != true)
                .Select(tool => tool.Name)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            binding.DeferredToolNames = accepted
                .Where(tool => tool.DeferLoading == true)
                .Select(tool => tool.Name)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            AppBindingService.AddAudit(
                state,
                created ? "binding.managed.created" : "binding.managed.repaired",
                binding.ThreadId,
                binding.BindingId,
                binding.AppId,
                binding.UserId,
                $"{accepted.Count} tools");

            return owner.MapBinding(binding, descriptor, AppBindingService.MapConnectionStatus(connection));
        });
    }

    private static string NormalizeBindingKind(string? bindingKind)
    {
        if (string.IsNullOrWhiteSpace(bindingKind))
            return AppBindingKinds.App;
        var normalized = bindingKind.Trim();
        if (!AppBindingKinds.IsKnown(normalized))
            throw AppServerErrors.InvalidParams($"Unknown bindingKind '{bindingKind}'.");
        return normalized;
    }

    private static SocialBindingIntentWire? NormalizeSocialIntent(string bindingKind, SocialBindingIntentWire? socialIntent)
    {
        if (!string.Equals(bindingKind, AppBindingKinds.SocialChannel, StringComparison.Ordinal))
            return null;
        if (socialIntent == null)
            throw AppServerErrors.InvalidParams("'socialIntent' is required for socialChannel bindings.");
        if (string.IsNullOrWhiteSpace(socialIntent.ChannelName))
            throw AppServerErrors.InvalidParams("'socialIntent.channelName' is required.");
        var targetSelection = string.IsNullOrWhiteSpace(socialIntent.TargetSelection)
            ? SocialBindingTargetSelections.ConfirmInChannel
            : socialIntent.TargetSelection.Trim();
        if (!SocialBindingTargetSelections.IsKnown(targetSelection))
            throw AppServerErrors.InvalidParams($"Unknown social targetSelection '{socialIntent.TargetSelection}'.");

        return new SocialBindingIntentWire
        {
            ChannelName = socialIntent.ChannelName.Trim().ToLowerInvariant(),
            TargetSelection = targetSelection,
            DisplayHint = string.IsNullOrWhiteSpace(socialIntent.DisplayHint) ? null : socialIntent.DisplayHint.Trim()
        };
    }

    private static void AuthorizeSocialBindingRequestGet(
        AppBindingRequestRecord request,
        string? channelAdapterName,
        bool requireSocialAuthorization)
    {
        if (!string.Equals(request.BindingKind, AppBindingKinds.SocialChannel, StringComparison.Ordinal))
            return;

        var channelName = ResolveSocialRequestChannelName(request);
        if (string.IsNullOrWhiteSpace(channelName))
            throw AppServerErrors.InvalidParams("Social binding request channelName is missing.");
        if (!string.Equals(request.AppId, ChannelAppId(channelName), StringComparison.Ordinal))
            throw AppServerErrors.InvalidParams("Social binding appId does not match channelName.");
        if (!requireSocialAuthorization)
            return;
        if (string.IsNullOrWhiteSpace(channelAdapterName))
            throw AppServerErrors.InvalidParams("Social channel binding requests may only be inspected by channel adapters.");
        if (!string.Equals(channelAdapterName.Trim(), channelName, StringComparison.OrdinalIgnoreCase))
            throw AppServerErrors.InvalidParams("Channel adapter cannot inspect binding requests for another channel.");
    }

    private static string? ResolveSocialRequestChannelName(AppBindingRequestRecord request)
    {
        if (!string.IsNullOrWhiteSpace(request.SocialIntent?.ChannelName))
            return request.SocialIntent.ChannelName.Trim().ToLowerInvariant();

        const string prefix = "com.dotharness.channel.";
        return request.AppId.StartsWith(prefix, StringComparison.Ordinal)
            ? request.AppId[prefix.Length..]
            : null;
    }

    private static string ChannelAppId(string channelName) =>
        $"com.dotharness.channel.{channelName.Trim().ToLowerInvariant()}";

    private static SocialChannelTargetWire? NormalizeAcceptedSocialTarget(
        AppBindingRequestRecord request,
        SocialChannelTargetWire? socialTarget)
    {
        if (!string.Equals(request.BindingKind, AppBindingKinds.SocialChannel, StringComparison.Ordinal))
        {
            if (socialTarget != null)
                throw AppServerErrors.InvalidParams("'socialTarget' is only valid for socialChannel bindings.");
            return null;
        }

        if (socialTarget == null)
            throw AppServerErrors.InvalidParams("'socialTarget' is required for socialChannel bindings.");
        if (string.IsNullOrWhiteSpace(socialTarget.ChannelName))
            throw AppServerErrors.InvalidParams("'socialTarget.channelName' is required.");
        if (string.IsNullOrWhiteSpace(socialTarget.ConversationKind))
            throw AppServerErrors.InvalidParams("'socialTarget.conversationKind' is required.");
        if (string.IsNullOrWhiteSpace(socialTarget.ConversationId))
            throw AppServerErrors.InvalidParams("'socialTarget.conversationId' is required.");
        if (string.IsNullOrWhiteSpace(socialTarget.DeliveryTarget))
            throw AppServerErrors.InvalidParams("'socialTarget.deliveryTarget' is required.");

        var channelName = socialTarget.ChannelName.Trim().ToLowerInvariant();
        if (request.SocialIntent != null
            && !string.Equals(channelName, request.SocialIntent.ChannelName, StringComparison.OrdinalIgnoreCase))
        {
            throw AppServerErrors.InvalidParams("Social target channelName does not match the binding request.");
        }

        return new SocialChannelTargetWire
        {
            ChannelName = channelName,
            AccountId = NormalizeNullable(socialTarget.AccountId),
            ConversationKind = socialTarget.ConversationKind.Trim().ToLowerInvariant(),
            ConversationId = socialTarget.ConversationId.Trim(),
            DeliveryTarget = socialTarget.DeliveryTarget.Trim(),
            DisplayName = NormalizeNullable(socialTarget.DisplayName),
            BoundBy = socialTarget.BoundBy == null
                ? null
                : new SocialChannelBoundByWire
                {
                    PlatformUserId = socialTarget.BoundBy.PlatformUserId.Trim(),
                    DisplayName = NormalizeNullable(socialTarget.BoundBy.DisplayName)
                }
        };
    }

    private static void EnsureNoActiveSocialTargetConflict(
        AppBindingStateDocument state,
        string appId,
        SocialChannelTargetWire socialTarget)
    {
        var now = DateTimeOffset.UtcNow;
        var conflict = state.Bindings.FirstOrDefault(binding =>
            binding.State == AppBindingStates.Active
            && string.Equals(binding.AppId, appId, StringComparison.Ordinal)
            && string.Equals(binding.BindingKind, AppBindingKinds.SocialChannel, StringComparison.Ordinal)
            && (binding.ExpiresAt == null || binding.ExpiresAt > now)
            && binding.SocialTarget != null
            && SocialTargetMatches(binding.SocialTarget!, socialTarget));
        if (conflict != null)
            throw AppServerErrors.InvalidParams("Social channel conversation is already bound to another active thread.");
    }

    private static bool SocialTargetMatches(SocialChannelTargetWire target, AppSocialBindingResolveParams p) =>
        string.Equals(target.ChannelName, p.ChannelName.Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals(target.AccountId ?? string.Empty, NormalizeNullable(p.AccountId) ?? string.Empty, StringComparison.Ordinal)
        && string.Equals(target.ConversationKind, p.ConversationKind.Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals(target.ConversationId, p.ConversationId.Trim(), StringComparison.Ordinal);

    private static bool SocialTargetMatches(SocialChannelTargetWire left, SocialChannelTargetWire right) =>
        string.Equals(left.ChannelName, right.ChannelName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.AccountId ?? string.Empty, right.AccountId ?? string.Empty, StringComparison.Ordinal)
        && string.Equals(left.ConversationKind, right.ConversationKind, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.ConversationId, right.ConversationId, StringComparison.Ordinal);

    private static AppBindingRequestRecord? ResolvePendingBindingRequest(
        AppBindingStateDocument state,
        string? appId,
        string? bindingRequestId,
        string token)
    {
        var candidates = state.BindingRequests.Where(request =>
            request.State == AppBindingStates.Pending
            && !request.Consumed
            && (string.IsNullOrWhiteSpace(appId) || string.Equals(request.AppId, appId.Trim(), StringComparison.Ordinal))
            && (string.IsNullOrWhiteSpace(bindingRequestId)
                || string.Equals(request.BindingRequestId, bindingRequestId.Trim(), StringComparison.Ordinal)));
        return candidates.FirstOrDefault(request => AppBindingToken.Matches(token.Trim(), request.RequestTokenHash));
    }

    private static AppHandoffWire BuildSocialHandoff(
        AppDescriptor descriptor,
        SocialBindingIntentWire socialIntent,
        string bindCode)
        => new()
        {
            Mode = "bindCode",
            BindCode = bindCode,
            Instructions = $"Send /bind {bindCode} in the target {descriptor.DisplayName} conversation to connect this thread."
        };

    private static string NewBindCode() =>
        $"DTC-{RandomNumberGenerator.GetInt32(0, 1_000_000):D6}";

    private static string? NormalizeNullable(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    public ThreadAppBindingsListResult ListThreadBindings(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        string threadId,
        bool includeRevoked)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");

        var state = stores.GetStore(workspaceCraftPath).Snapshot();
        var bindings = state.Bindings
            .Where(binding => string.Equals(binding.ThreadId, threadId, StringComparison.Ordinal)
                              && (includeRevoked || binding.State != AppBindingStates.Revoked))
            .Select(binding => owner.MapBinding(
                binding,
                catalog.Entries.FirstOrDefault(entry => string.Equals(entry.Descriptor.AppId, binding.AppId, StringComparison.Ordinal))?.Descriptor,
                AppBindingService.MapConnectionStatus(state, binding.UserId, binding.AppId)))
            .Concat(state.BindingRequests
                .Where(request => string.Equals(request.ThreadId, threadId, StringComparison.Ordinal)
                                  && request.State == AppBindingStates.Pending
                                  && request.ExpiresAt > DateTimeOffset.UtcNow)
                .Select(request =>
                {
                    var descriptor = catalog.Entries
                        .FirstOrDefault(entry => string.Equals(entry.Descriptor.AppId, request.AppId, StringComparison.Ordinal))
                        ?.Descriptor;
                    return owner.MapPendingBindingRequest(
                        request,
                        descriptor,
                        AppBindingService.MapConnectionStatus(state, request.UserId, request.AppId));
                }))
            .OrderByDescending(binding => binding.LastChangedAt)
            .ToList();
        return new ThreadAppBindingsListResult { Bindings = bindings };
    }

    public IReadOnlyList<ThreadAppBindingWire> ListBindingsForAppUser(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        string userId,
        string appId,
        bool includeRevoked)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw AppServerErrors.InvalidParams("'appId' is required.");

        var descriptor = FindApp(catalog, appId).Descriptor;
        var state = stores.GetStore(workspaceCraftPath).Snapshot();
        var connection = AppBindingService.MapConnectionStatus(state, userId, appId);
        return state.Bindings
            .Where(binding => string.Equals(binding.UserId, userId, StringComparison.Ordinal)
                              && string.Equals(binding.AppId, appId, StringComparison.Ordinal)
                              && (includeRevoked || binding.State != AppBindingStates.Revoked))
            .Select(binding => owner.MapBinding(binding, descriptor, connection))
            .OrderBy(binding => binding.ThreadId, StringComparer.Ordinal)
            .ThenBy(binding => binding.BindingId, StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyList<ThreadAppBindingWire> MoveActiveBindingsOfflineForApps(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        IReadOnlyCollection<string> appIds,
        string diagnostic,
        string auditEvent)
    {
        if (appIds.Count == 0)
            return [];

        var appIdSet = appIds.ToHashSet(StringComparer.Ordinal);
        var descriptors = catalog.Entries
            .Where(entry => appIdSet.Contains(entry.Descriptor.AppId))
            .GroupBy(entry => entry.Descriptor.AppId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Descriptor, StringComparer.Ordinal);

        return stores.GetStore(workspaceCraftPath).Update(state =>
        {
            var now = DateTimeOffset.UtcNow;
            var moved = new List<ThreadAppBindingWire>();
            foreach (var binding in state.Bindings.Where(binding =>
                         appIdSet.Contains(binding.AppId)
                         && binding.State == AppBindingStates.Active))
            {
                binding.State = AppBindingStates.Offline;
                binding.LastChangedAt = now;
                binding.Diagnostic = diagnostic;
                binding.ExposureRevision++;
                attachments.Remove(binding.BindingId);
                AppBindingService.AddAudit(state, auditEvent, binding.ThreadId, binding.BindingId, binding.AppId, binding.UserId, diagnostic);
                descriptors.TryGetValue(binding.AppId, out var descriptor);
                moved.Add(owner.MapBinding(
                    binding,
                    descriptor,
                    AppBindingService.MapConnectionStatus(state, binding.UserId, binding.AppId)));
            }

            return moved;
        });
    }

    public List<ThreadAppBindingSummaryWire> ListThreadBindingSummaries(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        string threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            return [];

        return ListThreadBindings(catalog, workspaceCraftPath, threadId, includeRevoked: false)
            .Bindings
            .Select(AppBindingService.MapSummary)
            .ToList();
    }

    public IReadOnlyList<ThreadAppBindingWire> RevokeBindingsForDeletedThread(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        string threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            return [];

        return stores.GetStore(workspaceCraftPath).Update(state =>
        {
            var now = DateTimeOffset.UtcNow;
            var revoked = new List<ThreadAppBindingWire>();
            foreach (var binding in state.Bindings.Where(binding =>
                         string.Equals(binding.ThreadId, threadId, StringComparison.Ordinal)
                         && binding.State != AppBindingStates.Revoked))
            {
                binding.State = AppBindingStates.Revoked;
                binding.LastChangedAt = now;
                binding.Diagnostic = "The thread was deleted.";
                binding.ExposureRevision++;
                attachments.Remove(binding.BindingId);
                AppBindingService.AddAudit(state, "binding.revoked.threadDeleted", binding.ThreadId, binding.BindingId, binding.AppId, binding.UserId, null);

                var descriptor = catalog.Entries
                    .FirstOrDefault(entry => string.Equals(entry.Descriptor.AppId, binding.AppId, StringComparison.Ordinal))
                    ?.Descriptor;
                revoked.Add(owner.MapBinding(
                    binding,
                    descriptor,
                    AppBindingService.MapConnectionStatus(state, binding.UserId, binding.AppId)));
            }

            return revoked;
        });
    }

    public ThreadAppBindingRevokeResult RevokeBinding(
        string workspaceCraftPath,
        ThreadAppBindingRevokeParams p)
    {
        if (string.IsNullOrWhiteSpace(p.ThreadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");
        if (string.IsNullOrWhiteSpace(p.BindingId))
            throw AppServerErrors.InvalidParams("'bindingId' is required.");

        return stores.GetStore(workspaceCraftPath).Update(state =>
        {
            var binding = FindBinding(state, p.BindingId)
                          ?? throw AppServerErrors.InvalidParams($"Binding '{p.BindingId}' was not found.");
            if (!string.Equals(binding.ThreadId, p.ThreadId, StringComparison.Ordinal))
                throw AppServerErrors.InvalidParams("Binding does not belong to the requested thread.");

            binding.State = AppBindingStates.Revoked;
            binding.LastChangedAt = DateTimeOffset.UtcNow;
            binding.Diagnostic = p.Reason;
            binding.ExposureRevision++;
            attachments.Remove(binding.BindingId);
            AppBindingService.AddAudit(state, "binding.revoked", binding.ThreadId, binding.BindingId, binding.AppId, binding.UserId, p.Reason);
            return new ThreadAppBindingRevokeResult
            {
                BindingId = binding.BindingId,
                State = AppBindingStates.Revoked
            };
        });
    }

    public ThreadAppBindingRefreshResult RefreshBindings(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        ThreadAppBindingRefreshParams p)
    {
        if (string.IsNullOrWhiteSpace(p.ThreadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");

        var now = DateTimeOffset.UtcNow;
        return stores.GetStore(workspaceCraftPath).Update(state =>
        {
            var bindings = state.Bindings
                .Where(binding => string.Equals(binding.ThreadId, p.ThreadId, StringComparison.Ordinal)
                                  && (string.IsNullOrWhiteSpace(p.BindingId)
                                      || string.Equals(binding.BindingId, p.BindingId, StringComparison.Ordinal)))
                .ToList();
            if (!string.IsNullOrWhiteSpace(p.BindingId) && bindings.Count == 0)
                throw AppServerErrors.InvalidParams($"Binding '{p.BindingId}' was not found.");

            var results = new List<ThreadAppBindingRefreshWire>();
            foreach (var binding in bindings)
            {
                if (binding.State == AppBindingStates.Revoked)
                {
                    results.Add(AppBindingService.MapRefresh(binding));
                    continue;
                }

                if (binding.ExpiresAt is { } expiresAt && expiresAt <= now)
                {
                    binding.State = AppBindingStates.Expired;
                    binding.LastChangedAt = now;
                    binding.Diagnostic = "The app binding has expired.";
                    binding.ExposureRevision++;
                    attachments.Remove(binding.BindingId);
                    AppBindingService.AddAudit(state, "binding.expired", binding.ThreadId, binding.BindingId, binding.AppId, binding.UserId, null);
                }
                else if (!catalog.Entries.Any(entry =>
                             string.Equals(entry.Descriptor.AppId, binding.AppId, StringComparison.Ordinal)
                             && entry.Plugin.Enabled
                             && entry.Plugin.Installed))
                {
                    binding.State = AppBindingStates.Offline;
                    binding.LastChangedAt = now;
                    binding.Diagnostic = "The owning plugin is disabled or unavailable.";
                }
                else if (owner.IsManagedAppWithoutExternalConnection(binding.AppId))
                {
                    if (owner.IsManagedAppWithoutExternalConnectionReady(binding.AppId))
                    {
                        if (binding.State == AppBindingStates.Offline)
                        {
                            binding.State = AppBindingStates.Active;
                            binding.LastChangedAt = now;
                            binding.Diagnostic = null;
                            binding.ExposureRevision++;
                            AppBindingService.AddAudit(state, "binding.managed.reattached", binding.ThreadId, binding.BindingId, binding.AppId, binding.UserId, null);
                        }
                    }
                    else if (binding.State == AppBindingStates.Active)
                    {
                        binding.State = AppBindingStates.Offline;
                        binding.LastChangedAt = now;
                        binding.Diagnostic = "The managed app runtime is not connected.";
                        binding.ExposureRevision++;
                        attachments.Remove(binding.BindingId);
                        AppBindingService.AddAudit(state, "binding.managed.offline", binding.ThreadId, binding.BindingId, binding.AppId, binding.UserId, binding.Diagnostic);
                    }
                }
                else if (FindConnection(state, binding.UserId, binding.AppId) is { } connection
                         && IsConnectionUsable(connection))
                {
                    if (managedRuntimesByAppId.ContainsKey(binding.AppId))
                    {
                        if (binding.State == AppBindingStates.Offline)
                        {
                            binding.State = AppBindingStates.Active;
                            binding.LastChangedAt = now;
                            binding.Diagnostic = null;
                            binding.ExposureRevision++;
                            AppBindingService.AddAudit(state, "binding.managed.reattached", binding.ThreadId, binding.BindingId, binding.AppId, binding.UserId, null);
                        }
                    }
                    else
                    {
                        var attachmentLive = tools.TryGetLiveAttachment(binding.BindingId, out _);
                        if (binding.State == AppBindingStates.Offline && attachmentLive)
                        {
                            binding.State = AppBindingStates.Active;
                            binding.LastChangedAt = now;
                            binding.Diagnostic = null;
                            binding.ExposureRevision++;
                            AppBindingService.AddAudit(state, "binding.reattached", binding.ThreadId, binding.BindingId, binding.AppId, binding.UserId, null);
                        }
                        else if (binding.State == AppBindingStates.Active && !attachmentLive)
                        {
                            binding.State = AppBindingStates.Offline;
                            binding.LastChangedAt = now;
                            binding.Diagnostic = "The app is not running or its tool channel is unavailable.";
                            binding.ExposureRevision++;
                            AppBindingService.AddAudit(state, "binding.offline", binding.ThreadId, binding.BindingId, binding.AppId, binding.UserId, binding.Diagnostic);
                        }
                    }
                }
                else if (binding.State == AppBindingStates.Active)
                {
                    binding.State = AppBindingStates.Offline;
                    binding.LastChangedAt = now;
                    binding.Diagnostic = "The app connection is unavailable.";
                    binding.ExposureRevision++;
                    attachments.Remove(binding.BindingId);
                    AppBindingService.AddAudit(state, "binding.offline", binding.ThreadId, binding.BindingId, binding.AppId, binding.UserId, binding.Diagnostic);
                }

                results.Add(AppBindingService.MapRefresh(binding));
            }

            return new ThreadAppBindingRefreshResult { Bindings = results };
        });
    }
}
