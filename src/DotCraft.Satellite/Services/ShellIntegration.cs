namespace DotCraft.Satellite.Services;

internal interface IRegistryStore
{
    string? GetValue(string keyPath, string? name);

    void SetValue(string keyPath, string? name, string value);

    void DeleteValue(string keyPath, string name);

    void DeleteTree(string keyPath);
}

internal interface IShellIntegration
{
    void Install();

    void RemoveAll();
}

internal sealed class ShellIntegration(IRegistryStore registry, string executablePath) : IShellIntegration
{
    public const string ClassesKey = @"Software\Classes\dotcraft";
    public const string CommandKey = @"Software\Classes\dotcraft\shell\open\command";
    public const string IconKey = @"Software\Classes\dotcraft\DefaultIcon";
    public const string PublicationKey = @"Software\DotCraft\Satellite";
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValue = "DotCraftSatellite";
    public const string CliRunValue = "DotCraftRemoteToolHost";
    public const string ExecutablePathValue = "ExecutablePath";
    public const string ProtocolOwnerValue = "ProtocolOwner";
    public const string OwnerSatellite = "Satellite";
    public const string OwnerDelegated = "Delegated";

    public void Install()
    {
        var action = ProtocolOwnershipPolicy.Decide(registry.GetValue(CommandKey, null), executablePath);
        if (action is ProtocolOwnershipAction.Write or ProtocolOwnershipAction.Rewrite)
        {
            registry.SetValue(ClassesKey, null, "URL:DotCraft Satellite");
            registry.SetValue(ClassesKey, "URL Protocol", string.Empty);
            registry.SetValue(IconKey, null, $"\"{executablePath}\",0");
            registry.SetValue(CommandKey, null, $"\"{executablePath}\" --url \"%1\"");
        }

        registry.SetValue(PublicationKey, ExecutablePathValue, executablePath);
        registry.SetValue(
            PublicationKey,
            ProtocolOwnerValue,
            action == ProtocolOwnershipAction.Delegate ? OwnerDelegated : OwnerSatellite);

        // A machine runs at most one Remote Tool Host, and this one supersedes the CLI's.
        registry.DeleteValue(RunKey, CliRunValue);
        registry.SetValue(RunKey, RunValue, $"\"{executablePath}\" --background");
    }

    public void RemoveAll()
    {
        registry.DeleteValue(RunKey, RunValue);
        var owned = string.Equals(
                        registry.GetValue(PublicationKey, ProtocolOwnerValue),
                        OwnerSatellite,
                        StringComparison.Ordinal)
                    && ProtocolOwnershipPolicy.Decide(registry.GetValue(CommandKey, null), executablePath)
                        == ProtocolOwnershipAction.Leave;
        if (owned)
            registry.DeleteTree(ClassesKey);
        registry.DeleteTree(PublicationKey);
    }
}
