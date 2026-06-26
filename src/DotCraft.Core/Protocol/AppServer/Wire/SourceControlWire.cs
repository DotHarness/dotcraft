using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

// ───── sourceControl/get, sourceControl/update (spec Section 25A) ─────

/// <summary>Params for <see cref="AppServerMethods.SourceControlGet"/>. Reserved for future filters.</summary>
public sealed class SourceControlGetParams
{
}

/// <summary>
/// Non-sensitive Perforce connection fields shared by the snapshot, update, and test wire DTOs.
/// Never carries a password or ticket.
/// </summary>
public sealed class PerforceConnectionWire
{
    public string Port { get; set; } = string.Empty;

    public string Client { get; set; } = string.Empty;

    public string User { get; set; } = string.Empty;

    public string Charset { get; set; } = string.Empty;

    public string P4ConfigName { get; set; } = string.Empty;

    public string P4ExecutablePath { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 30;

    public bool Online { get; set; } = true;

    public bool AutoOffline { get; set; } = true;
}

/// <summary>Client UI capability gates carried in a source control snapshot.</summary>
public sealed class SourceControlCapabilitiesWire
{
    public bool GitCommit { get; set; }

    public bool PerforceBinding { get; set; }

    public bool PerforceChangelist { get; set; }

    public bool PerforceShelve { get; set; }

    public bool PerforceSubmit { get; set; }
}

/// <summary>
/// Effective source control binding snapshot returned by
/// <see cref="AppServerMethods.SourceControlGet"/> and <see cref="AppServerMethods.SourceControlUpdate"/>.
/// </summary>
public sealed class SourceControlSnapshot
{
    public string Provider { get; set; } = "auto";

    public string EffectiveProvider { get; set; } = "none";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConnectionMode { get; set; }

    public string Status { get; set; } = "notConfigured";

    public string WorkspacePath { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PerforceConnectionWire? Perforce { get; set; }

    public SourceControlCapabilitiesWire Capabilities { get; set; } = new();
}

/// <summary>Params for <see cref="AppServerMethods.SourceControlUpdate"/>.</summary>
public sealed class SourceControlUpdateParams
{
    public string Provider { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConnectionMode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PerforceConnectionWire? Perforce { get; set; }
}

// ───── sourceControl/test (spec Section 25A.4) ─────

/// <summary>Params for <see cref="AppServerMethods.SourceControlTest"/>.</summary>
public sealed class SourceControlTestParams
{
    public string Provider { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConnectionMode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PerforceConnectionWire? Perforce { get; set; }

    /// <summary>
    /// Transient password for a one-shot login attempt when a ticket is missing.
    /// Never persisted, never returned, never logged.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Password { get; set; }
}

/// <summary>A stable diagnostic code plus its English fallback text.</summary>
public sealed class SourceControlDiagnosticItem
{
    public string Code { get; set; } = string.Empty;

    public string FallbackText { get; set; } = string.Empty;
}

/// <summary>Connection identity detail for a test result.</summary>
public sealed class SourceControlTestIdentity
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ServerAddress { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? User { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Client { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Charset { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConnectionMode { get; set; }
}

/// <summary>Workspace mapping detail for a test result.</summary>
public sealed class SourceControlTestWorkspace
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkspacePath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientRoot { get; set; }

    public List<string> AltRoots { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? MappingOk { get; set; }
}

/// <summary>Authentication / ticket detail for a test result.</summary>
public sealed class SourceControlTestAuthentication
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TicketStatus { get; set; }

    public bool LoginRequired { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpiresMessage { get; set; }
}

/// <summary>Low-level diagnostics for a test result.</summary>
public sealed class SourceControlTestDiagnostics
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? P4Version { get; set; }

    public int TimeoutSeconds { get; set; }

    public int WarningCount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; set; }
}

/// <summary>Structured result for <see cref="AppServerMethods.SourceControlTest"/>. Never persisted.</summary>
public sealed class SourceControlTestResult
{
    public string Status { get; set; } = "error";

    public string Code { get; set; } = "Unknown";

    public string Summary { get; set; } = string.Empty;

    public string FallbackText { get; set; } = string.Empty;

    public SourceControlTestIdentity Identity { get; set; } = new();

    public SourceControlTestWorkspace Workspace { get; set; } = new();

    public SourceControlTestAuthentication Authentication { get; set; } = new();

    public SourceControlTestDiagnostics Diagnostics { get; set; } = new();

    public List<SourceControlDiagnosticItem> Warnings { get; set; } = [];

    public List<SourceControlDiagnosticItem> Errors { get; set; } = [];
}
