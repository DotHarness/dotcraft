
namespace DotCraft.Security;

/// <summary>
/// Console-based implementation of approval service.
/// Prompts user interactively for approval with session-based storage.
/// </summary>
public sealed class ConsoleApprovalService(ApprovalStore? store = null) : IApprovalService
{
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

        if (store?.IsFileOperationApproved(operation, path) == true)
        {
            return true;
        }

        var choice = ApprovalPrompt.RequestFileApproval(operation, path);

        switch (choice)
        {
            case ApprovalOption.Always:
                store?.RecordFileOperation(operation, path);
                return true;

            case ApprovalOption.Session:
                lock (_sessionLock)
                {
                    _sessionFileOperations.Add(operation.ToLowerInvariant());
                }
                return true;

            case ApprovalOption.Once:
                return true;

            case ApprovalOption.Reject:
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

        if (store?.IsShellCommandApproved(command, workingDir) == true)
        {
            return true;
        }

        var choice = ApprovalPrompt.RequestShellApproval(command, workingDir);

        switch (choice)
        {
            case ApprovalOption.Always:
                store?.RecordShellCommand(command, workingDir);
                return true;

            case ApprovalOption.Session:
                lock (_sessionLock)
                {
                    _sessionShellCommands.Add("*");
                }
                return true;

            case ApprovalOption.Once:
                return true;

            case ApprovalOption.Reject:
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
        var choice = ApprovalPrompt.RequestFileApproval(displayOperation, target);

        switch (choice)
        {
            case ApprovalOption.Always:
            case ApprovalOption.Session:
                lock (_sessionLock)
                {
                    _sessionResourceScopes.Add(scopeKey);
                }
                return true;

            case ApprovalOption.Once:
                return true;

            case ApprovalOption.Reject:
            default:
                return false;
        }
    }
}
