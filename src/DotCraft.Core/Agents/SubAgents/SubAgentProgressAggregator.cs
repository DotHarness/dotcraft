using DotCraft.Sessions;

namespace DotCraft.Agents;

/// <summary>
/// Periodically snapshots <see cref="SubAgentProgressBridge"/> entries and writes
/// <see cref="SessionEventType.SubAgentProgress"/> events into the Turn event stream.
/// This bridges server-side SubAgent progress data to the Wire Protocol layer,
/// enabling connected clients to display real-time SubAgent progress.
///
/// The aggregator is started when the first <c>SpawnAgent</c> tool call begins within a Turn,
/// and stops when the Turn ends or all tracked SubAgents have completed.
/// </summary>
internal sealed class SubAgentProgressAggregator : IAsyncDisposable
{
    private readonly SessionEventChannel _eventChannel;
    private readonly string _threadId;
    private readonly string _turnId;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<string> _trackedLabels = [];
    private readonly object _labelLock = new();
    private Task? _runTask;

    /// <summary>
    /// Creates a new aggregator bound to a Turn's event channel.
    /// </summary>
    /// <param name="eventChannel">The Turn's event channel for writing SubAgent progress events.</param>
    /// <param name="threadId">Parent thread ID (for event envelope).</param>
    /// <param name="turnId">Parent turn ID (for event envelope).</param>
    /// <param name="interval">Snapshot interval. Defaults to ~200ms.</param>
    public SubAgentProgressAggregator(
        SessionEventChannel eventChannel,
        string threadId,
        string turnId,
        TimeSpan? interval = null)
    {
        _eventChannel = eventChannel;
        _threadId = threadId;
        _turnId = turnId;
        _interval = interval ?? TimeSpan.FromMilliseconds(200);
    }

    /// <summary>
    /// Registers a SubAgent label to track. Thread-safe; can be called from any thread.
    /// If the aggregator has not been started yet, calling this will start it.
    /// </summary>
    public void TrackLabel(string label)
    {
        lock (_labelLock)
        {
            _trackedLabels.Add(label);
        }

        // Auto-start on first label
        if (_runTask == null)
            Start();
    }

    /// <summary>
    /// Starts the periodic aggregation loop.
    /// </summary>
    public void Start()
    {
        if (_runTask != null) return;
        _runTask = RunLoopAsync(_cts.Token);
    }

    /// <summary>
    /// Stops the aggregation loop and emits a final snapshot.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();

        if (_runTask != null)
        {
            try { await _runTask; }
            catch (OperationCanceledException) { }
        }

        // Emit a final snapshot so clients see the completed state
        EmitSnapshot();

        _cts.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(_interval, ct);
                EmitSnapshot();

                // Check if all tracked SubAgents are completed
                if (AllCompleted())
                    break;
            }
        }
        catch (OperationCanceledException) { }
    }

    private void EmitSnapshot()
    {
        string[] labels;
        lock (_labelLock)
        {
            if (_trackedLabels.Count == 0) return;
            labels = [.. _trackedLabels];
        }

        var entries = new List<SubAgentProgressEntry>(labels.Length);
        foreach (var label in labels)
        {
            var progress = SubAgentProgressBridge.TryGet(label);
            entries.Add(new SubAgentProgressEntry
            {
                Label = label,
                CurrentTool = progress?.CurrentTool ?? progress?.LastTool,
                CurrentToolDisplay = progress?.CurrentToolDisplay ?? progress?.LastToolDisplay,
                InputTokens = progress?.InputTokens ?? 0,
                OutputTokens = progress?.OutputTokens ?? 0,
                CachedInputTokens = progress?.CachedInputTokens ?? 0,
                CacheWriteInputTokens = progress?.CacheWriteInputTokens ?? 0,
                ReasoningOutputTokens = progress?.ReasoningOutputTokens ?? 0,
                IsCompleted = progress?.IsCompleted ?? false
            });
        }

        var payload = new SubAgentProgressPayload { Entries = entries };
        _eventChannel.EmitSubAgentProgress(payload);
    }

    private bool AllCompleted()
    {
        string[] labels;
        lock (_labelLock)
        {
            labels = [.. _trackedLabels];
        }

        foreach (var label in labels)
        {
            var progress = SubAgentProgressBridge.TryGet(label);
            if (progress == null || !progress.IsCompleted)
                return false;
        }

        return labels.Length > 0;
    }
}
