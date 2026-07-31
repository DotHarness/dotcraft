using System.Text.Json.Serialization;
using DotCraft.Tools.BackgroundTerminals;

namespace DotCraft.Protocol.AppServer;


// ───── terminal/* ─────

public sealed class TerminalListParams
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThreadId { get; set; }
}

public sealed class TerminalReadParams
{
    public string SessionId { get; set; } = string.Empty;

    public int? WaitMs { get; set; }

    public int? MaxOutputChars { get; set; }
}

public sealed class TerminalWriteParams
{
    public string SessionId { get; set; } = string.Empty;

    public string Input { get; set; } = string.Empty;

    public int? YieldTimeMs { get; set; }

    public int? MaxOutputChars { get; set; }
}

public sealed class TerminalStopParams
{
    public string SessionId { get; set; } = string.Empty;
}

public sealed class TerminalCleanParams
{
    public string ThreadId { get; set; } = string.Empty;
}

/// <summary>Result for <c>terminal/list</c>.</summary>
public sealed class TerminalListResult
{
    [JsonPropertyName("terminals")]
    public IReadOnlyList<BackgroundTerminalSnapshot> Terminals { get; set; } = [];
}

/// <summary>Result for <c>terminal/read</c>.</summary>
public sealed class TerminalReadResult
{
    [JsonPropertyName("terminal")]
    public BackgroundTerminalSnapshot Terminal { get; set; } = null!;
}

/// <summary>Result for <c>terminal/write</c>.</summary>
public sealed class TerminalWriteResult
{
    [JsonPropertyName("terminal")]
    public BackgroundTerminalSnapshot Terminal { get; set; } = null!;
}

/// <summary>Result for <c>terminal/stop</c>.</summary>
public sealed class TerminalStopResult
{
    [JsonPropertyName("terminal")]
    public BackgroundTerminalSnapshot Terminal { get; set; } = null!;
}

/// <summary>Result for <c>terminal/clean</c>.</summary>
public sealed class TerminalCleanResult
{
    [JsonPropertyName("terminals")]
    public IReadOnlyList<BackgroundTerminalSnapshot> Terminals { get; set; } = [];
}
