using System.Text.Json.Nodes;

namespace DotCraft.Hooks;

public sealed record HookMcpExecutionRequest(
    HookEvent Event,
    string Server,
    string Tool,
    JsonObject? Input,
    bool SkipIfDormant,
    HookInput HookInput);

public sealed record HookMcpExecutionResult(
    bool Succeeded,
    bool Skipped = false,
    string? Output = null,
    string? Error = null);

public interface IHookMcpExecutor
{
    Task<HookMcpExecutionResult> ExecuteAsync(HookMcpExecutionRequest request, CancellationToken ct);
}
