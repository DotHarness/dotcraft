using DotCraft.Tools.BackgroundTerminals;
using Microsoft.Extensions.Logging;

namespace DotCraft.RemoteTools;

internal sealed class RemoteToolDiagnosticLogger(Action<RemoteToolHostDiagnostic>? diagnostic) : ILogger<BackgroundTerminalService>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => diagnostic is not null;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        try
        {
            diagnostic?.Invoke(new(RemoteToolHostDiagnosticLevel.Warning, "toolInvocationFailed",
                formatter(state, exception), exception));
        }
        catch { /* Diagnostic listeners must not change tool outcomes. */ }
    }
}
