using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

// ───── hooks/* (Lifecycle hook management) ─────

public sealed class HooksListParams
{
}

public sealed class HooksListResult
{
    public List<HookMetadataWire> Hooks { get; set; } = [];

    public List<string> Warnings { get; set; } = [];

    public List<HookErrorInfoWire> Errors { get; set; } = [];
}

public sealed class HooksSetStateParams
{
    public string Key { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Enabled { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TrustedHash { get; set; }
}

public sealed class HooksSetStateResult
{
    public List<HookMetadataWire> Hooks { get; set; } = [];

    public List<string> Warnings { get; set; } = [];

    public List<HookErrorInfoWire> Errors { get; set; } = [];
}

public sealed class HookMetadataWire
{
    public string Key { get; set; } = string.Empty;

    public string EventName { get; set; } = string.Empty;

    public string HandlerType { get; set; } = "command";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Matcher { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Command { get; set; }

    public int TimeoutSec { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StatusMessage { get; set; }

    public string SourcePath { get; set; } = string.Empty;

    public string Source { get; set; } = "unknown";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PluginId { get; set; }

    public int DisplayOrder { get; set; }

    public bool Enabled { get; set; }

    public bool IsManaged { get; set; }

    public string CurrentHash { get; set; } = string.Empty;

    public string TrustStatus { get; set; } = "untrusted";
}

public sealed class HookErrorInfoWire
{
    public string Path { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}
