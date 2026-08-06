namespace DotCraft.Hooks;

using DotCraft.Agents;

internal sealed record ToolHookFeedback(HookEvent Event, string Text, bool IsBlockingFeedback);

internal sealed class ToolHookFeedbackCollector
{
    private readonly List<ToolHookFeedback> _feedback = [];

    public void Add(HookEvent evt, HookResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.AdditionalContext))
        {
            _feedback.Add(new ToolHookFeedback(evt, result.AdditionalContext.Trim(), IsBlockingFeedback: false));
            StreamingToolFeedbackRuntimeScope.Report(evt.ToString(), result.AdditionalContext, isBlockingFeedback: false);
        }

        if (result.ExitCode == 2 && !string.IsNullOrWhiteSpace(result.BlockReason))
        {
            _feedback.Add(new ToolHookFeedback(evt, result.BlockReason.Trim(), IsBlockingFeedback: true));
            StreamingToolFeedbackRuntimeScope.Report(evt.ToString(), result.BlockReason, isBlockingFeedback: true);
        }
    }

    public IReadOnlyList<ToolHookFeedback> Snapshot() =>
        _feedback.Count == 0 ? [] : _feedback.ToArray();
}

internal static class ToolHookFeedbackScope
{
    private static readonly AsyncLocal<ToolHookFeedbackCollector?> CurrentCollector = new();

    public static ToolHookFeedbackCollector? Current => CurrentCollector.Value;

    public static void Record(HookEvent evt, HookResult result)
    {
        if (CurrentCollector.Value is { } collector)
        {
            collector.Add(evt, result);
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.AdditionalContext))
            StreamingToolFeedbackRuntimeScope.Report(evt.ToString(), result.AdditionalContext, isBlockingFeedback: false);
        if (result.ExitCode == 2 && !string.IsNullOrWhiteSpace(result.BlockReason))
            StreamingToolFeedbackRuntimeScope.Report(evt.ToString(), result.BlockReason, isBlockingFeedback: true);
    }

    public static IDisposable Set(ToolHookFeedbackCollector collector)
    {
        var previous = CurrentCollector.Value;
        CurrentCollector.Value = collector;
        return new Scope(previous);
    }

    private sealed class Scope(ToolHookFeedbackCollector? previous) : IDisposable
    {
        public void Dispose() => CurrentCollector.Value = previous;
    }
}
