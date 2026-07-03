namespace DotCraft.Hooks;

/// <summary>
/// Stable source labels for discovered hooks.
/// </summary>
public static class HookSources
{
    public const string User = "user";
    public const string Workspace = "workspace";
    public const string Plugin = "plugin";
    public const string Unknown = "unknown";
}

/// <summary>
/// Trust states exposed to clients and used by runtime execution filtering.
/// </summary>
public static class HookTrustStatuses
{
    public const string Managed = "managed";
    public const string Trusted = "trusted";
    public const string Untrusted = "untrusted";
    public const string Modified = "modified";
}

/// <summary>
/// Client-visible metadata for one discovered hook handler.
/// </summary>
public sealed class HookMetadata
{
    public string Key { get; set; } = string.Empty;

    public string EventName { get; set; } = string.Empty;

    public string HandlerType { get; set; } = "command";

    public string? Matcher { get; set; }

    public string? Command { get; set; }

    public int TimeoutSec { get; set; }

    public string? StatusMessage { get; set; }

    public string SourcePath { get; set; } = string.Empty;

    public string Source { get; set; } = HookSources.Unknown;

    public string? PluginId { get; set; }

    public int DisplayOrder { get; set; }

    public bool Enabled { get; set; }

    public bool IsManaged { get; set; }

    public string CurrentHash { get; set; } = string.Empty;

    public string TrustStatus { get; set; } = HookTrustStatuses.Untrusted;
}

/// <summary>
/// Warnings or errors collected while discovering hooks for a source file.
/// </summary>
public sealed class HookErrorInfo
{
    public string Path { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Full hook discovery output: effective runtime config plus metadata for clients.
/// </summary>
public sealed class HookDiscoveryResult
{
    public HooksFileConfig RuntimeConfig { get; init; } = new();

    public List<HookMetadata> Hooks { get; init; } = [];

    public List<string> Warnings { get; init; } = [];

    public List<HookErrorInfo> Errors { get; init; } = [];
}
