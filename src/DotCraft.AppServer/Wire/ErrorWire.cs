using System.Text.Json.Serialization;

namespace DotCraft.AppServer;

/// <summary>Error parameters identifying an RPC method.</summary>
internal sealed record MethodErrorParams([property: JsonPropertyName("method")] string Method);

/// <summary>Error parameters identifying a thread.</summary>
internal sealed record ThreadErrorParams([property: JsonPropertyName("threadId")] string ThreadId);

/// <summary>Error parameters identifying a turn.</summary>
internal sealed record TurnErrorParams([property: JsonPropertyName("turnId")] string TurnId);

/// <summary>Error parameters carrying worktree conflict paths.</summary>
internal sealed record WorktreeConflictErrorParams(
    [property: JsonPropertyName("conflictPaths")] IReadOnlyList<string> ConflictPaths);

/// <summary>Error parameters identifying an external channel.</summary>
internal sealed record ChannelErrorParams([property: JsonPropertyName("channelName")] string ChannelName);

/// <summary>Error parameters identifying a cron job.</summary>
internal sealed record CronJobErrorParams([property: JsonPropertyName("jobId")] string JobId);

/// <summary>Error parameters identifying a named protocol resource.</summary>
internal sealed record NamedResourceErrorParams([property: JsonPropertyName("name")] string Name);

/// <summary>Error parameters identifying a marketplace.</summary>
internal sealed record MarketplaceErrorParams(
    [property: JsonPropertyName("marketplaceName")] string MarketplaceName);

/// <summary>Error parameters identifying a command.</summary>
internal sealed record CommandErrorParams([property: JsonPropertyName("command")] string Command);

/// <summary>Error parameters identifying a required App Binding version.</summary>
internal sealed record AppBindingVersionErrorParams(
    [property: JsonPropertyName("requiredVersion")] int RequiredVersion);

/// <summary>Error parameters identifying an app surface.</summary>
internal sealed record AppSurfaceErrorParams(
    [property: JsonPropertyName("appId")] string AppId,
    [property: JsonPropertyName("surfaceId")] string SurfaceId);

/// <summary>Error parameters carrying agent-profile diagnostics.</summary>
internal sealed record AgentProfileDiagnosticsErrorParams(
    [property: JsonPropertyName("diagnostics")] object Diagnostics);

/// <summary>Error parameters identifying an automation task.</summary>
internal sealed record TaskErrorParams([property: JsonPropertyName("taskId")] string TaskId);
