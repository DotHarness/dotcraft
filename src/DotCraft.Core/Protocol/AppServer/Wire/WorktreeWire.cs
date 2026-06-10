using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Cron;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol.AppServer;


// ───── worktree/createAndFork ─────

public sealed class WorktreeCreateAndForkParams
{
    public string SourceThreadId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadForkPoint? ForkPoint { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionIdentity? Identity { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadConfiguration? Config { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DynamicToolSpec>? DynamicTools { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, RuntimeAdditionalContextEntry>? AdditionalContext { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BranchName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BaseRef { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; set; }

    public bool? CopyDirtyChanges { get; set; }

    public bool? ExcludeTurns { get; set; }
}

public sealed class WorktreeCreateAndForkResponse
{
    public SessionWireThread Thread { get; set; } = new();

    public ThreadWorktreeInfo Worktree { get; set; } = new();
}

public sealed class WorktreeCreateAndStartParams
{
    public SessionIdentity Identity { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadConfiguration? Config { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DynamicToolSpec>? DynamicTools { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, RuntimeAdditionalContextEntry>? AdditionalContext { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HistoryMode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BranchName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BaseRef { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; set; }

    public bool? CopyDirtyChanges { get; set; }
}

public sealed class WorktreeCreateAndStartResponse
{
    public SessionWireThread Thread { get; set; } = new();

    public ThreadWorktreeInfo Worktree { get; set; } = new();
}

public sealed class ThreadWorktreeHandoffParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string Mode { get; set; } = WorktreeHandoffModes.Worktree;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BranchName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BaseRef { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; set; }

    public bool? CopyDirtyChanges { get; set; }
}

public sealed class ThreadWorktreeHandoffResponse
{
    public SessionWireThread Thread { get; set; } = new();

    public string Mode { get; set; } = WorktreeHandoffModes.Local;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public ThreadWorktreeInfo? Worktree { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadWorktreeDirtyHandoffInfo? DirtyHandoff { get; set; }
}

public sealed class WorktreeListParams
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionIdentity? Identity { get; set; }
}

public sealed class WorktreeListResult
{
    public List<ThreadWorktreeStatus> Data { get; set; } = [];
}

public sealed class WorktreeStatusParams
{
    public string ThreadId { get; set; } = string.Empty;
}

public sealed class WorktreeStatusResult
{
    public ThreadWorktreeStatus Status { get; set; } = new();
}
