using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DotCraft.Security;

/// <summary>
/// Stores and manages persistent approval records for file and shell operations.
/// </summary>
public sealed class ApprovalStore
{
    private readonly string _storePath;
    
    private readonly HashSet<string> _approvedFileOperations = [];
    
    private readonly HashSet<string> _approvedShellCommands = [];
    
    private readonly Lock _lock = new();

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

    /// <summary>
    /// Check if a shell command is already approved.
    /// </summary>
    public bool IsShellCommandApproved(string command, string? workingDir)
    {
        lock (_lock)
        {
            var key = ComputeShellCommandKey(command, workingDir);
            return _approvedShellCommands.Contains(key);
        }
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

    /// <summary>
    /// Record an approved shell command.
    /// </summary>
    public void RecordShellCommand(string command, string? workingDir)
    {
        lock (_lock)
        {
            var key = ComputeShellCommandKey(command, workingDir);
            if (_approvedShellCommands.Add(key))
            {
                Save();
            }
        }
    }

    /// <summary>
    /// Clear all approval records.
    /// </summary>
    public void ClearAll()
    {
        lock (_lock)
        {
            _approvedFileOperations.Clear();
            _approvedShellCommands.Clear();
            Save();
        }
    }

    /// <summary>
    /// Get count of approved operations.
    /// </summary>
    public (int fileOps, int shellCmds) GetApprovalCounts()
    {
        lock (_lock)
        {
            return (_approvedFileOperations.Count, _approvedShellCommands.Count);
        }
    }

    private static string ComputeFileOperationKey(string operation, string path)
    {
        // Normalize path and create a stable key
        var normalizedPath = Path.GetFullPath(path).ToLowerInvariant();
        var input = $"{operation.ToLowerInvariant()}:{normalizedPath}";
        return ComputeHash(input);
    }

    private static string ComputeShellCommandKey(string command, string? workingDir)
    {
        // Create a stable key based on command structure
        var normalizedCommand = command.Trim();
        var normalizedDir = string.IsNullOrWhiteSpace(workingDir) 
            ? "" 
            : Path.GetFullPath(workingDir).ToLowerInvariant();
        var input = $"{normalizedCommand}:{normalizedDir}";
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
                    _approvedShellCommands.UnionWith(data.ShellCommands ?? Array.Empty<string>());
                }
            }
            catch
            {
                // If file is corrupted, start fresh
                _approvedFileOperations.Clear();
                _approvedShellCommands.Clear();
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
                    FileOperations = _approvedFileOperations.ToArray(),
                    ShellCommands = _approvedShellCommands.ToArray(),
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
        public string[]? FileOperations { get; set; }
        
        public string[]? ShellCommands { get; set; }
        
        public DateTime LastUpdated { get; set; }
    }
}
