using System.Text.Json.Serialization;
using DotCraft.Plugins;
using PluginDiagnostic = DotCraft.Plugins.PluginDiagnostic;

namespace DotCraft.AppBinding;

/// <summary>
/// Top-level plugin app contribution document.
/// </summary>
public sealed class AppDescriptorDocument
{
    public List<AppDescriptor> Apps { get; set; } = [];
}

/// <summary>
/// Plugin-declared app descriptor used for App Binding discovery and validation.
/// </summary>
public sealed class AppDescriptor
{
    public string AppId { get; set; } = string.Empty;

    [JsonIgnore]
    public string ToolNamespace { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string DeveloperName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; set; }

    /// <summary>
    /// Optional <see cref="DotCraft.Sdk"/> <c>SessionIdentity.ChannelName</c> this app stamps on
    /// threads it originates. When a thread's <c>OriginChannel</c> matches, the host attributes the
    /// thread to this app and renders the app icon + display name as the thread origin badge. Opt-in;
    /// there is no implicit <see cref="ToolNamespace"/> matching.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OriginChannel { get; set; }

    /// <summary>
    /// Optional finer-grained per-member branding for an app that originates threads for distinct
    /// members/roles. Requires <see cref="OriginChannel"/>. When a thread matches and its
    /// <c>channelContext</c> matches a member, the host renders that member's icon + display name.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AppOriginMemberDescriptor>? OriginMembers { get; set; }

    public AppConnectionDescriptor Connection { get; set; } = new();

    public AppNativeApplicationDescriptor NativeApplication { get; set; } = new();

    [JsonIgnore]
    public List<AppScopeDescriptor> Scopes { get; set; } = [];

    [JsonIgnore]
    public List<AppToolCatalogEntry> ToolCatalog { get; set; } = [];

    [JsonIgnore]
    public AppDynamicToolCatalogDescriptor DynamicToolCatalog { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PrivacyUrl { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TermsUrl { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReleasePage { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DownloadUrl { get; set; }
}

/// <summary>
/// A per-member origin-branding entry: when a thread's <c>channelContext</c> contains
/// <see cref="Match"/> (case-insensitive substring), the host renders this member's icon + name.
/// </summary>
public sealed class AppOriginMemberDescriptor
{
    public string Match { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; set; }
}

public sealed class AppConnectionDescriptor
{
    public List<AppHandoffModeDescriptor> HandoffModes { get; set; } = [];
}

public sealed class AppHandoffModeDescriptor
{
    public string Mode { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UriTemplate { get; set; }
}

public sealed class AppNativeApplicationDescriptor
{
    public string DisplayName { get; set; } = string.Empty;

    public string Protocol { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InstallUrl { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, AppNativeApplicationPlatformDescriptor>? Platforms { get; set; }
}

public sealed class AppNativeApplicationPlatformDescriptor
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Protocol { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AppUserModelId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BundleId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DesktopId { get; set; }
}

public sealed class AppScopeDescriptor
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Risk { get; set; } = AppBindingRisks.Read;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DefaultSelected { get; set; }
}

public sealed class AppToolCatalogEntry
{
    public string Name { get; set; } = string.Empty;

    public string Scope { get; set; } = string.Empty;

    public string Risk { get; set; } = AppBindingRisks.Read;

    public string DefaultExposure { get; set; } = AppBindingExposures.Direct;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
}

public sealed class AppDynamicToolCatalogDescriptor
{
    public bool Enabled { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
}

public static class AppBindingRisks
{
    public const string Read = "read";
    public const string Mutate = "mutate";
    public const string ExternalWrite = "externalWrite";

    public static bool IsKnown(string value) =>
        string.Equals(value, Read, StringComparison.Ordinal)
        || string.Equals(value, Mutate, StringComparison.Ordinal)
        || string.Equals(value, ExternalWrite, StringComparison.Ordinal);

    public static int Rank(string value) =>
        value switch
        {
            Read => 0,
            Mutate => 1,
            ExternalWrite => 2,
            _ => int.MaxValue
        };
}

public static class AppBindingExposures
{
    public const string Direct = "direct";
    public const string Deferred = "deferred";

    public static bool IsKnown(string value) =>
        string.Equals(value, Direct, StringComparison.Ordinal)
        || string.Equals(value, Deferred, StringComparison.Ordinal);
}

internal static class LegacyAppBindingStates
{
    public const string Pending = "pending";
    public const string Active = "active";
    public const string Offline = "offline";
    public const string Expired = "expired";
    public const string Revoked = "revoked";
    public const string Error = "error";
    public const string Cancelled = "cancelled";
}

public static class AppBindingKinds
{
    public const string App = "app";
    public const string SocialChannel = "socialChannel";
    public const string ManagedApp = "managedApp";

    public static bool IsKnown(string value) =>
        string.Equals(value, App, StringComparison.Ordinal)
        || string.Equals(value, SocialChannel, StringComparison.Ordinal)
        || string.Equals(value, ManagedApp, StringComparison.Ordinal);
}

public static class AppContextBlockKinds
{
    public const string Role = "role";
    public const string Mission = "mission";
    public const string TeamState = "teamState";
    public const string MailboxDigest = "mailboxDigest";
    public const string ArtifactIndex = "artifactIndex";
    public const string Policy = "policy";

    /// <summary>
    /// Model‑visible state pushed by an Interactive Tool UI via <c>ui/update-model-context</c>
    /// (M‑iii). Keyed to the originating <c>dynamicToolCall</c> item; last‑write‑wins.
    /// </summary>
    public const string UiModelContext = "uiModelContext";

    public static bool IsKnown(string value) =>
        string.Equals(value, Role, StringComparison.Ordinal)
        || string.Equals(value, Mission, StringComparison.Ordinal)
        || string.Equals(value, TeamState, StringComparison.Ordinal)
        || string.Equals(value, MailboxDigest, StringComparison.Ordinal)
        || string.Equals(value, ArtifactIndex, StringComparison.Ordinal)
        || string.Equals(value, Policy, StringComparison.Ordinal)
        || string.Equals(value, UiModelContext, StringComparison.Ordinal);
}

public static class AppContextBlockVisibilities
{
    public const string Model = "model";
    public const string HiddenFromModel = "hiddenFromModel";

    public static bool IsKnown(string value) =>
        string.Equals(value, Model, StringComparison.Ordinal)
        || string.Equals(value, HiddenFromModel, StringComparison.Ordinal);
}

public static class AppThreadInputStartPolicies
{
    public const string QueueOnly = "queueOnly";
    public const string RunWhenIdle = "runWhenIdle";

    public static bool IsKnown(string value) =>
        string.Equals(value, QueueOnly, StringComparison.Ordinal)
        || string.Equals(value, RunWhenIdle, StringComparison.Ordinal);
}

public static class AppConnectionStates
{
    public const string NotConnected = "notConnected";
    public const string Connecting = "connecting";
    public const string Connected = "connected";
    public const string NeedsAuth = "needsAuth";
    public const string Error = "error";
}

public static class AppNativeApplicationStates
{
    public const string Installed = "installed";
    public const string Missing = "missing";
    public const string Unknown = "unknown";
}

public static class AppBindingErrorCodes
{
    public const string Offline = "AppBindingOffline";
    public const string Expired = "AppBindingExpired";
    public const string Revoked = "AppBindingRevoked";
    public const string ScopeDenied = "AppBindingScopeDenied";
    public const string ToolUnavailable = "AppBindingToolUnavailable";
    public const string ProtocolViolation = "AppBindingProtocolViolation";

    /// <summary>
    /// A UI‑initiated tool call targeted a tool that requires approval, but the calling client
    /// cannot prompt for it (no approval gate, e.g. non‑Desktop).
    /// </summary>
    public const string ApprovalRequired = "AppBindingApprovalRequired";
/// <summary>The user declined a UI‑initiated mutating tool call at the approval prompt (M‑v).</summary>
    public const string ApprovalDeclined = "AppBindingApprovalDeclined";
}

public sealed record ManagedAppBindingCatalogMetadata(
    string OwningPluginId,
    IReadOnlySet<string> Surfaces,
    bool RequiresExternalConnection);

public sealed record AppCatalogEntry(
    AppDescriptor Descriptor,
    DiscoveredPlugin Plugin,
    IReadOnlyList<PluginDiagnostic> Diagnostics)
{
    public ManagedAppBindingCatalogMetadata? ManagedRuntime { get; init; }
}

public sealed record AppCatalogSnapshot(
    IReadOnlyList<AppCatalogEntry> Entries,
    IReadOnlyList<PluginDiagnostic> Diagnostics)
{
    public IReadOnlyList<DiscoveredPlugin> Plugins { get; init; } = [];
}
