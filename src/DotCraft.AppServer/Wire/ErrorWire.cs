using System.Text.Json.Serialization;

namespace DotCraft.AppServer;

internal sealed record MethodErrorParams([property: JsonPropertyName("method")] string Method);

internal sealed record ThreadErrorParams([property: JsonPropertyName("threadId")] string ThreadId);

internal sealed record TurnErrorParams([property: JsonPropertyName("turnId")] string TurnId);

internal sealed record WorktreeConflictErrorParams(
    [property: JsonPropertyName("conflictPaths")] IReadOnlyList<string> ConflictPaths);

internal sealed record ChannelErrorParams([property: JsonPropertyName("channelName")] string ChannelName);


internal sealed record NamedResourceErrorParams([property: JsonPropertyName("name")] string Name);

internal sealed record MarketplaceErrorParams(
    [property: JsonPropertyName("marketplaceName")] string MarketplaceName);

internal sealed record CommandErrorParams([property: JsonPropertyName("command")] string Command);

internal sealed record WorktreeCleanupErrorParams(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("exitCode")] int? ExitCode,
    [property: JsonPropertyName("outputTail")] string OutputTail);

internal sealed record FileSystemErrorParams([property: JsonPropertyName("path")] string Path);

internal sealed record AppBindingVersionErrorParams(
    [property: JsonPropertyName("requiredVersion")] int RequiredVersion);

internal sealed record AppSurfaceErrorParams(
    [property: JsonPropertyName("appId")] string AppId,
    [property: JsonPropertyName("surfaceId")] string SurfaceId);

internal sealed record AgentProfileDiagnosticsErrorParams(
    [property: JsonPropertyName("diagnostics")] object Diagnostics);

internal sealed record RemoteToolBusyErrorParams([property: JsonPropertyName("owner")] string Owner);
