using System.Text.Json;

namespace DotCraft.Sdk.AppServer;

/// <summary>DotCraft AppServer initialize options.</summary>
public class DotCraftClientOptions
{
    /// <summary>Overrides the connection entry point's reconnect default.</summary>
    public bool? AutoReconnect { get; set; }

    /// <summary>Machine-readable client name.</summary>
    public string ClientName { get; set; } = "dotcraft-dotnet";

    /// <summary>Human-readable client title.</summary>
    public string? ClientTitle { get; set; }

    /// <summary>Client version string.</summary>
    public string ClientVersion { get; set; } = "0.1.0";

    /// <summary>Whether the client can answer approval requests.</summary>
    public bool ApprovalSupport { get; set; }

    /// <summary>Whether the client wants streaming delta notifications.</summary>
    public bool StreamingSupport { get; set; } = true;

    /// <summary>Whether the client can answer model request-user-input prompts.</summary>
    public bool RequestUserInputSupport { get; set; }

    /// <summary>Whether the client wants workspace config change notifications.</summary>
    public bool ConfigChange { get; set; } = true;

    /// <summary>Additional client capability fields merged into the initialize request.</summary>
    public IReadOnlyDictionary<string, object?>? ExtraCapabilities { get; set; }

    /// <summary>Optional handler for server-initiated approval requests.</summary>
    public ApprovalHandler? ApprovalHandler { get; set; }

    /// <summary>Optional handler for server-initiated user-input requests.</summary>
    public UserInputHandler? UserInputHandler { get; set; }
}

/// <summary>Options for Hub-backed local AppServer connections.</summary>
public sealed class DotCraftLocalClientOptions : DotCraftClientOptions
{
    /// <summary>Optional dotcraft executable or dll path used when starting Hub.</summary>
    public string? Executable { get; set; }

    /// <summary>Optional override for the Hub lock file path.</summary>
    public string? HubLockPath { get; set; }

    /// <summary>Optional user profile directory used to resolve Hub and default Chat workspace paths.</summary>
    public string? UserProfilePath { get; set; }

    /// <summary>Hub startup timeout.</summary>
    public TimeSpan HubStartupTimeout { get; set; } = TimeSpan.FromSeconds(15);
}

/// <summary>Raw AppServer notification retained as the extension and diagnostics escape hatch.</summary>
public sealed record AppServerNotification(string Method, JsonElement Params);
