using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using DotCraft.Tools;
using DotCraft.Utilities;

namespace DotCraft.CodeMode;

/// <summary>A running program. It can outlive the <c>CodeMode</c> call that started it, until its Turn ends.</summary>
internal sealed class CodeModeCell
{
    private readonly Lock _gate = new();
    private readonly List<CodeModeOutputItem> _output = [];
    private readonly List<CodeModeNestedCall> _calls = [];
    private readonly CancellationTokenSource _termination = new();

    private CodeModeCell(string id, ToolInvocationContext context, TimeSpan timeout)
    {
        Id = id;
        ThreadId = context.ThreadId;
        TurnId = context.TurnId;
        Deadline = new PausableDeadline(timeout, _termination.Token);
        Approvals = new CodeModeApprovals(Deadline);
    }

    public string Id { get; }
    public string ThreadId { get; }
    public string? TurnId { get; }
    public PausableDeadline Deadline { get; }
    public CodeModeApprovals Approvals { get; }
    public CancellationToken TerminationToken => _termination.Token;
    public bool TerminationRequested => _termination.IsCancellationRequested;
    public Task<CodeModeCellOutcome> Completion { get; private set; } = null!;

    public IReadOnlyList<CodeModeNestedCall> Calls
    {
        get
        {
            lock (_gate)
                return _calls.ToArray();
        }
    }

    public static CodeModeCell Start(
        CodeModeExecDependencies dependencies,
        ToolInvocationContext context,
        CodeModeCellSession session,
        CodeModeProgram program)
    {
        var cell = new CodeModeCell(session.Id, context, program.Timeout);
        cell.Completion = Task.Run(() => cell.RunAsync(dependencies, context, session));
        return cell;
    }

    public void Terminate() => _termination.Cancel();

    public void AddOutput(CodeModeOutputItem item)
    {
        lock (_gate)
            _output.Add(item);
    }

    public void AddCall(CodeModeNestedCall call)
    {
        lock (_gate)
            _calls.Add(call);
    }

    public IReadOnlyList<CodeModeOutputItem> TakeOutput()
    {
        lock (_gate)
        {
            var output = _output.ToArray();
            _output.Clear();
            return output;
        }
    }

    private async Task<CodeModeCellOutcome> RunAsync(
        CodeModeExecDependencies dependencies,
        ToolInvocationContext context,
        CodeModeCellSession session)
    {
        try
        {
            using var run = new CodeModeCellRun(dependencies, context, session, this);
            var outcome = await run.RunAsync().ConfigureAwait(false);
            if (outcome is not { Status: CodeModeProtocol.Completed, StoreWrites: { } writes })
                return outcome;
            var saved = dependencies.Store.TryApply(
                context.ThreadId,
                writes["set"] as JsonObject ?? new JsonObject(),
                (writes["deleted"] as JsonArray ?? []).Select(static key => key!.GetValue<string>()));
            return saved ? outcome : outcome with { StoreRejected = true };
        }
        finally
        {
            session.Release();
            Deadline.Dispose();
        }
    }
}

/// <summary>The open cells of one worker host, each closed once its final result is delivered or its Turn ends.</summary>
internal sealed class CodeModeCellRegistry
{
    private readonly ConcurrentDictionary<string, CodeModeCell> _cells = new(StringComparer.Ordinal);

    public void Add(CodeModeCell cell) => _cells[cell.Id] = cell;

    public CodeModeCell? Find(string threadId, string cellId) =>
        _cells.TryGetValue(cellId, out var cell) && cell.ThreadId == threadId ? cell : null;

    /// <summary>Closes the cell, returning false when another caller already closed it.</summary>
    public bool Close(CodeModeCell cell) => _cells.TryRemove(new KeyValuePair<string, CodeModeCell>(cell.Id, cell));

    public void TerminateTurn(string threadId, string turnId) =>
        TerminateWhere(cell => cell.ThreadId == threadId && cell.TurnId == turnId);

    public void TerminateThread(string threadId) => TerminateWhere(cell => cell.ThreadId == threadId);

    private void TerminateWhere(Func<CodeModeCell, bool> match)
    {
        foreach (var cell in _cells.Values.Where(match))
        {
            if (Close(cell))
                cell.Terminate();
        }
    }
}
