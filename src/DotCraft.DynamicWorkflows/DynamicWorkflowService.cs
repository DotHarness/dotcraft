using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Processes;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.DynamicWorkflows;

public sealed partial class DynamicWorkflowService(
    string workspacePath,
    DynamicWorkflowStore store,
    DynamicWorkflowParser parser,
    StructuredWorkflowResultRegistry structuredResults,
    IManagedChildProcessFactory processFactory,
    AppConfig runtimeConfig,
    IReadOnlyList<SubAgentRoleConfig> roleConfigs,
    ILogger<DynamicWorkflowService>? logger = null)
    : IDynamicWorkflowService, ISessionServiceConsumer, IThreadLifecycleObserver
{
    private sealed class ActiveRun(DynamicWorkflowRun state, IReadOnlyList<DynamicWorkflowReplayCall>? replayCalls = null)
    {
        public DynamicWorkflowRun State = state;
        public CancellationTokenSource Cancellation { get; } = new();
        public SemaphoreSlim Capacity { get; } = new(state.Limits.MaxConcurrency, state.Limits.MaxConcurrency);
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public SemaphoreSlim JournalGate { get; } = new(1, 1);
        public ManagedChildProcess? Worker;
        public Task? Execution;
        public int AgentCalls;
        public long LogBytes;
        public string CancellationStatus = DynamicWorkflowStatuses.Cancelled;
        public string? CancellationError;
        public IReadOnlyList<DynamicWorkflowReplayCall> ReplayCalls = replayCalls ?? [];
        public int ReplayCursor;
        public bool ReplayDiverged;
        public object ReplayGate { get; } = new();
        public string? CurrentPhase;
    }

    private readonly ConcurrentDictionary<string, ActiveRun> _active = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _runsFromThisInstance = new(StringComparer.Ordinal);
    private readonly AppConfig _runtimeConfig = runtimeConfig ?? throw new ArgumentNullException(nameof(runtimeConfig));
    private ISessionService? _sessionService;
    private volatile bool _accepting;

    public event Action<DynamicWorkflowRunChanged>? RunChanged;

    internal bool IsRunFromCurrentInstance(string runId) => _runsFromThisInstance.ContainsKey(runId);

    public void SetSessionService(ISessionService service) =>
        _sessionService = service ?? throw new ArgumentNullException(nameof(service));

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        foreach (var runId in store.EnumerateRunIds())
        {
            var state = await store.ReadStateAsync(runId, cancellationToken).ConfigureAwait(false);
            if (state == null) continue;
            if (state.Status == DynamicWorkflowStatuses.Running)
            {
                var interrupted = state with
                {
                    Status = DynamicWorkflowStatuses.Interrupted,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Error = "The AppServer stopped before this workflow completed."
                };
                await store.WriteStateAsync(interrupted, cancellationToken).ConfigureAwait(false);
                await store.AppendJournalAsync(runId, "run.interrupted", null, cancellationToken).ConfigureAwait(false);
                continue;
            }
            if (state.NotificationStatus == "pending"
                && state.Status is DynamicWorkflowStatuses.Succeeded or DynamicWorkflowStatuses.Failed or DynamicWorkflowStatuses.Stopped)
            {
                var pending = new ActiveRun(state);
                try { await NotifyParentAsync(pending).ConfigureAwait(false); }
                catch (Exception ex) { logger?.LogWarning(ex, "Failed to reconcile workflow notification for {RunId}.", runId); }
                finally
                {
                    pending.Capacity.Dispose();
                    pending.Gate.Dispose();
                    pending.JournalGate.Dispose();
                    pending.Cancellation.Dispose();
                }
            }
        }
        _accepting = true;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _accepting = false;
        foreach (var active in _active.Values)
        {
            active.CancellationStatus = DynamicWorkflowStatuses.Interrupted;
            active.CancellationError = "The AppServer stopped before this workflow completed.";
        }
        foreach (var active in _active.Values) active.Cancellation.Cancel();
        var executions = _active.Values.Select(value => value.Execution).OfType<Task>().ToArray();
        if (executions.Length > 0)
        {
            try { await Task.WhenAll(executions).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException) { }
        }
    }

    public async Task<DynamicWorkflowRun> StartInlineAsync(
        DynamicWorkflowStartRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_accepting) throw new InvalidOperationException("Dynamic Workflow runtime is not accepting work.");
        var session = _sessionService ?? throw new InvalidOperationException("Session service has not been bound.");
        var parent = await session.GetThreadAsync(request.ParentThreadId, cancellationToken).ConfigureAwait(false);
        if (!parent.Turns.Any(turn => string.Equals(turn.Id, request.ParentTurnId, StringComparison.Ordinal)))
            throw new ArgumentException("Parent Turn does not exist in the requested thread.", nameof(request));
        var limits = request.LimitsOverride ?? new DynamicWorkflowLimits();
        limits.Validate();
        if (request.TokenBudget is <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Token budget must be positive when configured.");
        var args = CanonicalJson.Normalize(request.Args);
        if (Encoding.UTF8.GetByteCount(args?.ToJsonString() ?? "null") > limits.MaxArgsBytes)
            throw new DynamicWorkflowValidationException("args_too_large", "Workflow arguments exceed the configured size limit.");
        var parsed = parser.Parse(request.Script, limits.MaxScriptBytes);
        var runId = NewRunId();
        var attemptId = "attempt_001";
        var scriptPath = Path.Combine(store.GetRunDirectory(runId), "script.js");
        var state = new DynamicWorkflowRun
        {
            RunId = runId,
            AttemptId = attemptId,
            Name = parsed.Metadata.Name,
            Description = parsed.Metadata.Description,
            DeclaredPhases = parsed.Metadata.Phases,
            ParentThreadId = request.ParentThreadId,
            ParentTurnId = request.ParentTurnId,
            ScriptPath = scriptPath,
            ScriptHash = parsed.SourceHash,
            Status = DynamicWorkflowStatuses.Running,
            CreatedAt = DateTimeOffset.UtcNow,
            Args = args,
            TokenBudget = request.TokenBudget,
            Limits = limits,
            NotificationStatus = "none",
            ResumedFromRunId = request.ResumedFromRunId
        };
        await store.CreateAsync(state, request.Script, cancellationToken).ConfigureAwait(false);
        await store.AppendJournalAsync(runId, "workflow.meta", new JsonObject
        {
            ["name"] = parsed.Metadata.Name,
            ["description"] = parsed.Metadata.Description,
            ["whenToUse"] = parsed.Metadata.WhenToUse,
            ["phases"] = new JsonArray(parsed.Metadata.Phases.Select(phase => (JsonNode?)JsonValue.Create(phase)).ToArray())
        }, cancellationToken).ConfigureAwait(false);
        if (request.ResumedFromRunId != null)
            await store.AppendJournalAsync(runId, "run.resumed", new JsonObject
            {
                ["sourceRunId"] = request.ResumedFromRunId,
                ["initiator"] = request.Initiator ?? "model"
            }, cancellationToken).ConfigureAwait(false);
        var active = new ActiveRun(state, request.ReplayCalls);
        if (!_active.TryAdd(runId, active)) throw new InvalidOperationException("Duplicate workflow run id.");
        _runsFromThisInstance.TryAdd(runId, 0);
        active.Execution = Task.Run(() => ExecuteRunAsync(active, request.Script), CancellationToken.None);
        PublishChanged(state, "created");
        return state;
    }

    public async Task<DynamicWorkflowRun?> GetRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        if (_active.TryGetValue(runId, out var active)) return active.State;
        return await store.ReadStateAsync(runId, cancellationToken).ConfigureAwait(false);
    }

    public Task CancelAsync(string runId, CancellationToken cancellationToken = default) =>
        CancelWithStatusAsync(runId, DynamicWorkflowStatuses.Cancelled, "Workflow was cancelled.", cancellationToken);

    public Task PauseAsync(string runId, CancellationToken cancellationToken = default) =>
        CancelWithStatusAsync(runId, DynamicWorkflowStatuses.Paused, "Workflow was paused.", cancellationToken);

    public Task StopRunAsync(string runId, CancellationToken cancellationToken = default) =>
        CancelWithStatusAsync(runId, DynamicWorkflowStatuses.Stopped, "Workflow was stopped.", cancellationToken);

    private async Task CancelWithStatusAsync(string runId, string status, string error, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_active.TryGetValue(runId, out var active))
        {
            active.CancellationStatus = status;
            active.CancellationError = error;
            active.Cancellation.Cancel();
            if (active.Execution != null) await active.Execution.WaitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        var persisted = await store.ReadStateAsync(runId, cancellationToken).ConfigureAwait(false);
        if (persisted == null) return;
        if (persisted.Status == status) return;
        if (status == DynamicWorkflowStatuses.Stopped && persisted.Status == DynamicWorkflowStatuses.Paused)
        {
            var stopped = persisted with
            {
                Status = DynamicWorkflowStatuses.Stopped,
                CompletedAt = DateTimeOffset.UtcNow,
                Error = error,
                NotificationStatus = "pending"
            };
            await store.WriteStateAsync(stopped, cancellationToken).ConfigureAwait(false);
            await store.AppendJournalAsync(runId, "run.stopped", new JsonObject { ["error"] = error }, cancellationToken).ConfigureAwait(false);
            PublishChanged(stopped, "control");
            var pending = new ActiveRun(stopped);
            try { await NotifyParentAsync(pending).ConfigureAwait(false); }
            finally
            {
                pending.Capacity.Dispose();
                pending.Gate.Dispose();
                pending.JournalGate.Dispose();
                pending.Cancellation.Dispose();
            }
        }
    }

    public async Task<DynamicWorkflowRun> ResumeAsync(
        string runId,
        string parentThreadId,
        string parentTurnId,
        JsonNode? args = null,
        CancellationToken cancellationToken = default) =>
        await ResumeCoreAsync(runId, parentThreadId, parentTurnId, args, "model", cancellationToken).ConfigureAwait(false);

    private async Task<DynamicWorkflowRun> ResumeCoreAsync(
        string runId,
        string parentThreadId,
        string parentTurnId,
        JsonNode? args,
        string initiator,
        CancellationToken cancellationToken)
    {
        if (!_runsFromThisInstance.ContainsKey(runId))
            throw new InvalidOperationException("Only runs created by the current AppServer instance can be resumed.");
        var prior = await GetRunAsync(runId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Workflow run '{runId}' was not found.");
        if (prior.Status is not (DynamicWorkflowStatuses.Paused or DynamicWorkflowStatuses.Stopped
            or DynamicWorkflowStatuses.Failed or DynamicWorkflowStatuses.Succeeded))
            throw new InvalidOperationException($"Workflow run in status '{prior.Status}' cannot be resumed.");
        var script = await File.ReadAllTextAsync(prior.ScriptPath, cancellationToken).ConfigureAwait(false);
        var replayCalls = await ReadReplayCallsAsync(runId, cancellationToken).ConfigureAwait(false);
        return await StartInlineAsync(new DynamicWorkflowStartRequest
        {
            ParentThreadId = parentThreadId,
            ParentTurnId = parentTurnId,
            Script = script,
            Args = args ?? prior.Args?.DeepClone(),
            TokenBudget = prior.TokenBudget,
            LimitsOverride = prior.Limits,
            ResumedFromRunId = runId,
            Initiator = initiator,
            ReplayCalls = replayCalls
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DynamicWorkflowRun> ResumeFromClientAsync(
        string runId,
        string threadId,
        JsonNode? args = null,
        CancellationToken cancellationToken = default)
    {
        var prior = await GetRunAsync(runId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Workflow run '{runId}' was not found.");
        if (!string.Equals(prior.ParentThreadId, threadId, StringComparison.Ordinal))
            throw new KeyNotFoundException($"Workflow run '{runId}' was not found.");
        return await ResumeCoreAsync(runId, threadId, prior.ParentTurnId, args, "client", cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<DynamicWorkflowReplayCall>> ReadReplayCallsAsync(string runId, CancellationToken cancellationToken)
    {
        var entries = await store.ReadJournalAsync(runId, cancellationToken).ConfigureAwait(false);
        var calls = new List<DynamicWorkflowReplayCall>();
        var indexByOperation = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var type = entry["type"]?.GetValue<string>();
            var payload = entry["payload"] as JsonObject;
            var operationId = payload?["operationId"]?.GetValue<string>();
            if (operationId == null) continue;
            if (type == "agent.requested" && payload?["fingerprint"]?.GetValue<string>() is { } fingerprint)
            {
                indexByOperation[operationId] = calls.Count;
                calls.Add(new DynamicWorkflowReplayCall(
                    fingerprint,
                    null,
                    false,
                    payload?["phase"]?.GetValue<string>(),
                    payload?["label"]?.GetValue<string>()));
            }
            else if (type == "agent.completed" && indexByOperation.TryGetValue(operationId, out var index))
            {
                calls[index] = calls[index] with
                {
                    Completed = true,
                    Result = payload?["result"]?.DeepClone(),
                    ChildThreadId = payload?["childThreadId"]?.GetValue<string>(),
                    InputTokens = payload?["inputTokens"]?.GetValue<long>() ?? 0,
                    OutputTokens = payload?["outputTokens"]?.GetValue<long>() ?? 0
                };
            }
            else if (type == "agent.failed" && indexByOperation.TryGetValue(operationId, out var failedIndex))
            {
                // A handled child failure is a completed agent() call whose public result is null.
                calls[failedIndex] = calls[failedIndex] with { Completed = true, Result = null };
            }
        }
        return calls;
    }

    public async Task OnThreadDeletingAsync(SessionThread thread, CancellationToken cancellationToken = default)
    {
        var runIds = store.EnumerateRunIds().ToArray();
        foreach (var runId in runIds)
        {
            var state = await GetRunAsync(runId, cancellationToken).ConfigureAwait(false);
            if (state == null || !string.Equals(state.ParentThreadId, thread.Id, StringComparison.Ordinal)) continue;
            await CancelAsync(runId, cancellationToken).ConfigureAwait(false);
            if (_active.TryGetValue(runId, out var active) && active.Execution != null)
            {
                try { await active.Execution.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false); }
                catch (Exception ex) when (ex is TimeoutException or OperationCanceledException) { }
            }
            await store.DeleteAsync(runId).ConfigureAwait(false);
        }
    }

    private static string NewRunId()
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
        Span<byte> bytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(bytes);
        Span<char> suffix = stackalloc char[6];
        for (var index = 0; index < suffix.Length; index++) suffix[index] = alphabet[bytes[index] % alphabet.Length];
        return $"run_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}_{suffix.ToString()}";
    }

    private void PublishChanged(DynamicWorkflowRun run, string reason) =>
        RunChanged?.Invoke(new DynamicWorkflowRunChanged(run.ParentThreadId, run.RunId, reason));

    private static ProcessStartInfo CreateWorkerStartInfo(string workingDirectory)
    {
        var processPath = Environment.ProcessPath;
        var entryAssembly = Assembly.GetEntryAssembly()?.Location;
        var loadedAppAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, "dotcraft", StringComparison.OrdinalIgnoreCase))
            ?.Location;
        var binary = !string.IsNullOrWhiteSpace(processPath)
                     && string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotcraft", StringComparison.OrdinalIgnoreCase)
            ? processPath
            : !string.IsNullOrWhiteSpace(entryAssembly)
              && string.Equals(Path.GetFileNameWithoutExtension(entryAssembly), "dotcraft", StringComparison.OrdinalIgnoreCase)
                ? entryAssembly
                : loadedAppAssembly;
        if (string.IsNullOrWhiteSpace(binary)) throw new InvalidOperationException("Cannot resolve the current DotCraft executable.");
        var startInfo = new ProcessStartInfo
        {
            FileName = binary.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? "dotnet" : binary,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        if (binary.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) startInfo.ArgumentList.Add(binary);
        startInfo.ArgumentList.Add("workflow-worker");
        return startInfo;
    }
}
