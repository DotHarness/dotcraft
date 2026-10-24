using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private WorktreeEnvironmentCoordinator? _worktreeEnvironmentCoordinator;

    private WorktreeEnvironmentCoordinator WorktreeEnvironments =>
        LazyInitializer.EnsureInitialized(ref _worktreeEnvironmentCoordinator, () => new WorktreeEnvironmentCoordinator(this));

    public void CancelWorktreeOperation(string operationId) =>
        WorktreeEnvironments.Cancel(operationId);

    public async Task<WorktreeSetupResult> RunWorktreeSetupAsync(
        WorktreeRunSetupOptions options,
        CancellationToken ct = default) =>
        await WorktreeEnvironments.RunSetupAsync(options, ct);

    public async Task<ThreadWorktreeInfo> SetWorktreeEnvironmentAsync(
        WorktreeSetEnvironmentOptions options,
        CancellationToken ct = default) =>
        await WorktreeEnvironments.SetEnvironmentAsync(options, ct);

    private sealed class WorktreeEnvironmentCoordinator(SessionService owner)
    {
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _operations = new(StringComparer.Ordinal);

        public static string SourceWorkspaceOf(ThreadWorktreeInfo worktree) =>
            string.IsNullOrWhiteSpace(worktree.SourceWorkspacePath) ? worktree.WorkspacePath : worktree.SourceWorkspacePath;

        public void Cancel(string operationId)
        {
            if (string.IsNullOrWhiteSpace(operationId)
                || !_operations.TryGetValue(operationId.Trim(), out var cancellation))
            {
                return;
            }

            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public Operation Begin(string? operationId, CancellationToken ct)
        {
            var id = string.IsNullOrWhiteSpace(operationId) ? null : operationId.Trim();
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (id != null && !_operations.TryAdd(id, cancellation))
            {
                cancellation.Dispose();
                throw new ArgumentException($"Operation '{id}' is already running.");
            }

            return new Operation(this, id, cancellation, ct);
        }

        public async Task<WorktreeSetupResult?> SetUpAsync(
            WorktreeEnvironment? environment,
            ThreadWorktreeInfo worktree,
            string sourceExecutionWorkspace,
            Action<string, string>? progress,
            Action<string>? output,
            CancellationToken ct)
        {
            const string step = WorktreeCreateSteps.SetupEnvironment;
            if (environment == null || environment.SetupScript.Length == 0)
            {
                progress?.Invoke(step, WorktreeHandoffStepStatuses.Skipped);
                return environment == null ? null : new WorktreeSetupResult { Status = WorktreeSetupStatuses.Skipped };
            }

            progress?.Invoke(step, WorktreeHandoffStepStatuses.Started);
            var run = await RunSetupScriptAsync(environment, worktree, sourceExecutionWorkspace, output, ct);
            if (run.Cancelled)
            {
                progress?.Invoke(step, WorktreeHandoffStepStatuses.Failed);
                throw new OperationCanceledException(ct);
            }

            progress?.Invoke(step, run.Succeeded ? WorktreeHandoffStepStatuses.Completed : WorktreeHandoffStepStatuses.Failed);
            return ToSetupResult(run);
        }

        public async Task<WorktreeSetupResult> RunSetupAsync(WorktreeRunSetupOptions options, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(options);
            var thread = await owner.GetOrLoadThreadAsync(NormalizeRequiredThreadId(options.ThreadId), ct);
            if (thread.Worktree is not { } worktree || string.IsNullOrWhiteSpace(worktree.EnvironmentPath))
            {
                throw new WorktreeEnvironmentException(
                    WorktreeEnvironmentErrorCodes.EnvironmentInvalid,
                    "No environment is recorded on this conversation's worktree.");
            }

            var environment = WorktreeEnvironment.Require(worktree.EnvironmentPath, SourceWorkspaceOf(worktree));
            using var operation = Begin(options.OperationId, ct);
            if (environment.SetupScript.Length == 0)
                return new WorktreeSetupResult { Status = WorktreeSetupStatuses.Skipped };

            var run = await RunSetupScriptAsync(environment, worktree, SourceWorkspaceOf(worktree), options.Output, operation.Token);
            ct.ThrowIfCancellationRequested();
            return ToSetupResult(run);
        }

        public async Task<ThreadWorktreeInfo> SetEnvironmentAsync(WorktreeSetEnvironmentOptions options, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (string.IsNullOrWhiteSpace(options.Path))
                throw new ArgumentException("'path' is required.");

            var fullPath = Path.GetFullPath(options.Path.Trim());
            var status = (await owner.Worktrees.ListAsync(ct)).FirstOrDefault(entry =>
                             string.Equals(Path.GetFullPath(entry.Path), fullPath, StringComparison.OrdinalIgnoreCase))
                         ?? throw new ArgumentException($"'{options.Path}' is not a managed worktree.");
            if (status.ThreadId is not { } threadId)
                throw new InvalidOperationException("Only a worktree that a conversation works in can record an environment.");

            var environmentPath = string.IsNullOrWhiteSpace(options.EnvironmentPath)
                ? null
                : WorktreeEnvironment.Require(options.EnvironmentPath, SourceWorkspaceOf(status.Worktree)).FilePath;

            using (await owner.Gate.AcquireAsync(threadId, ct))
            {
                var thread = await owner.GetOrLoadThreadAsync(threadId, ct);
                var worktree = thread.Worktree
                               ?? throw new InvalidOperationException("Only a worktree that a conversation works in can record an environment.");
                if (string.Equals(worktree.EnvironmentPath, environmentPath, StringComparison.Ordinal))
                    return worktree;

                var updated = worktree with { EnvironmentPath = environmentPath };
                thread.Worktree = updated;
                await owner.PersistThreadWithMaterializationAsync(thread, ct);
                owner.ThreadUpdatedForBroadcast?.Invoke(thread);
                return updated;
            }
        }

        public async Task EnsureCleanedUpAsync(
            ThreadWorktreeInfo worktree,
            Action<string, string>? progress,
            CancellationToken ct)
        {
            var run = await CleanUpAsync(worktree, progress, ct);
            if (run is null || run.Succeeded)
                return;
            ct.ThrowIfCancellationRequested();
            throw new WorktreeEnvironmentException(
                WorktreeEnvironmentErrorCodes.CleanupFailed,
                run.ExitCode is { } exitCode
                    ? $"The environment cleanup script failed with exit code {exitCode}."
                    : "The environment cleanup script failed.")
            {
                WorktreePath = worktree.Path,
                ExitCode = run.ExitCode,
                OutputTail = run.OutputTail
            };
        }

        public async Task<bool> TryCleanUpAsync(ThreadWorktreeInfo worktree, string threadId, CancellationToken ct)
        {
            var run = await CleanUpAsync(worktree, progress: null, ct);
            if (run is null || run.Succeeded)
                return true;
            owner.Logger?.LogWarning(
                "Kept the worktree {Path} of thread {ThreadId} because its environment cleanup failed (exit code {ExitCode}).",
                worktree.Path,
                threadId,
                run.ExitCode);
            return false;
        }

        private static async Task<WorktreeScriptResult?> CleanUpAsync(
            ThreadWorktreeInfo worktree,
            Action<string, string>? progress,
            CancellationToken ct)
        {
            const string step = WorktreeEnvironmentSteps.CleanupEnvironment;
            WorktreeEnvironment? environment = null;
            string? invalid = null;
            if (!string.IsNullOrWhiteSpace(worktree.EnvironmentPath) && Directory.Exists(worktree.Path))
            {
                try
                {
                    environment = WorktreeEnvironment.Read(worktree.EnvironmentPath, SourceWorkspaceOf(worktree));
                }
                catch (WorktreeEnvironmentException ex)
                {
                    invalid = ex.Message;
                }
            }

            if (invalid == null && (environment == null || environment.CleanupScript.Length == 0))
            {
                progress?.Invoke(step, WorktreeHandoffStepStatuses.Skipped);
                return null;
            }

            progress?.Invoke(step, WorktreeHandoffStepStatuses.Started);
            var run = environment == null
                ? new WorktreeScriptResult(null, invalid + Environment.NewLine, Cancelled: false)
                : await WorktreeScriptRunner.RunAsync(
                    environment.CleanupScript,
                    environment.RootIn(worktree.Path),
                    variables: null,
                    output: null,
                    ct);
            progress?.Invoke(step, run.Succeeded ? WorktreeHandoffStepStatuses.Completed : WorktreeHandoffStepStatuses.Failed);
            return run;
        }

        private static Task<WorktreeScriptResult> RunSetupScriptAsync(
            WorktreeEnvironment environment,
            ThreadWorktreeInfo worktree,
            string sourceExecutionWorkspace,
            Action<string>? output,
            CancellationToken ct) =>
            WorktreeScriptRunner.RunAsync(
                environment.SetupScript,
                environment.RootIn(worktree.Path),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [WorktreeScriptRunner.SourceTreeVariable] = sourceExecutionWorkspace,
                    [WorktreeScriptRunner.WorktreeVariable] = worktree.Path
                },
                output,
                ct);

        private static WorktreeSetupResult ToSetupResult(WorktreeScriptResult run) => new()
        {
            Status = run.Succeeded ? WorktreeSetupStatuses.Succeeded : WorktreeSetupStatuses.Failed,
            ExitCode = run.ExitCode,
            OutputTail = run.OutputTail
        };

        private void End(string? id, CancellationTokenSource cancellation)
        {
            if (id != null)
                _operations.TryRemove(new KeyValuePair<string, CancellationTokenSource>(id, cancellation));
            cancellation.Dispose();
        }

        public sealed class Operation(
            WorktreeEnvironmentCoordinator owner,
            string? id,
            CancellationTokenSource cancellation,
            CancellationToken requestToken) : IDisposable
        {
            public CancellationToken Token { get; } = cancellation.Token;

            public bool IsCancelled => cancellation.IsCancellationRequested && !requestToken.IsCancellationRequested;

            public void Dispose() => owner.End(id, cancellation);
        }
    }
}
