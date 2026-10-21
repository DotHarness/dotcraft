using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Tools;
using DotCraft.Utilities;
using Microsoft.Extensions.AI;

namespace DotCraft.CodeMode;

/// <summary>Waits on a cell for a <c>CodeMode</c> or <c>CodeModeWait</c> call and turns what it finds into the call's result.</summary>
internal static class CodeModeCellResponse
{
    public static async Task<ToolExecutionResult> AwaitAsync(
        CodeModeExecDependencies dependencies,
        ToolInvocationContext context,
        CodeModeCell cell,
        TimeSpan yieldTime,
        int maxOutputTokens,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        if (!await EndsWithinAsync(cell, yieldTime, cancellationToken).ConfigureAwait(false))
            return Build(dependencies, context, cell, null, maxOutputTokens, stopwatch.Elapsed);
        return await FinishAsync(dependencies, context, cell, maxOutputTokens, stopwatch).ConfigureAwait(false);
    }

    public static async Task<ToolExecutionResult> TerminateAsync(
        CodeModeExecDependencies dependencies,
        ToolInvocationContext context,
        CodeModeCell cell,
        int maxOutputTokens)
    {
        var stopwatch = Stopwatch.StartNew();
        cell.Terminate();
        return await FinishAsync(dependencies, context, cell, maxOutputTokens, stopwatch).ConfigureAwait(false);
    }

    public static ToolExecutionResult NotFound(string cellId)
    {
        var message = $"No open CodeMode cell has the id '{cellId}'.";
        return ToolExecutionResult.Failed(new ToolError(ToolErrorCodes.NotFound, message), message);
    }

    private static async Task<ToolExecutionResult> FinishAsync(
        CodeModeExecDependencies dependencies,
        ToolInvocationContext context,
        CodeModeCell cell,
        int maxOutputTokens,
        Stopwatch stopwatch)
    {
        var outcome = await cell.Completion.ConfigureAwait(false);
        if (!dependencies.Host.OpenCells.Close(cell))
            return NotFound(cell.Id);
        return Build(dependencies, context, cell, outcome, maxOutputTokens, stopwatch.Elapsed);
    }

    /// <summary>Returns true when the cell ended, false when the call should yield: the yield time passed with no approval pending, or a steer arrived.</summary>
    private static async Task<bool> EndsWithinAsync(CodeModeCell cell, TimeSpan yieldTime, CancellationToken cancellationToken)
    {
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var steer = SteerAsync(waiting.Token);
            var first = await Task.WhenAny(cell.Completion, Task.Delay(yieldTime, waiting.Token), steer).ConfigureAwait(false);
            if (first != steer && !cell.Completion.IsCompleted)
                await Task.WhenAny(cell.Completion, cell.Approvals.WhenClearAsync(), steer).ConfigureAwait(false);
        }
        finally
        {
            await waiting.CancelAsync().ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return cell.Completion.IsCompleted;
    }

    private static async Task SteerAsync(CancellationToken cancellationToken)
    {
        if (StreamingGuidanceRuntimeScope.Current?.WaitForInstantInterruptAsync is { } waitAsync)
        {
            try
            {
                await waitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
    }

    private static ToolExecutionResult Build(
        CodeModeExecDependencies dependencies,
        ToolInvocationContext context,
        CodeModeCell cell,
        CodeModeCellOutcome? outcome,
        int maxOutputTokens,
        TimeSpan elapsed)
    {
        var output = cell.TakeOutput();
        var calls = cell.Calls;
        var status = outcome?.Status ?? CodeModeProtocol.Running;
        var builder = new StringBuilder();
        builder.Append(status switch
        {
            CodeModeProtocol.Running => $"Script running with cell ID {cell.Id}",
            CodeModeProtocol.Completed => "Script completed",
            CodeModeProtocol.TimedOut => "Script timed out",
            CodeModeProtocol.Terminated => "Script terminated",
            _ => "Script failed"
        }).Append('\n');
        builder.Append("Wall time ")
            .Append(elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture))
            .Append(" seconds\n");
        builder.Append("Output:");
        foreach (var item in output.Where(static item => item.Text is not null))
            builder.Append('\n').Append(item.Text);
        if (outcome?.StoreRejected == true)
            builder.Append("\nStore writes were not saved: the store would exceed ")
                .Append(CodeModeStore.MaxBytes / (1024 * 1024))
                .Append(" MiB.");
        if (status == CodeModeProtocol.Failed)
        {
            builder.Append("\nScript error:\n").Append(outcome!.Error ?? "The script failed.");
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
        var maxChars = (int)Math.Min(int.MaxValue, (long)maxOutputTokens * 4);
        if (text.Length > maxChars)
        {
            text = (string)ToolResultProcessor.Process(
                context.ToolName.Name,
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
        var structured = new JsonObject
        {
            ["status"] = status,
            ["wallTimeMs"] = (long)elapsed.TotalMilliseconds,
            ["nestedCalls"] = calls.Count
        };
        if (status == CodeModeProtocol.Running)
            structured["cellId"] = cell.Id;
        if (status == CodeModeProtocol.Failed)
            structured["error"] = outcome!.Error ?? "The script failed.";
        return ToolExecutionResult.Succeeded(
            text,
            structuredContent: JsonSerializer.SerializeToElement(structured),
            contentItems: images.Count == 0 ? null : [new TextContent(text), .. images]);
    }
}
