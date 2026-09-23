using System.Text.Json;

namespace DotCraft.Auth.OpenAI;

public sealed class OpenAITokenStore : IOpenAITokenStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string? _filePath;
    private readonly object _gate = new();

    public OpenAITokenStore(string? userDataPath = null)
    {
        _filePath = string.IsNullOrWhiteSpace(userDataPath)
            ? null
            : Path.Combine(userDataPath, "auth.json");
    }

    public string? FilePath => _filePath;

    public AuthDotJson? Load()
    {
        lock (_gate)
        {
            if (_filePath is null || !File.Exists(_filePath))
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

    public void Save(AuthDotJson auth)
    {
        ArgumentNullException.ThrowIfNull(auth);
        lock (_gate)
        {
            using var fileLock = AcquireFileLock();
            WriteTokens(auth);
        }
    }

    public void Delete()
    {
        lock (_gate)
        {
            using var fileLock = AcquireFileLock();
            File.Delete(_filePath!);
        }
    }

    public bool TryReplace(AuthDotJson expected, AuthDotJson? replacement)
    {
        lock (_gate)
        {
            using var fileLock = AcquireFileLock();
            if (!HasSameTokens(Load(), expected))
                return false;
            if (replacement is null)
                File.Delete(_filePath!);
            else
                WriteTokens(replacement);
            return true;
        }
    }

    private void WriteTokens(AuthDotJson auth)
    {
        var temporary = _filePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(auth, JsonOptions));
        TightenPermissions(temporary);
        File.Move(temporary, _filePath!, overwrite: true);
    }

    public static bool HasSameTokens(AuthDotJson? left, AuthDotJson? right) =>
        left?.Tokens?.AccessToken == right?.Tokens?.AccessToken
        && left?.Tokens?.RefreshToken == right?.Tokens?.RefreshToken
        && left?.Tokens?.IdToken == right?.Tokens?.IdToken;

    private FileStream AcquireFileLock()
    {
        if (_filePath is null)
            throw new InvalidOperationException("UserDataPath is required for OpenAI authentication persistence.");
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(_filePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (timeout.Elapsed < TimeSpan.FromSeconds(5))
            {
                Thread.Sleep(20);
            }
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
