using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Cron;
using Microsoft.Extensions.AI;

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
