
namespace DotCraft.Security;

/// <summary>
/// Console-based implementation of approval service.
/// Prompts user interactively for approval with session-based storage.
/// </summary>
public sealed class ConsoleApprovalService : IApprovalService
{
    private readonly IInteractiveApprovalPrompt _prompt;
    private readonly ApprovalStore? _store;

    public ConsoleApprovalService(ApprovalStore? store = null)
        : this(RejectingInteractiveApprovalPrompt.Instance, store)
    {
    }

    public ConsoleApprovalService(IInteractiveApprovalPrompt prompt, ApprovalStore? store = null)
    {
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        _store = store;
    }

    // Session-based operation approvals (cleared when process exits)
    private readonly HashSet<string> _sessionFileOperations = [];

    private readonly HashSet<string> _sessionShellCommands = [];

    private readonly HashSet<string> _sessionResourceScopes = [];

    private readonly Lock _sessionLock = new();

    public async Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null)
    {
        lock (_sessionLock)
        {
            if (_sessionFileOperations.Contains(operation.ToLowerInvariant()))
            {
                return true;
            }
        }

        if (_store?.IsFileOperationApproved(operation, path) == true)
        {
            return true;
        }

        var choice = _prompt.RequestFileApproval(operation, path);

        switch (choice)
        {
            case InteractiveApprovalDecision.Always:
                _store?.RecordFileOperation(operation, path);
                return true;

            case InteractiveApprovalDecision.Session:
                lock (_sessionLock)
                {
                    _sessionFileOperations.Add(operation.ToLowerInvariant());
                }
                return true;

            case InteractiveApprovalDecision.Once:
                return true;

            case InteractiveApprovalDecision.Reject:
            default:
                return false;
        }
    }

    public async Task<bool> RequestShellApprovalAsync(string command, string? workingDir, ApprovalContext? context = null)
    {
        lock (_sessionLock)
        {
            if (_sessionShellCommands.Contains("*"))
            {
                return true;
            }
        }

        if (_store?.IsShellCommandApproved(command, workingDir) == true)
        {
            return true;
        }

        var choice = _prompt.RequestShellApproval(command, workingDir);

        switch (choice)
        {
            case InteractiveApprovalDecision.Always:
                _store?.RecordShellCommand(command, workingDir);
                return true;

            case InteractiveApprovalDecision.Session:
                lock (_sessionLock)
                {
                    _sessionShellCommands.Add("*");
                }
                return true;

            case InteractiveApprovalDecision.Once:
                return true;

            case InteractiveApprovalDecision.Reject:
            default:
                return false;
        }
    }

    public async Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null)
    {
        var scopeKey = $"{kind}:{operation}".ToLowerInvariant();
        lock (_sessionLock)
        {
            if (_sessionResourceScopes.Contains(scopeKey))
            {
                return true;
            }
        }

        // Reuse the file approval prompt to avoid adding new localization keys; the
        // operation / target columns still convey the resource identity clearly.
        var displayOperation = $"{kind}:{operation}";
        var choice = _prompt.RequestFileApproval(displayOperation, target);

        switch (choice)
        {
            case InteractiveApprovalDecision.Always:
            case InteractiveApprovalDecision.Session:
                lock (_sessionLock)
                {
                    _sessionResourceScopes.Add(scopeKey);
                }
                return true;

            case InteractiveApprovalDecision.Once:
                return true;

            case InteractiveApprovalDecision.Reject:
            default:
                return false;
        }
    }

    private sealed class RejectingInteractiveApprovalPrompt : IInteractiveApprovalPrompt
    {
        internal static readonly RejectingInteractiveApprovalPrompt Instance = new();

        public InteractiveApprovalDecision RequestFileApproval(string operation, string path) =>
            InteractiveApprovalDecision.Reject;

        public InteractiveApprovalDecision RequestShellApproval(string command, string? workingDirectory) =>
            InteractiveApprovalDecision.Reject;
    }
}
