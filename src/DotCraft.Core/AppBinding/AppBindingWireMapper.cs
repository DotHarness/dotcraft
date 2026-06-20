using System.Text.Json.Nodes;
using DotCraft.Plugins;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using static DotCraft.AppBinding.AppBindingStoreAccessor;

namespace DotCraft.AppBinding;

internal sealed class AppBindingWireMapper(
    IReadOnlyDictionary<string, IManagedAppBindingRuntime> managedRuntimesByAppId)
{
    public static bool IsVisibleOnAppListSurface(AppCatalogEntry entry, string surface)
    {
        if (entry.ManagedRuntime == null)
            return true;

        return entry.Plugin.Installed
               && entry.Plugin.Enabled
               && entry.ManagedRuntime.Surfaces.Contains(surface);
    }

    public AppInfoWire MapAppInfo(
        AppCatalogEntry entry,
        AppBindingStateDocument state,
        string userId,
        string? threadId,
        string surface)
    {
        var managedRuntime = entry.ManagedRuntime == null
            ? null
            : managedRuntimesByAppId.GetValueOrDefault(entry.Descriptor.AppId);
        var descriptor = managedRuntime?.GetCatalogDescriptor(surface) ?? entry.Descriptor;
        var managed = entry.ManagedRuntime != null;
        var requiresExternalConnection = entry.ManagedRuntime?.RequiresExternalConnection ?? true;
        var diagnostics = managedRuntime == null
            ? entry.Diagnostics
            : entry.Diagnostics.Concat(managedRuntime.GetCatalogDiagnostics(surface)).ToArray();
        var connectionStatus = ResolveConnectionStatus(
            managedRuntime,
            managed,
            requiresExternalConnection,
            descriptor.AppId,
            MapConnectionStatus(state, userId, descriptor.AppId));
        var connection = managed && !requiresExternalConnection ? null : FindConnection(state, userId, descriptor.AppId);
        var binding = string.IsNullOrWhiteSpace(threadId)
            ? null
            : state.Bindings
                .Where(candidate => string.Equals(candidate.ThreadId, threadId, StringComparison.Ordinal)
                                    && string.Equals(candidate.AppId, descriptor.AppId, StringComparison.Ordinal)
                                    && candidate.State != AppBindingStates.Revoked)
                .OrderByDescending(candidate => candidate.LastChangedAt)
                .FirstOrDefault();

        var icon = ResolveIconForWire(descriptor.Icon) ?? ResolvePluginInterfaceIconForWire(entry.Plugin.Manifest);
        return new AppInfoWire
        {
            AppId = descriptor.AppId,
            ToolNamespace = descriptor.ToolNamespace,
            DisplayName = descriptor.DisplayName,
            DeveloperName = descriptor.DeveloperName,
            Description = descriptor.Description,
            Category = descriptor.Category,
            Icon = icon,
            PluginId = entry.Plugin.Manifest.Id,
            Installed = entry.Plugin.Installed,
            Enabled = entry.Plugin.Enabled,
            CatalogVisible = true,
            Managed = managed,
            RequiresExternalConnection = requiresExternalConnection,
            ReleasePage = descriptor.ReleasePage,
            DownloadUrl = descriptor.DownloadUrl,
            NativeApp = new AppNativeApplicationWire
            {
                DisplayName = string.IsNullOrWhiteSpace(descriptor.NativeApplication.DisplayName)
                    ? descriptor.DisplayName
                    : descriptor.NativeApplication.DisplayName,
                Protocol = descriptor.NativeApplication.Protocol,
                InstallUrl = descriptor.NativeApplication.InstallUrl ?? descriptor.ReleasePage ?? descriptor.DownloadUrl,
                Status = managed && !requiresExternalConnection
                    ? AppNativeApplicationStates.Installed
                    : AppNativeApplicationStates.Unknown
            },
            ConnectionState = connectionStatus.State,
            AccountLabel = connection?.AccountLabel,
            HandoffModes = descriptor.Connection.HandoffModes,
            Scopes = descriptor.Scopes,
            ToolCatalog = descriptor.ToolCatalog,
            DynamicToolCatalog = new AppDynamicToolCatalogDescriptor
            {
                Enabled = descriptor.DynamicToolCatalog.Enabled,
                Description = descriptor.DynamicToolCatalog.Description
            },
            BindingSummary = binding == null
                ? null
                : new ThreadAppBindingSummaryWire
                {
                    ThreadId = binding.ThreadId,
                    BindingId = binding.BindingId,
                    AppId = binding.AppId,
                    DisplayName = descriptor.DisplayName,
                    Icon = icon,
                    ToolNamespace = descriptor.ToolNamespace,
                    State = binding.State,
                    ConnectionState = connectionStatus.State,
                    Managed = managed,
                    RequiresExternalConnection = requiresExternalConnection,
                    GrantedScopes = binding.GrantedScopes.ToList(),
                    ExpiresAt = binding.ExpiresAt,
                    BindingKind = binding.BindingKind,
                    SocialTarget = binding.SocialTarget,
                    ExposureRevision = binding.ExposureRevision
                },
            Diagnostics = diagnostics.Select(MapDiagnostic).ToList()
        };
    }

    public ThreadOriginAppWire? ResolveOriginApp(AppCatalogSnapshot catalog, string? originChannel, string? channelContext = null)
    {
        if (string.IsNullOrWhiteSpace(originChannel))
            return null;

        var entry = catalog.Entries
            .Where(candidate => (candidate.Plugin.Installed || candidate.Plugin.Installable)
                                && !string.IsNullOrWhiteSpace(candidate.Descriptor.OriginChannel)
                                && string.Equals(candidate.Descriptor.OriginChannel, originChannel, StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.Descriptor.AppId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (entry is null)
            return null;

        var member = ResolveOriginMember(entry.Descriptor, channelContext);
        if (member is not null)
        {
            return new ThreadOriginAppWire
            {
                AppId = entry.Descriptor.AppId,
                DisplayName = member.DisplayName,
                Icon = ResolvePluginRelativeIconForWire(entry, member.Icon),
                MemberId = member.Match
            };
        }

        return new ThreadOriginAppWire
        {
            AppId = entry.Descriptor.AppId,
            DisplayName = entry.Descriptor.DisplayName,
            Icon = ResolveIconForWire(entry.Descriptor.Icon)
                   ?? ResolvePluginInterfaceIconForWire(entry.Plugin.Manifest)
        };
    }

    public ThreadAppBindingWire MapBinding(
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
        var connectionStatus = ResolveConnectionStatus(
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

    public ThreadAppBindingWire MapPendingBindingRequest(
        AppBindingRequestRecord request,
        AppDescriptor? descriptor,
        AppConnectionStatusWire connection)
    {
        var managedRuntime = managedRuntimesByAppId.GetValueOrDefault(request.AppId);
        var managed = managedRuntime != null;
        var requiresExternalConnection = managedRuntime?.RequiresExternalConnection ?? true;
        var connectionStatus = ResolveConnectionStatus(
            managedRuntime,
            managed,
            requiresExternalConnection,
            request.AppId,
            connection);
        return new ThreadAppBindingWire
        {
            BindingRequestId = request.BindingRequestId,
            BindingId = request.BindingRequestId,
            ThreadId = request.ThreadId,
            AppId = request.AppId,
            DisplayName = descriptor?.DisplayName,
            Icon = ResolveIconForWire(descriptor?.Icon),
            ToolNamespace = descriptor?.ToolNamespace,
            State = AppBindingStates.Pending,
            ConnectionState = connectionStatus.State,
            Managed = managed,
            RequiresExternalConnection = requiresExternalConnection,
            GrantedScopes = [],
            AttachedToolCount = 0,
            ExpiresAt = request.ExpiresAt,
            LastChangedAt = request.CreatedAt,
            Diagnostic = request.Reason,
            BindingKind = request.BindingKind
        };
    }

    public static ThreadAppBindingRefreshWire MapRefresh(AppBindingRecord binding) =>
        new()
        {
            BindingId = binding.BindingId,
            State = binding.State,
            AttachedToolCount = binding.AttachedTools.Count
        };

    public static ThreadAppBindingSummaryWire MapSummary(ThreadAppBindingWire binding) =>
        new()
        {
            ThreadId = binding.ThreadId,
            BindingRequestId = binding.BindingRequestId,
            BindingId = binding.BindingId,
            AppId = binding.AppId,
            DisplayName = binding.DisplayName,
            Icon = binding.Icon,
            ToolNamespace = binding.ToolNamespace,
            State = binding.State,
            ConnectionState = binding.ConnectionState,
            Managed = binding.Managed,
            RequiresExternalConnection = binding.RequiresExternalConnection,
            GrantedScopes = binding.GrantedScopes.ToList(),
            ExpiresAt = binding.ExpiresAt,
            BindingKind = binding.BindingKind,
            SocialTarget = binding.SocialTarget,
            ExposureRevision = binding.ExposureRevision
        };

    internal static AppConnectionStatusWire ResolveConnectionStatus(
        IManagedAppBindingRuntime? managedRuntime,
        bool managed,
        bool requiresExternalConnection,
        string appId,
        AppConnectionStatusWire fallback)
    {
        if (!managed || requiresExternalConnection)
            return fallback;

        var status = managedRuntime?.GetConnectionStatus(appId)
                     ?? new AppConnectionStatusWire { AppId = appId, State = AppConnectionStates.Connected };
        if (string.IsNullOrWhiteSpace(status.AppId))
            status.AppId = appId;
        return status;
    }

    public static AppConnectionStatusWire MapConnectionStatus(AppConnectionRecord? connection, string? appId = null)
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

    public static AppConnectionStatusWire MapConnectionStatus(
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

    private static AppOriginMemberDescriptor? ResolveOriginMember(AppDescriptor descriptor, string? channelContext)
    {
        if (descriptor.OriginMembers is not { Count: > 0 } members || string.IsNullOrWhiteSpace(channelContext))
            return null;

        return members.FirstOrDefault(member =>
            !string.IsNullOrWhiteSpace(member.Match)
            && channelContext.Contains(member.Match, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolvePluginRelativeIconForWire(AppCatalogEntry entry, string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
            return null;
        if (icon.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || icon.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || icon.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || Path.IsPathFullyQualified(icon))
        {
            return ResolveIconForWire(icon);
        }

        try
        {
            var root = Path.GetFullPath(entry.Plugin.Manifest.RootPath);
            var full = Path.GetFullPath(Path.Combine(root, icon));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return ResolveIconForWire(full);
        }
        catch
        {
            return null;
        }
    }

    private static PluginDiagnosticWire MapDiagnostic(PluginDiagnostic diagnostic) =>
        new()
        {
            Severity = diagnostic.Severity.ToString().ToLowerInvariant(),
            Code = diagnostic.Code,
            Message = diagnostic.Message,
            PluginId = diagnostic.PluginId,
            Path = diagnostic.Path
        };

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

    private static string? ResolvePluginInterfaceIconForWire(PluginManifest manifest)
    {
        var interfaceMetadata = manifest.Interface;
        return ResolveIconForWire(interfaceMetadata?.ComposerIcon)
               ?? ResolveIconForWire(interfaceMetadata?.Logo);
    }
}
