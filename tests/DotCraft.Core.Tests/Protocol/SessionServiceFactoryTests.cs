using DotCraft.Abstractions;
using DotCraft.Protocol;
using DotCraft.Tests.Sessions.Protocol.AppServer;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceFactoryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        "SessionServiceFactoryTests_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void BindSessionServiceConsumers_BindsEveryRegisteredConsumer()
    {
        Directory.CreateDirectory(_tempDir);
        var sessionService = new TestableSessionService(new ThreadStore(_tempDir));
        var first = new RecordingConsumer();
        var second = new RecordingConsumer();
        var services = new ServiceCollection();
        services.AddSingleton<ISessionServiceConsumer>(first);
        services.AddSingleton<ISessionServiceConsumer>(second);
        using var provider = services.BuildServiceProvider();

        SessionServiceFactory.BindSessionServiceConsumers(provider, sessionService);

        Assert.Same(sessionService, first.SessionService);
        Assert.Same(sessionService, second.SessionService);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for test artifacts.
        }
    }

    private sealed class RecordingConsumer : ISessionServiceConsumer
    {
        public ISessionService? SessionService { get; private set; }

        public void SetSessionService(ISessionService service) => SessionService = service;
    }
}
