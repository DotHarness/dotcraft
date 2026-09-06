using System.Runtime.Versioning;
using Microsoft.Win32;

namespace DotCraft.Satellite.Services;

[SupportedOSPlatform("windows")]
internal sealed class WindowsRegistryStore : IRegistryStore
{
    public string? GetValue(string keyPath, string? name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(name ?? string.Empty) as string;
    }

    public void SetValue(string keyPath, string? name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true)
            ?? throw new InvalidOperationException($"Unable to open HKCU\\{keyPath}.");
        key.SetValue(name ?? string.Empty, value, RegistryValueKind.String);
    }

    public void DeleteValue(string keyPath, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public void DeleteTree(string keyPath) =>
        Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
}
