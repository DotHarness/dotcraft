using System.Diagnostics;
using DotCraft.Security.ShellCommands;

namespace DotCraft.Tools.BackgroundTerminals;

public sealed partial class BackgroundTerminalService
{
    private sealed class ActiveTerminal
    {
        private readonly BackgroundTerminalService _owner;
        private readonly object _sync = new();
        private readonly TerminalOutputPump _output;
        private bool _completionStarted;
        private string _status = BackgroundTerminalStatus.Running;
        private int? _exitCode;
        private DateTimeOffset? _completedAt;

        public ActiveTerminal(
            string sessionId,
            string metadataPath,
            string outputPath,
            BackgroundTerminalStartRequest request,
            Process process,
            DateTimeOffset startedAt,
            ShellStdinSession stdinSession,
            BackgroundTerminalService owner)
        {
            SessionId = sessionId;
            MetadataPath = metadataPath;
            OutputPath = outputPath;
            Request = request;
            Process = process;
            StartedAt = startedAt;
            StdinSession = stdinSession;
            _owner = owner;
            _output = new TerminalOutputPump(process, outputPath, owner._config.OutputMaxBytes,
                text => owner.Raise("outputDelta", CreateSnapshot(includeOutput: false), text));
        }

        public string SessionId { get; }

        public string MetadataPath { get; }

        public string OutputPath { get; }

        public string ThreadId => Request.ThreadId;

        public BackgroundTerminalStartRequest Request { get; }

        public Process Process { get; }

        public DateTimeOffset StartedAt { get; }

        public ShellStdinSession StdinSession { get; }

        public TaskCompletionSource MetadataCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void BeginReading() => _output.Start();

        public bool TryBeginCompletion()
        {
            lock (_sync)
            {
                if (_completionStarted)
                    return false;

                _completionStarted = true;
                return true;
            }
        }

        public void FinishCompletion(string status, int? exitCode)
        {
            lock (_sync)
            {
                _status = status;
                _exitCode = exitCode;
                _completedAt = DateTimeOffset.UtcNow;
            }
        }

        public Task DrainOutputAsync() => _output.DrainAsync();

        public void SignalCompletionPublished()
        {
            MetadataCompleted.TrySetResult();
        }

        public void SignalCompletionFailed(Exception error)
        {
            MetadataCompleted.TrySetException(error);
        }

        public async Task WaitForCompletionMetadataAsync(CancellationToken ct)
        {
            await MetadataCompleted.Task.WaitAsync(ct).ConfigureAwait(false);
        }

        public BackgroundTerminalMetadata ToMetadata(string? status = null)
        {
            lock (_sync)
            {
                return new BackgroundTerminalMetadata
                {
                    SessionId = SessionId,
                    ThreadId = Request.ThreadId,
                    TurnId = Request.TurnId,
                    CallId = Request.CallId,
                    Command = Request.Command,
                    WorkingDirectory = Request.WorkingDirectory,
                    Source = Request.Source,
                    Status = status ?? _status,
                    OutputPath = OutputPath,
                    MetadataPath = MetadataPath,
                    ExitCode = _exitCode,
                    StartedAt = StartedAt,
                    CompletedAt = _completedAt
                };
            }
        }

        public BackgroundTerminalSnapshot CreateSnapshot(
            string? status = null,
            int? maxOutputChars = null,
            string? backgroundReason = null,
            bool includeOutput = true)
        {
            int? exitCode;
            DateTimeOffset? completedAt;
            string effectiveStatus;
            lock (_sync)
            {
                exitCode = _exitCode;
                completedAt = _completedAt;
                effectiveStatus = status ?? _status;
            }

            var (limited, original, truncated) = includeOutput
                ? _output.Snapshot(maxOutputChars ?? Request.MaxOutputChars)
                : (string.Empty, 0, false);
            return new BackgroundTerminalSnapshot
            {
                SessionId = SessionId,
                ThreadId = Request.ThreadId,
                TurnId = Request.TurnId,
                CallId = Request.CallId,
                Command = Request.Command,
                WorkingDirectory = Request.WorkingDirectory,
                Source = Request.Source,
                Status = effectiveStatus,
                Output = limited,
                OutputPath = OutputPath,
                ExitCode = effectiveStatus == BackgroundTerminalStatus.Running ? null : exitCode,
                StartedAt = StartedAt,
                CompletedAt = completedAt,
                WallTimeMs = (long)Math.Max(0, ((completedAt ?? DateTimeOffset.UtcNow) - StartedAt).TotalMilliseconds),
                OriginalOutputChars = original,
                Truncated = truncated,
                BackgroundReason = backgroundReason ?? (Request.RunInBackground ? "runInBackground" : null)
            };
        }

    }

    private sealed record BackgroundTerminalMetadata
    {
        public string SessionId { get; init; } = string.Empty;

        public string ThreadId { get; init; } = string.Empty;

        public string? TurnId { get; init; }

        public string? CallId { get; init; }

        public string Command { get; init; } = string.Empty;

        public string WorkingDirectory { get; init; } = string.Empty;

        public string Source { get; init; } = "host";

        public string Status { get; init; } = BackgroundTerminalStatus.Running;

        public string OutputPath { get; init; } = string.Empty;

        public string MetadataPath { get; init; } = string.Empty;

        public int? ExitCode { get; init; }

        public DateTimeOffset StartedAt { get; init; }

        public DateTimeOffset? CompletedAt { get; init; }

        public BackgroundTerminalSnapshot ToSnapshot(string output, int originalChars, bool truncated) => new()
        {
            SessionId = SessionId,
            ThreadId = ThreadId,
            TurnId = TurnId,
            CallId = CallId,
            Command = Command,
            WorkingDirectory = WorkingDirectory,
            Source = Source,
            Status = Status,
            Output = output,
            OutputPath = OutputPath,
            ExitCode = ExitCode,
            StartedAt = StartedAt,
            CompletedAt = CompletedAt,
            WallTimeMs = (long)Math.Max(0, ((CompletedAt ?? DateTimeOffset.UtcNow) - StartedAt).TotalMilliseconds),
            OriginalOutputChars = originalChars,
            Truncated = truncated
        };
    }
}
