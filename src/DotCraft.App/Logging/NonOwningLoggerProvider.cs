using Microsoft.Extensions.Logging;

namespace DotCraft.Logging;

/// <summary>
/// Forwards framework categories to an application-owned logger factory without
/// transferring ownership to the nested host.
/// </summary>
internal sealed class NonOwningLoggerProvider(ILoggerFactory loggerFactory) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => loggerFactory.CreateLogger(categoryName);

    public void Dispose()
    {
        // The application composition root owns loggerFactory.
    }
}
