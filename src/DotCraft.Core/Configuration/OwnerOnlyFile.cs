using System.Security.AccessControl;

namespace DotCraft.Configuration;

/// <summary>Creates new files that only the current user can open.</summary>
public static class OwnerOnlyFile
{
    public static FileStream Create(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            });
        }
        return CreateOnWindows(path);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static FileStream CreateOnWindows(string path)
    {
        var owner = System.Security.Principal.WindowsIdentity.GetCurrent().User
            ?? throw new UnauthorizedAccessException("The current user cannot be determined.");
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(owner);
        security.AddAccessRule(new FileSystemAccessRule(
            owner, FileSystemRights.FullControl, AccessControlType.Allow));
        return new FileInfo(path).Create(
            FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, security);
    }
}
