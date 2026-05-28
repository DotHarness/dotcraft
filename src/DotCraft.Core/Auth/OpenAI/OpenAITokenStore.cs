using System.Text.Json;

namespace DotCraft.Auth.OpenAI;

/// <summary>
/// Persists OpenAI OAuth tokens to <c>~/.craft/auth.json</c> with file permissions limited to the
/// current user (Unix mode 0600; Windows ACL granting only the owning SID).
/// </summary>
public sealed class OpenAITokenStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _filePath;
    private readonly object _gate = new();

    public OpenAITokenStore(string? globalCraftDir = null)
    {
        var dir = string.IsNullOrWhiteSpace(globalCraftDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".craft")
            : globalCraftDir;
        _filePath = Path.Combine(dir, "auth.json");
    }

    /// <summary>Absolute path to the auth.json file.</summary>
    public string FilePath => _filePath;

    /// <summary>Reads the auth.json from disk, returning null when the file is absent.</summary>
    public AuthDotJson? Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_filePath))
                return null;
            try
            {
                var json = File.ReadAllText(_filePath);
                if (string.IsNullOrWhiteSpace(json))
                    return null;
                return JsonSerializer.Deserialize<AuthDotJson>(json, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>Writes auth.json atomically and tightens file permissions to the current user.</summary>
    public void Save(AuthDotJson auth)
    {
        ArgumentNullException.ThrowIfNull(auth);

        lock (_gate)
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var tempPath = _filePath + ".tmp";
            var json = JsonSerializer.Serialize(auth, JsonOptions);
            File.WriteAllText(tempPath, json);
            TightenPermissions(tempPath);
            File.Move(tempPath, _filePath, overwrite: true);
            TightenPermissions(_filePath);
        }
    }

    /// <summary>Deletes auth.json if it exists.</summary>
    public void Delete()
    {
        lock (_gate)
        {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
        }
    }

    private static void TightenPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            TightenWindowsAcl(path);
        }
        else
        {
            try
            {
                File.SetUnixFileMode(path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (PlatformNotSupportedException) { }
            catch (IOException) { }
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void TightenWindowsAcl(string path)
    {
        try
        {
            var info = new FileInfo(path);
            var security = info.GetAccessControl();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent().User;
            if (currentUser is null)
                return;

            foreach (System.Security.AccessControl.FileSystemAccessRule rule in
                     security.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier)))
            {
                security.RemoveAccessRule(rule);
            }

            security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                currentUser,
                System.Security.AccessControl.FileSystemRights.FullControl,
                System.Security.AccessControl.AccessControlType.Allow));

            info.SetAccessControl(security);
        }
        catch (UnauthorizedAccessException) { }
        catch (PlatformNotSupportedException) { }
        catch (IOException) { }
    }
}
