using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using DotCraft.Tools;
using DotCraft.Utilities;
using Microsoft.Extensions.AI;

namespace DotCraft.CodeMode;

internal sealed record CodeModeExecDependencies(
    CodeModeWorkerHost Host,
    CodeModeStore Store,
    IToolDispatcher Dispatcher,
    EffectiveToolSnapshot Snapshot,
    CodeModeSurface Surface,
    string WorkspacePath,
    string DataPath,
    int SpillPreviewLines);

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

        var stopwatch = Stopwatch.StartNew();
        CodeModeCellSession session;
        try
        {
            session = await dependencies.Host.StartCellAsync(
                context.ThreadId,
                StartPayload(program, context.ThreadId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (CodeModeUnavailableException ex)
        {
            return ToolExecutionResult.Failed(new ToolError(UnavailableErrorCode, ex.Message), ex.Message);
        }

        using var deadline = new PausableDeadline(program.Timeout, cancellationToken);
        try
        {
            using var run = new CodeModeCellRun(dependencies, context, session, deadline, cancellationToken);
            var outcome = await run.RunAsync().ConfigureAwait(false);
            var storeRejected = outcome is { Status: CodeModeProtocol.Completed, StoreWrites: { } writes }
                && !dependencies.Store.TryApply(
                    context.ThreadId,
                    writes["set"] as JsonObject ?? new JsonObject(),
                    (writes["deleted"] as JsonArray ?? []).Select(static key => key!.GetValue<string>()));
            return BuildResult(context, program, outcome, run.Output, run.Calls, storeRejected, stopwatch.Elapsed);
        }
        finally
        {
            session.Release();
        }
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

    private ToolExecutionResult BuildResult(
        ToolInvocationContext context,
        CodeModeProgram program,
        CodeModeCellOutcome outcome,
        IReadOnlyList<CodeModeOutputItem> output,
        IReadOnlyList<CodeModeNestedCall> calls,
        bool storeRejected,
        TimeSpan elapsed)
    {
        var builder = new StringBuilder();
        builder.Append(outcome.Status switch
        {
            CodeModeProtocol.Completed => "Script completed",
            CodeModeProtocol.TimedOut => "Script timed out",
            _ => "Script failed"
        }).Append('\n');
        builder.Append("Wall time ")
            .Append(elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture))
            .Append(" seconds\n");
        builder.Append("Output:");
        foreach (var item in output.Where(static item => item.Text is not null))
            builder.Append('\n').Append(item.Text);
        if (storeRejected)
            builder.Append("\nStore writes were not saved: the store would exceed ")
                .Append(CodeModeStore.MaxBytes / (1024 * 1024))
                .Append(" MiB.");
        if (outcome.Status == CodeModeProtocol.Failed)
        {
            builder.Append("\nScript error:\n").Append(outcome.Error ?? "The script failed.");
            if (!string.IsNullOrWhiteSpace(outcome.Stack))
                builder.Append('\n').Append(outcome.Stack.TrimEnd());
            if (calls.Count > 0)
            {
                builder.Append("\nTool calls made before the failure:");
                foreach (var call in calls)
                    builder.Append("\n- tools.").Append(call.JsName).Append(": ").Append(call.Status);
            }
        }

        var text = builder.ToString();
        var maxChars = (int)Math.Min(int.MaxValue, (long)program.MaxOutputTokens * 4);
        if (text.Length > maxChars)
        {
            text = (string)ToolResultProcessor.Process(
                CodeModeSurface.CodeModeToolName.Name,
                text,
                maxChars,
                context.WorkspacePath ?? dependencies.WorkspacePath,
                dependencies.DataPath,
                context.ThreadId,
                dependencies.SpillPreviewLines,
                context.CallId)!;
        }

        var images = output
            .Where(static item => item.Data is not null)
            .Select(static item => (AIContent)new DataContent(Convert.FromBase64String(item.Data!), item.MimeType!))
            .ToList();
        return ToolExecutionResult.Succeeded(
            text,
            contentItems: images.Count == 0 ? null : [new TextContent(text), .. images]);
    }
}
