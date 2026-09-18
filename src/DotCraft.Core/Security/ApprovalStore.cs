using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using DotCraft.Security.ShellCommands;

namespace DotCraft.Security;

public sealed class ApprovalStore
{
    private const int SchemaVersion = 2;

    private readonly string _storePath;

    private readonly HashSet<string> _approvedFileOperations = [];

    private readonly HashSet<string> _approvedShellKeys = [];

    private readonly HashSet<string> _approvedResourceOperations = [];

    private readonly Lock _lock = new();

    private readonly LearnedShellRuleStore _shellRules;

    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public ApprovalStore(string workspacePath)
    {
        var securityDir = Path.Combine(workspacePath, "security");
        Directory.CreateDirectory(securityDir);
        _storePath = Path.Combine(securityDir, "approvals.json");
        _shellRules = new LearnedShellRuleStore(Path.Combine(securityDir, "shell-rules.json"));
        Load();
    }

    /// <summary>
    /// Check if a file operation is already approved.
    /// </summary>
    public bool IsFileOperationApproved(string operation, string path)
    {
        lock (_lock)
        {
            var key = ComputeFileOperationKey(operation, path);
            return _approvedFileOperations.Contains(key);
        }
    }

    public bool IsShellApproved(string approvalKeyHash)
    {
        lock (_lock)
            return _approvedShellKeys.Contains(approvalKeyHash);
    }

    /// <summary>
    /// Checks whether a generic resource operation has a persistent approval.
    /// </summary>
    public bool IsResourceOperationApproved(string kind, string operation, string target)
    {
        lock (_lock)
            return _approvedResourceOperations.Contains(ComputeResourceOperationKey(kind, operation, target));
    }

    /// <summary>
    /// Record an approved file operation.
    /// </summary>
    public void RecordFileOperation(string operation, string path)
    {
        lock (_lock)
        {
            var key = ComputeFileOperationKey(operation, path);
            if (_approvedFileOperations.Add(key))
            {
                Save();
            }
        }
    }

    public void RecordShellApproval(ShellApprovalRequest request)
    {
        foreach (var rule in request.Remember.Rules)
            _shellRules.Append(rule);

        if (!request.Remember.ExactKeyFallback && request.Remember.Rules.Count > 0)
            return;

        lock (_lock)
        {
            if (_approvedShellKeys.Add(request.ApprovalKey.Hash))
                Save();
        }
    }

    /// <summary>
    /// Persists an approval for a generic resource operation.
    /// </summary>
    public void RecordResourceOperation(string kind, string operation, string target)
    {
        lock (_lock)
        {
            if (_approvedResourceOperations.Add(ComputeResourceOperationKey(kind, operation, target)))
                Save();
        }
    }

    private static string ComputeFileOperationKey(string operation, string path)
    {
        // Normalize path and create a stable key
        var normalizedPath = Path.GetFullPath(path).ToLowerInvariant();
        var input = $"{operation.ToLowerInvariant()}:{normalizedPath}";
        return ComputeHash(input);
    }

    private static string ComputeResourceOperationKey(string kind, string operation, string target)
    {
        var input = $"{kind.Trim().ToLowerInvariant()}:{operation.Trim().ToLowerInvariant()}:{target.Trim()}";
        return ComputeHash(input);
    }

    private static string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private void Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_storePath))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(_storePath);
                var data = JsonSerializer.Deserialize<ApprovalData>(json);
                if (data != null)
                {
                    _approvedFileOperations.UnionWith(data.FileOperations ?? Array.Empty<string>());
                    _approvedResourceOperations.UnionWith(data.ResourceOperations ?? Array.Empty<string>());
                    if (data.SchemaVersion == SchemaVersion)
                        _approvedShellKeys.UnionWith(data.ShellKeys ?? Array.Empty<string>());
                }
            }
            catch
            {
                // If file is corrupted, start fresh
                _approvedFileOperations.Clear();
                _approvedShellKeys.Clear();
                _approvedResourceOperations.Clear();
            }
        }
    }

    private void Save()
    {
        lock (_lock)
        {
            try
            {
                var data = new ApprovalData
                {
                    SchemaVersion = SchemaVersion,
                    FileOperations = _approvedFileOperations.ToArray(),
                    ShellKeys = _approvedShellKeys.ToArray(),
                    ResourceOperations = _approvedResourceOperations.ToArray(),
                    LastUpdated = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(data, _serializerOptions);

                File.WriteAllText(_storePath, json);
            }
            catch
            {
                // Fail silently to not interrupt user operations
            }
        }
    }

    private sealed class ApprovalData
    {
        public int SchemaVersion { get; set; }

        public string[]? FileOperations { get; set; }

        public string[]? ShellKeys { get; set; }

        public string[]? ResourceOperations { get; set; }

        public DateTime LastUpdated { get; set; }
    }
}
