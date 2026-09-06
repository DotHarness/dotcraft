using DotCraft.Satellite.Services;
using Xunit;

namespace DotCraft.Satellite.Tests;

public sealed class ShellIntegrationTests
{
    private const string OurExe = @"C:\Users\ann\AppData\Local\DotCraftSatellite\current\dotcraft-satellite.exe";
    private const string StalePath =
        @"""C:\Users\ann\AppData\Local\DotCraftSatellite\app-0.6.1\dotcraft-satellite.exe"" --url ""%1""";
    private const string DesktopCommand = @"""C:\Program Files\DotCraft\DotCraft.exe"" ""%1""";

    [Fact]
    public void Decide_ClassifiesTheRegisteredHandler()
    {
        Assert.Equal(ProtocolOwnershipAction.Write, ProtocolOwnershipPolicy.Decide(null, OurExe));
        Assert.Equal(ProtocolOwnershipAction.Write, ProtocolOwnershipPolicy.Decide(string.Empty, OurExe));
        Assert.Equal(
            ProtocolOwnershipAction.Leave,
            ProtocolOwnershipPolicy.Decide($"\"{OurExe}\" --url \"%1\"", OurExe));
        Assert.Equal(ProtocolOwnershipAction.Rewrite, ProtocolOwnershipPolicy.Decide(StalePath, OurExe));
        Assert.Equal(ProtocolOwnershipAction.Delegate, ProtocolOwnershipPolicy.Decide(DesktopCommand, OurExe));
    }

    [Fact]
    public void Install_ClaimsTheProtocol_PublishesItself_AndSupersedesTheCliAutostart()
    {
        var registry = new FakeRegistry();
        registry.SetValue(ShellIntegration.RunKey, ShellIntegration.CliRunValue, @"""dotcraft.exe"" tool-host serve");

        new ShellIntegration(registry, OurExe).Install();

        Assert.Equal(
            $"\"{OurExe}\" --url \"%1\"",
            registry.GetValue(ShellIntegration.CommandKey, null));
        Assert.Equal($"\"{OurExe}\",0", registry.GetValue(ShellIntegration.IconKey, null));
        Assert.Equal(string.Empty, registry.GetValue(ShellIntegration.ClassesKey, "URL Protocol"));
        Assert.Equal(OurExe, registry.GetValue(ShellIntegration.PublicationKey, ShellIntegration.ExecutablePathValue));
        Assert.Equal(
            ShellIntegration.OwnerSatellite,
            registry.GetValue(ShellIntegration.PublicationKey, ShellIntegration.ProtocolOwnerValue));
        Assert.Equal(
            $"\"{OurExe}\" --background",
            registry.GetValue(ShellIntegration.RunKey, ShellIntegration.RunValue));
        Assert.Null(registry.GetValue(ShellIntegration.RunKey, ShellIntegration.CliRunValue));
    }

    [Fact]
    public void Install_WhenAnotherProgramOwnsTheProtocol_OnlyPublishesItself()
    {
        var registry = new FakeRegistry();
        registry.SetValue(ShellIntegration.CommandKey, null, DesktopCommand);

        new ShellIntegration(registry, OurExe).Install();

        Assert.Equal(DesktopCommand, registry.GetValue(ShellIntegration.CommandKey, null));
        Assert.Equal(
            ShellIntegration.OwnerDelegated,
            registry.GetValue(ShellIntegration.PublicationKey, ShellIntegration.ProtocolOwnerValue));
        Assert.Equal(OurExe, registry.GetValue(ShellIntegration.PublicationKey, ShellIntegration.ExecutablePathValue));
    }

    [Fact]
    public void Install_RepairsAStalePathToItself()
    {
        var registry = new FakeRegistry();
        registry.SetValue(ShellIntegration.CommandKey, null, StalePath);

        new ShellIntegration(registry, OurExe).Install();

        Assert.Equal($"\"{OurExe}\" --url \"%1\"", registry.GetValue(ShellIntegration.CommandKey, null));
    }

    [Fact]
    public void RemoveAll_IsIdempotentAndClearsWhatItOwns()
    {
        var registry = new FakeRegistry();
        var integration = new ShellIntegration(registry, OurExe);
        integration.Install();

        integration.RemoveAll();
        integration.RemoveAll();

        Assert.Null(registry.GetValue(ShellIntegration.CommandKey, null));
        Assert.Null(registry.GetValue(ShellIntegration.ClassesKey, "URL Protocol"));
        Assert.Null(registry.GetValue(ShellIntegration.PublicationKey, ShellIntegration.ExecutablePathValue));
        Assert.Null(registry.GetValue(ShellIntegration.RunKey, ShellIntegration.RunValue));
    }

    [Fact]
    public void RemoveAll_NeverRemovesAnotherProgramsProtocolHandler()
    {
        var registry = new FakeRegistry();
        registry.SetValue(ShellIntegration.CommandKey, null, DesktopCommand);
        var integration = new ShellIntegration(registry, OurExe);
        integration.Install();

        integration.RemoveAll();

        Assert.Equal(DesktopCommand, registry.GetValue(ShellIntegration.CommandKey, null));
        Assert.Null(registry.GetValue(ShellIntegration.PublicationKey, ShellIntegration.ExecutablePathValue));
        Assert.Null(registry.GetValue(ShellIntegration.RunKey, ShellIntegration.RunValue));
    }

    private sealed class FakeRegistry : IRegistryStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

        public string? GetValue(string keyPath, string? name) =>
            _values.GetValueOrDefault(Key(keyPath, name));

        public void SetValue(string keyPath, string? name, string value) =>
            _values[Key(keyPath, name)] = value;

        public void DeleteValue(string keyPath, string name) => _values.Remove(Key(keyPath, name));

        public void DeleteTree(string keyPath)
        {
            foreach (var key in _values.Keys
                         .Where(key => key.StartsWith(keyPath + @"\", StringComparison.OrdinalIgnoreCase))
                         .ToArray())
            {
                _values.Remove(key);
            }
        }

        private static string Key(string keyPath, string? name) => keyPath + @"\\" + (name ?? string.Empty);
    }
}
