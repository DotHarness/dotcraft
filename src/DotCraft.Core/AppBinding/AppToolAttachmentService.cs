using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Plugins;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Security;
using Microsoft.Extensions.AI;
using static DotCraft.AppBinding.AppBindingStoreAccessor;

namespace DotCraft.AppBinding;

internal sealed class AppToolAttachmentService(
    AppBindingService facade,
    AppBindingStoreAccessor stores,
    AppBindingAttachmentRegistry attachments,
    IReadOnlyDictionary<string, IManagedAppBindingRuntime> managedRuntimesByAppId)
{
    public AppBindingAttachToolsResult AttachTools(
        AppCatalogSnapshot catalog,
        string workspaceCraftPath,
        IAppServerTransport transport,
        AppServerConnection connection,
        AppBindingAttachToolsParams p)
    {
        if (string.IsNullOrWhiteSpace(p.BindingId))
            throw AppServerErrors.InvalidParams("'bindingId' is required.");
        if (string.IsNullOrWhiteSpace(p.ThreadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");
        if (string.IsNullOrWhiteSpace(p.AppId))
            throw AppServerErrors.InvalidParams("'appId' is required.");
        if (string.IsNullOrWhiteSpace(p.GrantId))
            throw AppServerErrors.InvalidParams("'grantId' is required.");
        if (p.Tools.Count == 0)
            throw AppServerErrors.InvalidParams("'tools' must not be empty.");
        if (!WireDynamicToolProxy.TryValidateSpecs(p.Tools, out var dynamicToolError))
            throw AppServerErrors.InvalidParams(dynamicToolError);

        var entry = FindEnabledApp(catalog, p.AppId);
        var warnings = new List<string>();
        return stores.GetStore(workspaceCraftPath).Update(state =>
        {
            var binding = FindBinding(state, p.BindingId)
                          ?? throw AppServerErrors.InvalidParams($"Binding '{p.BindingId}' was not found.");
            if (!string.Equals(binding.ThreadId, p.ThreadId, StringComparison.Ordinal)
                || !string.Equals(binding.AppId, p.AppId, StringComparison.Ordinal)
                || !string.Equals(binding.GrantId, p.GrantId, StringComparison.Ordinal))
            {
                throw AppServerErrors.InvalidParams("Binding attachment identifiers do not match the active binding.");
            }

            if (binding.State is not (AppBindingStates.Active or AppBindingStates.Offline))
                throw AppServerErrors.InvalidParams($"Binding '{p.BindingId}' is not active or offline.");
            if (!IsBindingConnectionUsable(state, binding))
                throw AppServerErrors.InvalidParams($"App '{binding.AppId}' is not connected for this workspace user.");

            var wasOffline = binding.State == AppBindingStates.Offline;
            var accepted = ValidateAttachedTools(entry.Descriptor, binding, p, warnings);
            binding.State = AppBindingStates.Active;
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
            binding.GrantProof = p.GrantProof?.DeepClone() as JsonObject;
            binding.LastChangedAt = DateTimeOffset.UtcNow;
            binding.Diagnostic = null;
            binding.ExposureRevision++;

            attachments.Set(binding.BindingId, transport, connection);
            if (wasOffline)
                AddAudit(state, "binding.reattached", binding.ThreadId, binding.BindingId, binding.AppId, binding.UserId, null);
            AddAudit(state, "binding.tools.attached", binding.ThreadId, binding.BindingId, binding.AppId, binding.UserId, $"{accepted.Count} tools");
            return new AppBindingAttachToolsResult
            {
                Binding = MapBinding(binding, entry.Descriptor, MapConnectionStatus(state, binding.UserId, binding.AppId)),
                AcceptedToolCount = accepted.Count,
                Warnings = warnings
            };
        });
    }

    public IReadOnlyList<AITool> CreateRuntimeToolsForThread(
        SessionThread thread,
        IReadOnlySet<string> reservedToolNames)
    {
        var workspaceCraftPath = Path.Combine(thread.WorkspacePath, ".craft");
        if (!Directory.Exists(workspaceCraftPath))
            return [];

        var state = stores.GetStore(workspaceCraftPath).Snapshot();
        var tools = new List<AITool>();
        foreach (var binding in state.Bindings.Where(binding =>
                     string.Equals(binding.ThreadId, thread.Id, StringComparison.Ordinal)
                     && binding.AttachedTools.Count > 0
                     && binding.State is AppBindingStates.Active or AppBindingStates.Offline or AppBindingStates.Expired))
        {
            foreach (var spec in binding.AttachedTools)
            {
                if (reservedToolNames.Contains(spec.Name))
                    continue;

                // App-only interactive UI tools (visibility excludes "model") are invoked via
                // ui/tool/call from their UI, never exposed to the model.
                if (!UiToolVisibility.IsModelVisible(spec.Meta?.Ui))
                    continue;

                var effectiveState = GetRuntimeBindingState(binding);
                if (managedRuntimesByAppId.ContainsKey(binding.AppId)
                    && effectiveState != AppBindingStates.Active
                    && !string.Equals(binding.BindingKind, AppBindingKinds.SocialChannel, StringComparison.Ordinal))
                {
                    continue;
                }

                tools.Add(new AppBindingRuntimeFunction(
                    this,
                    workspaceCraftPath,
                    binding.BindingId,
                    effectiveState,
                    CloneSpec(spec)));
            }
        }

        return tools;
    }

    internal async ValueTask<DynamicToolCallResult> InvokeAttachedToolAsync(
        string workspaceCraftPath,
        string bindingId,
        DynamicToolSpec spec,
        string executionThreadId,
        string executionTurnId,
        ISessionService? executionSessionService,
        string callId,
        JsonObject arguments,
        CancellationToken cancellationToken)
    {
        var state = stores.GetStore(workspaceCraftPath).Snapshot();
        var binding = FindBinding(state, bindingId);
        if (binding == null)
            return Failed(AppBindingErrorCodes.ToolUnavailable, "The app binding no longer exists.");

        var runtimeState = GetRuntimeBindingState(binding);
        if (runtimeState == AppBindingStates.Revoked)
            return Failed(AppBindingErrorCodes.Revoked, "The app binding was revoked.");
        if (runtimeState == AppBindingStates.Expired)
            return Failed(AppBindingErrorCodes.Expired, "The app binding has expired.");
        if (runtimeState != AppBindingStates.Active)
            return Failed(AppBindingErrorCodes.Offline, "The app binding is offline. Reconnect the app or refresh the binding.");

        if (managedRuntimesByAppId.TryGetValue(binding.AppId, out var managedRuntime))
        {
            try
            {
                return await managedRuntime.InvokeToolAsync(
                    new ManagedAppBindingToolCallContext(
                        workspaceCraftPath,
                        Directory.GetParent(Path.GetFullPath(workspaceCraftPath))?.FullName ?? Path.GetFullPath(workspaceCraftPath),
                        binding.BindingId,
                        executionThreadId,
                        executionTurnId,
                        callId,
                        binding.AppId,
                        binding.GrantId,
                        spec.Name)
                    {
                        AppBindingService = facade,
                        SessionService = executionSessionService
                    },
                    arguments,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Failed(AppBindingErrorCodes.ToolUnavailable, ex.Message);
            }
        }

        if (!TryGetLiveAttachment(binding.BindingId, out var attachment))
            return Failed(AppBindingErrorCodes.Offline, "The app binding is offline. Reconnect the app or refresh the binding.");

        try
        {
            var response = await attachment.Transport.SendClientRequestAsync(
                AppServerMethods.ItemToolCall,
                new DynamicToolCallParams
                {
                    ThreadId = executionThreadId,
                    TurnId = executionTurnId,
                    CallId = callId,
                    Namespace = spec.Namespace,
                    Tool = spec.Name,
                    Arguments = arguments
                },
                cancellationToken,
                TimeSpan.FromSeconds(120));

            if (response.Error.HasValue)
                return Failed(AppBindingErrorCodes.ProtocolViolation, response.Error.Value.ToString());

            if (!response.Result.HasValue)
                return Failed(AppBindingErrorCodes.ProtocolViolation, $"App-bound tool '{spec.Name}' returned no result.");

            return response.Result.Value.Deserialize<DynamicToolCallResult>(SessionWireJsonOptions.Default)
                   ?? Failed(AppBindingErrorCodes.ProtocolViolation, $"App-bound tool '{spec.Name}' returned an invalid result.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(AppBindingErrorCodes.ToolUnavailable, $"App-bound tool '{spec.Name}' timed out while waiting for app response.");
        }
        catch (Exception ex)
        {
            return Failed(AppBindingErrorCodes.ToolUnavailable, ex.Message);
        }
    }

    public string GetRuntimeBindingState(AppBindingRecord binding)
    {
        if (binding.State == AppBindingStates.Revoked)
            return AppBindingStates.Revoked;
        if (binding.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
            return AppBindingStates.Expired;
        if (binding.State != AppBindingStates.Active)
            return binding.State;
        if (managedRuntimesByAppId.TryGetValue(binding.AppId, out var runtime))
            return IsManagedRuntimeReady(runtime, binding.AppId) ? AppBindingStates.Active : AppBindingStates.Offline;
        if (!TryGetLiveAttachment(binding.BindingId, out _))
            return AppBindingStates.Offline;
        return AppBindingStates.Active;
    }

    public bool TryGetLiveAttachment(
        string bindingId,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ActiveAppBindingAttachment? attachment) =>
        attachments.TryGetLive(bindingId, out attachment);

    private bool IsBindingConnectionUsable(AppBindingStateDocument state, AppBindingRecord binding) =>
        IsManagedAppWithoutExternalConnection(binding.AppId)
            ? IsManagedAppWithoutExternalConnectionReady(binding.AppId)
            : IsConnectionUsable(FindConnection(state, binding.UserId, binding.AppId));

    private bool IsManagedAppWithoutExternalConnection(string appId) =>
        managedRuntimesByAppId.TryGetValue(appId, out var runtime)
        && runtime.RequiresExternalConnection == false;

    private bool IsManagedAppWithoutExternalConnectionReady(string appId) =>
        managedRuntimesByAppId.TryGetValue(appId, out var runtime)
        && runtime.RequiresExternalConnection == false
        && IsManagedRuntimeReady(runtime, appId);

    private static bool IsManagedRuntimeReady(IManagedAppBindingRuntime runtime, string appId) =>
        runtime.RequiresExternalConnection
        || string.Equals(runtime.GetConnectionStatus(appId).State, AppConnectionStates.Connected, StringComparison.Ordinal);

    private ThreadAppBindingWire MapBinding(
        AppBindingRecord binding,
        AppDescriptor? descriptor,
        AppConnectionStatusWire connection)
    {
        var effectiveState = binding.State;
        if (binding.State == AppBindingStates.Active
            && binding.ExpiresAt is { } expiresAt
            && expiresAt <= DateTimeOffset.UtcNow)
        {
            effectiveState = AppBindingStates.Expired;
        }

        var managedRuntime = managedRuntimesByAppId.GetValueOrDefault(binding.AppId);
        var managed = managedRuntime != null;
        var requiresExternalConnection = managedRuntime?.RequiresExternalConnection ?? true;
        var connectionStatus = AppBindingWireMapper.ResolveConnectionStatus(
            managedRuntime,
            managed,
            requiresExternalConnection,
            binding.AppId,
            connection);
        return new ThreadAppBindingWire
        {
            BindingId = binding.BindingId,
            ThreadId = binding.ThreadId,
            AppId = binding.AppId,
            GrantId = binding.GrantId,
            DisplayName = descriptor?.DisplayName,
            Icon = ResolveIconForWire(descriptor?.Icon),
            ToolNamespace = descriptor?.ToolNamespace,
            State = effectiveState,
            ConnectionState = connectionStatus.State,
            Managed = managed,
            RequiresExternalConnection = requiresExternalConnection,
            GrantedScopes = binding.GrantedScopes.ToList(),
            AttachedToolCount = binding.AttachedTools.Count,
            ExpiresAt = binding.ExpiresAt,
            LastChangedAt = binding.LastChangedAt,
            ApprovalMode = binding.ApprovalMode,
            AuditRef = binding.AuditRef,
            Diagnostic = binding.Diagnostic,
            BindingKind = binding.BindingKind,
            SocialTarget = binding.SocialTarget,
            ExposureRevision = binding.ExposureRevision
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

    private static List<DynamicToolSpec> ValidateAttachedTools(
        AppDescriptor descriptor,
        AppBindingRecord binding,
        AppBindingAttachToolsParams p,
        List<string> warnings,
        bool allowDirectMutatingToolExposure = false)
    {
        var catalogByName = descriptor.ToolCatalog.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        AddDynamicAttachedToolCatalog(descriptor, p.ToolCatalog, catalogByName);
        var grantedScopes = binding.GrantedScopes.ToHashSet(StringComparer.Ordinal);
        var accepted = new List<DynamicToolSpec>();
        var direct = p.DirectToolNames?.ToHashSet(StringComparer.Ordinal) ?? [];
        var deferred = p.DeferredToolNames?.ToHashSet(StringComparer.Ordinal) ?? [];

        foreach (var tool in p.Tools)
        {
            if (!string.Equals(tool.Namespace, descriptor.ToolNamespace, StringComparison.Ordinal))
                throw AppServerErrors.InvalidParams($"Attached tool '{tool.Name}' must use namespace '{descriptor.ToolNamespace}'.");

            if (!catalogByName.TryGetValue(tool.Name, out var catalogEntry))
                throw AppServerErrors.InvalidParams($"Attached tool '{tool.Name}' is not declared in the app tool catalog.");

            if (!grantedScopes.Contains(catalogEntry.Scope))
                throw AppServerErrors.InvalidParams($"Attached tool '{tool.Name}' requires ungranted scope '{catalogEntry.Scope}'.");

            var clone = CloneSpec(tool);
            var requestedDirect = direct.Contains(tool.Name);
            var requestedDeferred = deferred.Contains(tool.Name);
            if (requestedDirect && requestedDeferred)
                warnings.Add($"Tool '{tool.Name}' was listed as both direct and deferred; deferred wins.");

            var enforceDeferredForRisk = AppBindingRisks.Rank(catalogEntry.Risk) > AppBindingRisks.Rank(AppBindingRisks.Read)
                && !allowDirectMutatingToolExposure;

            if (enforceDeferredForRisk
                && requestedDirect
                && !requestedDeferred)
            {
                warnings.Add($"Tool '{tool.Name}' is {catalogEntry.Risk}; deferred exposure was enforced.");
            }

            clone.DeferLoading = requestedDeferred
                || enforceDeferredForRisk
                || (!requestedDirect && string.Equals(catalogEntry.DefaultExposure, AppBindingExposures.Deferred, StringComparison.Ordinal));
            accepted.Add(clone);
        }

        return accepted;
    }

    private static void AddDynamicAttachedToolCatalog(
        AppDescriptor descriptor,
        IReadOnlyList<AppToolCatalogEntry>? dynamicCatalog,
        Dictionary<string, AppToolCatalogEntry> catalogByName)
    {
        if (dynamicCatalog is not { Count: > 0 })
            return;

        if (!descriptor.DynamicToolCatalog.Enabled)
            throw AppServerErrors.InvalidParams($"App '{descriptor.AppId}' does not allow dynamic app tool catalogs.");

        var scopeById = descriptor.Scopes.ToDictionary(scope => scope.Id, StringComparer.Ordinal);
        foreach (var tool in dynamicCatalog)
        {
            if (!PluginManifestParser.IsValidFunctionName(tool.Name)
                || string.IsNullOrWhiteSpace(tool.Scope)
                || !AppBindingRisks.IsKnown(tool.Risk)
                || !AppBindingExposures.IsKnown(tool.DefaultExposure))
            {
                throw AppServerErrors.InvalidParams("Dynamic app tool catalog entries require a valid name, scope, risk, and defaultExposure.");
            }

            if (!scopeById.TryGetValue(tool.Scope, out var scope))
                throw AppServerErrors.InvalidParams($"Dynamic app tool '{tool.Name}' references unknown scope '{tool.Scope}'.");

            if (AppBindingRisks.Rank(tool.Risk) < AppBindingRisks.Rank(scope.Risk))
                throw AppServerErrors.InvalidParams($"Dynamic app tool '{tool.Name}' risk must not be lower than scope '{tool.Scope}' risk.");

            if (catalogByName.ContainsKey(tool.Name))
                throw AppServerErrors.InvalidParams($"Dynamic app tool '{tool.Name}' is declared more than once.");

            catalogByName.Add(tool.Name, tool);
        }
    }

    private static DynamicToolSpec CloneSpec(DynamicToolSpec spec) =>
        new()
        {
            Namespace = spec.Namespace,
            Name = spec.Name,
            Description = spec.Description,
            InputSchema = spec.InputSchema?.DeepClone() as JsonObject,
            DeferLoading = spec.DeferLoading,
            Approval = spec.Approval == null
                ? null
                : new ChannelToolApprovalDescriptor
                {
                    Kind = spec.Approval.Kind,
                    TargetArgument = spec.Approval.TargetArgument,
                    Operation = spec.Approval.Operation,
                    OperationArgument = spec.Approval.OperationArgument
                },
            Meta = spec.Meta
        };

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

    private static string? ResolveIconForWire(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
            return null;
        if (icon.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || icon.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || icon.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return icon;
        }

        try
        {
            if (!Path.IsPathFullyQualified(icon) || !File.Exists(icon))
                return icon;

            var mimeType = Path.GetExtension(icon).ToLowerInvariant() switch
            {
                ".svg" => "image/svg+xml",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                ".ico" => "image/x-icon",
                _ => "application/octet-stream"
            };
            return $"data:{mimeType};base64,{Convert.ToBase64String(File.ReadAllBytes(icon))}";
        }
        catch
        {
            return icon;
        }
    }

    internal static DynamicToolCallResult Failed(string code, string message) =>
        new()
        {
            Success = false,
            ErrorCode = code,
            ErrorMessage = message,
            ContentItems = [new ExtChannelToolContentItem { Type = "text", Text = $"{code}: {message}" }]
        };

    private sealed class AppBindingRuntimeFunction(
        AppToolAttachmentService service,
        string workspaceCraftPath,
        string bindingId,
        string bindingState,
        DynamicToolSpec spec) : AIFunction, IDynamicToolRuntimeTool
    {
        private readonly JsonElement _jsonSchema = ToJsonElement(spec.InputSchema ?? new JsonObject { ["type"] = "object" });

        public DynamicToolSpec Spec => spec;

        public override string Name => spec.Name;

        public override string Description => spec.Description;

        public override JsonElement JsonSchema => _jsonSchema;

        public override JsonElement? ReturnJsonSchema => null;

        public override MethodInfo? UnderlyingMethod => null;

        public override JsonSerializerOptions JsonSerializerOptions => SessionWireJsonOptions.Default;

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            var scope = PluginFunctionExecutionScope.Current
                        ?? throw new InvalidOperationException("App-bound dynamic tools require an active turn scope.");

            var callId = $"appdyntool_{Guid.NewGuid():N}";
            var argsObject = ToJsonObject(arguments);
            var item = new SessionItem
            {
                Id = SessionIdGenerator.NewItemId(scope.NextItemSequence()),
                TurnId = scope.TurnId,
                Type = ItemType.DynamicToolCall,
                Status = ItemStatus.Started,
                CreatedAt = DateTimeOffset.UtcNow,
                Payload = CreatePayload(callId, argsObject)
            };
            scope.Turn.Items.Add(item);
            scope.EmitItemStarted(item);

            var inputSchema = spec.InputSchema ?? new JsonObject { ["type"] = "object" };
            if (!PluginFunctionSchemaValidator.TryValidateArguments(inputSchema, argsObject, out var validationError))
                return FinalizeFailure(item, scope, callId, argsObject, "InvalidArguments", validationError);

            var unavailable = bindingState switch
            {
                AppBindingStates.Offline => (AppBindingErrorCodes.Offline, "The app binding is offline. Reconnect the app or refresh the binding."),
                AppBindingStates.Expired => (AppBindingErrorCodes.Expired, "The app binding has expired."),
                AppBindingStates.Revoked => (AppBindingErrorCodes.Revoked, "The app binding was revoked."),
                _ => ((string, string)?)null
            };
            if (unavailable != null)
                return FinalizeFailure(item, scope, callId, argsObject, unavailable.Value.Item1, unavailable.Value.Item2);

            var approvalFailure = await ApplyServerApprovalAsync(scope, argsObject, cancellationToken);
            if (approvalFailure != null)
                return FinalizeFailure(item, scope, callId, argsObject, approvalFailure.Value.ErrorCode, approvalFailure.Value.ErrorMessage);

            var result = await service.InvokeAttachedToolAsync(
                workspaceCraftPath,
                bindingId,
                spec,
                scope.ThreadId,
                scope.TurnId,
                scope.SessionService,
                callId,
                argsObject,
                cancellationToken);
            item.Status = ItemStatus.Completed;
            item.CompletedAt = DateTimeOffset.UtcNow;
            item.Payload = CreatePayload(callId, argsObject, result);
            scope.EmitItemCompleted(item);

            return MapToolResultToModelValue(result);
        }

        private DynamicToolCallPayload CreatePayload(
            string callId,
            JsonObject argsObject,
            DynamicToolCallResult? result = null)
            => new()
            {
                Namespace = spec.Namespace,
                ToolName = spec.Name,
                CallId = callId,
                Arguments = argsObject.DeepClone() as JsonObject,
                ContentItems = result?.ContentItems?.Select(MapContentItem).ToArray(),
                StructuredResult = result?.StructuredResult?.DeepClone(),
                Success = result?.Success ?? false,
                ErrorCode = result?.ErrorCode,
                ErrorMessage = result?.ErrorMessage,
                Meta = result?.Meta?.DeepClone(),
                Ui = spec.Meta?.Ui is { } ui
                    ? JsonSerializer.SerializeToNode(ui, SessionWireJsonOptions.Default)
                    : null
            };

        private async Task<(string ErrorCode, string ErrorMessage)?> ApplyServerApprovalAsync(
            PluginFunctionExecutionContext scope,
            JsonObject argsObject,
            CancellationToken cancellationToken)
        {
            var approval = spec.Approval;
            if (approval == null)
                return null;

            var targetState = ApprovalArgumentResolver.ResolveTargetArgument(
                argsObject,
                spec.InputSchema,
                approval.TargetArgument,
                out var approvalTarget);
            if (targetState == ApprovalTargetArgumentState.MissingOptional)
                return null;
            if (targetState == ApprovalTargetArgumentState.MissingRequired)
            {
                return (
                    "InvalidArguments",
                    $"App-bound tool '{spec.Name}' requires string argument '{approval.TargetArgument}' for approval routing.");
            }

            if (!TryResolveApprovalOperation(argsObject, approval, out var approvalOperation, out var operationError))
                return ("InvalidArguments", operationError);

            return approval.Kind.ToLowerInvariant() switch
            {
                "file" => await GuardFileAccessAsync(scope, approvalTarget, approvalOperation, cancellationToken),
                "shell" => await GuardShellAccessAsync(scope, approvalTarget, approvalOperation),
                "remoteresource" => await GuardRemoteResourceAccessAsync(scope, approvalTarget, approvalOperation),
                _ => (
                    AppBindingErrorCodes.ProtocolViolation,
                    $"App-bound tool '{spec.Name}' uses unsupported approval kind '{approval.Kind}'.")
            };
        }

        private bool TryResolveApprovalOperation(
            JsonObject argsObject,
            ChannelToolApprovalDescriptor approval,
            out string operation,
            out string error)
        {
            if (!string.IsNullOrWhiteSpace(approval.Operation))
            {
                operation = approval.Operation!;
                error = string.Empty;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(approval.OperationArgument)
                && ApprovalArgumentResolver.TryReadStringArgument(argsObject, approval.OperationArgument!, out var operationArgument))
            {
                operation = operationArgument;
                error = string.Empty;
                return true;
            }

            operation = string.Empty;
            error = $"App-bound tool '{spec.Name}' could not resolve approval operation metadata.";
            return false;
        }

        private object FinalizeFailure(
            SessionItem item,
            PluginFunctionExecutionContext scope,
            string callId,
            JsonObject argsObject,
            string errorCode,
            string errorMessage)
        {
            var result = Failed(errorCode, errorMessage);
            item.Status = ItemStatus.Completed;
            item.CompletedAt = DateTimeOffset.UtcNow;
            item.Payload = CreatePayload(callId, argsObject, result);
            scope.EmitItemCompleted(item);
            return MapToolResultToModelValue(result);
        }

        private static async Task<(string ErrorCode, string ErrorMessage)?> GuardFileAccessAsync(
            PluginFunctionExecutionContext scope,
            string path,
            string operation,
            CancellationToken cancellationToken)
        {
            var userDotCraftPath = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".craft"));
            var guard = new FileAccessGuard(
                scope.WorkspacePath,
                requireApprovalOutsideWorkspace: scope.RequireApprovalOutsideWorkspace,
                approvalService: scope.ApprovalService,
                blacklist: scope.PathBlacklist,
                trustedReadPaths: [userDotCraftPath]);
            var resolvedPath = guard.ResolvePath(path);
            var error = await guard.ValidatePathAsync(resolvedPath, operation, path, cancellationToken);
            return error == null ? null : ("AccessDenied", error);
        }

        private static async Task<(string ErrorCode, string ErrorMessage)?> GuardShellAccessAsync(
            PluginFunctionExecutionContext scope,
            string workingDirectory,
            string command)
        {
            var normalizedCommand = command.Trim();
            if (string.IsNullOrWhiteSpace(normalizedCommand))
                return ("InvalidArguments", "Shell approval routing requires a non-empty command string.");

            if (scope.PathBlacklist != null && scope.PathBlacklist.CommandReferencesBlacklistedPath(normalizedCommand))
                return ("AccessDenied", "Error: Command references a blacklisted path and cannot be executed.");

            var resolvedWorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? scope.WorkspacePath
                : ResolveAgainstWorkspace(scope.WorkspacePath, workingDirectory);
            var hasPathTraversal = normalizedCommand.Contains("..\\", StringComparison.Ordinal)
                || normalizedCommand.Contains("../", StringComparison.Ordinal);
            var isOutsideWorkspace = !IsWithinBoundary(resolvedWorkingDirectory, scope.WorkspacePath);

            if (!hasPathTraversal && !isOutsideWorkspace)
                return null;

            if (!scope.RequireApprovalOutsideWorkspace)
            {
                if (hasPathTraversal)
                    return ("AccessDenied", "Error: Command blocked by safety guard (path traversal detected).");
                return ("AccessDenied", "Error: Working directory is outside workspace boundary.");
            }

            var approved = await scope.ApprovalService.RequestShellApprovalAsync(
                normalizedCommand,
                resolvedWorkingDirectory,
                ApprovalContextScope.Current);
            return approved ? null : ("AccessDenied", "Error: Command execution was rejected by user.");
        }

        private static async Task<(string ErrorCode, string ErrorMessage)?> GuardRemoteResourceAccessAsync(
            PluginFunctionExecutionContext scope,
            string target,
            string operation)
        {
            var normalizedTarget = target.Trim();
            if (string.IsNullOrWhiteSpace(normalizedTarget))
                return ("InvalidArguments", "Remote resource approval routing requires a non-empty target string.");

            var normalizedOperation = operation.Trim();
            if (string.IsNullOrWhiteSpace(normalizedOperation))
                return ("InvalidArguments", "Remote resource approval routing requires a non-empty operation string.");

            var approved = await scope.ApprovalService.RequestResourceApprovalAsync(
                "remoteResource",
                normalizedOperation,
                normalizedTarget,
                ApprovalContextScope.Current);
            return approved ? null : ("AccessDenied", "Error: Remote resource operation was rejected by user.");
        }

        private static object MapToolResultToModelValue(DynamicToolCallResult result)
        {
            if (result.ContentItems is { Count: > 0 } contentItems)
            {
                var aiContents = new List<AIContent>();
                foreach (var item in contentItems)
                {
                    if (string.Equals(item.Type, "text", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(item.Text))
                    {
                        aiContents.Add(new TextContent(item.Text));
                    }
                    else if (string.Equals(item.Type, "image", StringComparison.OrdinalIgnoreCase)
                             && !string.IsNullOrWhiteSpace(item.DataBase64)
                             && !string.IsNullOrWhiteSpace(item.MediaType))
                    {
                        try
                        {
                            aiContents.Add(new DataContent(Convert.FromBase64String(item.DataBase64), item.MediaType));
                        }
                        catch (FormatException)
                        {
                            aiContents.Add(new TextContent("[Invalid app-bound dynamic tool image payload]"));
                        }
                    }
                }

                if (aiContents.Count > 0)
                {
                    if (result.StructuredResult != null)
                        aiContents.Add(new TextContent(result.StructuredResult.ToJsonString(SessionWireJsonOptions.Default)));

                    return aiContents;
                }
            }

            if (result.StructuredResult != null)
            {
                return new
                {
                    result.Success,
                    result.ContentItems,
                    result.StructuredResult,
                    result.ErrorCode,
                    result.ErrorMessage
                };
            }

            if (!result.Success)
            {
                var error = result.ErrorMessage ?? "App-bound dynamic tool call failed.";
                return string.IsNullOrWhiteSpace(result.ErrorCode) ? error : $"{result.ErrorCode}: {error}";
            }

            return "App-bound dynamic tool completed.";
        }

        private static PluginFunctionContentItem MapContentItem(ExtChannelToolContentItem item)
            => new()
            {
                Type = item.Type,
                Text = item.Text,
                DataBase64 = item.DataBase64,
                MediaType = item.MediaType
            };

        private static JsonObject ToJsonObject(AIFunctionArguments arguments)
        {
            var root = new JsonObject();
            foreach (var (key, value) in arguments)
                root[key] = value is JsonNode node ? node.DeepClone() : JsonSerializer.SerializeToNode(value, SessionWireJsonOptions.Default);
            return root;
        }

        private static JsonElement ToJsonElement(JsonNode node)
            => JsonSerializer.Deserialize<JsonElement>(node.ToJsonString(SessionWireJsonOptions.Default), SessionWireJsonOptions.Default);

        private static string ResolveAgainstWorkspace(string workspacePath, string path)
            => Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(workspacePath, path));

        private static bool IsWithinBoundary(string fullPath, string boundaryRoot)
        {
            var resolvedPath = Path.GetFullPath(fullPath);
            var resolvedBoundary = Path.GetFullPath(boundaryRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (resolvedPath.Equals(resolvedBoundary, StringComparison.OrdinalIgnoreCase))
                return true;

            return resolvedPath.StartsWith(resolvedBoundary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                   || resolvedPath.StartsWith(resolvedBoundary + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }
}
