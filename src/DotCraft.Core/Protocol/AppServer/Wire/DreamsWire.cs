using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Cron;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol.AppServer;


// ───── dreams/* (workspace Dreams management) ─────

/// <summary>
/// Params for <see cref="AppServerMethods.DreamsStatus"/>.
/// </summary>
public sealed class DreamsStatusParams
{
}

/// <summary>
/// Params for <see cref="AppServerMethods.DreamsRun"/>.
/// </summary>
public sealed class DreamsRunParams
{
}

/// <summary>
/// Params for <see cref="AppServerMethods.DreamsCreate"/>.
/// </summary>
public sealed class DreamsCreateParams
{
    public List<string>? ThreadIds { get; set; }

    public int? ThreadLookbackCount { get; set; }

    public string? Instructions { get; set; }

    public string? Model { get; set; }
}

public sealed class DreamsRunIdParams
{
    public string RunId { get; set; } = string.Empty;
}

public sealed class DreamsListParams
{
    public bool IncludeArchived { get; set; }
}

/// <summary>
/// Result for <see cref="AppServerMethods.DreamsStatus"/> and <see cref="AppServerMethods.DreamsRun"/>.
/// </summary>
public sealed class DreamsStatusResult
{
    public bool Enabled { get; set; }

    public string Interval { get; set; } = "24:00:00";

    public int ThreadLookbackCount { get; set; }

    public bool AutoApply { get; set; }

    public int HistoryTailChars { get; set; }

    public int MinCompletedTurnsSinceLastRun { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? NextRunAt { get; set; }

    public bool Running { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActiveDreamStoreId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public DreamsRunStateWire? LastRun { get; set; }
}

public sealed class DreamsRunResult
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public DreamsRunStateWire? Run { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActiveDreamStoreId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DreamsRunPreviewWire? Preview { get; set; }
}

public sealed class DreamsListResult
{
    public List<DreamsRunStateWire> Runs { get; set; } = [];
}

/// <summary>
/// Wire projection of the latest Dreams run state.
/// </summary>
public sealed class DreamsRunStateWire
{
    public string Id { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? EndedAt { get; set; }

    public int ProcessedThreadCount { get; set; }

    public int CandidateThreadCount { get; set; }

    public bool DreamWritten { get; set; }

    public bool HistoryWritten { get; set; }

    public int TopicFilesWritten { get; set; }

    public int TopicFilesDeleted { get; set; }

    public int EvidenceSearchCount { get; set; }

    public int EvidenceReadCount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputStoreId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReviewStatus { get; set; }

    public bool AutoApplied { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorType { get; set; }

    public List<string> EvidenceThreadIds { get; set; } = [];

    public List<string> WrittenPaths { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThreadId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TurnId { get; set; }

    public List<string> TurnIds { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Trigger { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TokenUsageInfo? Usage { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InputManifestPath { get; set; }
}

public sealed class DreamsRunPreviewWire
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActiveStoreId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputStoreId { get; set; }

    public string ActiveIndexMarkdown { get; set; } = string.Empty;

    public string OutputIndexMarkdown { get; set; } = string.Empty;

    public List<string> ActiveTopicPaths { get; set; } = [];

    public List<string> OutputTopicPaths { get; set; } = [];
}
