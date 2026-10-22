using System.Text.Json.Nodes;
using DotCraft.Tools;

namespace DotCraft.CodeMode;

internal sealed record CodeModeExecDependencies(
    CodeModeWorkerHost Host,
    CodeModeStore Store,
    IToolDispatcher Dispatcher,
    EffectiveToolSnapshot Snapshot,
    CodeModeSurface Surface,
    string WorkspacePath,
    string DataPath,
    int SpillPreviewLines,
    ToolLocation? Location);

internal sealed class CodeModeExecRuntime(CodeModeExecDependencies dependencies) : IToolRuntime
{
    public const string UnavailableErrorCode = "code_mode_unavailable";

    public async ValueTask<ToolExecutionResult> InvokeAsync(
        ToolInvocationContext context,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        var limits = dependencies.Host.Limits;
        var code = arguments["code"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        if (!CodeModePragma.TryParse(code, limits, out var program, out var error))
            return ToolExecutionResult.Failed(new ToolError(ToolErrorCodes.InputInvalid, error), error);

        CodeModeCellSession session;
        try
        {
            session = await dependencies.Host.StartCellAsync(
                StartPayload(program, context.ThreadId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (CodeModeUnavailableException ex)
        {
            return ToolExecutionResult.Failed(new ToolError(UnavailableErrorCode, ex.Message), ex.Message);
        }

        var cell = CodeModeCell.Start(dependencies, context, session, program);
        dependencies.Host.OpenCells.Add(cell);
        return await CodeModeCellResponse.AwaitAsync(
            dependencies,
            context,
            cell,
            program.YieldTime,
            program.MaxOutputTokens,
            cancellationToken).ConfigureAwait(false);
    }

    private JsonObject StartPayload(CodeModeProgram program, string threadId)
    {
        var limits = dependencies.Host.Limits;
        var tools = new JsonArray();
        foreach (var tool in dependencies.Surface.Tools)
        {
            tools.Add(new JsonObject
            {
                ["name"] = tool.JsName,
                ["description"] = tool.Declaration
            });
        }
        var aliases = new JsonObject();
        foreach (var (alias, target) in dependencies.Surface.Aliases)
            aliases[alias] = target;
        return new JsonObject
        {
            ["source"] = program.Source,
            ["tools"] = tools,
            ["aliases"] = aliases,
            ["store"] = dependencies.Store.Snapshot(threadId),
            ["limits"] = new JsonObject
            {
                ["maxMemoryBytes"] = limits.MaxCellMemoryBytes,
                ["maxStatements"] = limits.MaxStatements,
                ["maxRecursionDepth"] = limits.MaxRecursionDepth,
                ["engineTimeoutMs"] = (long)limits.EngineTimeout.TotalMilliseconds,
                ["regexTimeoutMs"] = (long)program.Timeout.TotalMilliseconds,
                ["maxOutputBytes"] = limits.MaxCellOutputBytes
            }
        };
    }
}

internal sealed class CodeModeWaitRuntime(CodeModeExecDependencies dependencies) : IToolRuntime
{
    public async ValueTask<ToolExecutionResult> InvokeAsync(
        ToolInvocationContext context,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        var limits = dependencies.Host.Limits;
        if (arguments["cell_id"] is not JsonValue idValue || !idValue.TryGetValue<string>(out var cellId) || string.IsNullOrWhiteSpace(cellId))
            return Invalid("cell_id is required.");
        var maxYieldMs = (long)limits.MaxTimeout.TotalMilliseconds;
        if (!CodeModePragma.TryReadOptionalInteger(arguments["yield_time_ms"], 0, maxYieldMs, (long)limits.DefaultYieldTime.TotalMilliseconds, out var yieldMs))
            return Invalid($"yield_time_ms must be an integer from 0 to {maxYieldMs}.");
        if (!CodeModePragma.TryReadOptionalInteger(arguments["max_tokens"], 1, limits.MaxOutputTokens, limits.DefaultMaxOutputTokens, out var maxTokens))
            return Invalid($"max_tokens must be an integer from 1 to {limits.MaxOutputTokens}.");
        var terminate = arguments["terminate"] is JsonValue flag && flag.TryGetValue<bool>(out var requested) && requested;

        if (dependencies.Host.OpenCells.Find(context.ThreadId, cellId) is not { } cell)
            return CodeModeCellResponse.NotFound(cellId);
        return terminate
            ? await CodeModeCellResponse.TerminateAsync(dependencies, context, cell, (int)maxTokens).ConfigureAwait(false)
            : await CodeModeCellResponse.AwaitAsync(
                dependencies,
                context,
                cell,
                TimeSpan.FromMilliseconds(yieldMs),
                (int)maxTokens,
                cancellationToken).ConfigureAwait(false);
    }

    private static ToolExecutionResult Invalid(string message) =>
        ToolExecutionResult.Failed(new ToolError(ToolErrorCodes.InputInvalid, message), message);
}
