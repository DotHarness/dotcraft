using DotCraft.Satellite.Services;
using Microsoft.UI.Xaml;
using Xunit;

namespace DotCraft.Satellite.Tests;

public sealed class ApplicationLifetimeTests
{
    [Fact]
    public void NormalTrayMode_RequiresExplicitShutdown()
    {
        var options = new StartupOptions(null, Background: true, Uninstall: false);

        Assert.Equal(DispatcherShutdownMode.OnExplicitShutdown, App.ShutdownModeFor(options));
    }

    [Theory]
    [InlineData("standby", null)]
    [InlineData(null, "default")]
    public void PreviewMode_ShutsDownWithItsLastWindow(string? island, string? consent)
    {
        var options = new StartupOptions(
            null,
            Background: false,
            Uninstall: false,
            PreviewIsland: island,
            PreviewConsent: consent);

        Assert.Equal(DispatcherShutdownMode.OnLastWindowClose, App.ShutdownModeFor(options));
    }
}
