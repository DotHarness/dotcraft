using DotCraft.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DotCraft.Tests.Logging;

public sealed class NonOwningLoggerProviderTests
{
    [Fact]
    public void Dispose_DoesNotDisposeApplicationLoggerFactory()
    {
        var factory = new RecordingLoggerFactory();
        var provider = new NonOwningLoggerProvider(factory);

        Assert.Same(NullLogger.Instance, provider.CreateLogger("Microsoft.AspNetCore.Hosting"));
        provider.Dispose();

        Assert.Equal("Microsoft.AspNetCore.Hosting", factory.LastCategory);
        Assert.False(factory.Disposed);
    }

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        public string? LastCategory { get; private set; }

        public bool Disposed { get; private set; }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName)
        {
            LastCategory = categoryName;
            return NullLogger.Instance;
        }

        public void Dispose() => Disposed = true;
    }
}
