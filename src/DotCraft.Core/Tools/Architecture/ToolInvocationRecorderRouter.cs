using System.Text.Json.Nodes;

namespace DotCraft.Tools;

/// <summary>
/// Late-bound recorder used to break the construction cycle between the shared dispatcher and
/// Session Core. A host binds its Session recorder after constructing the service.
/// </summary>
public sealed class ToolInvocationRecorderRouter : IToolInvocationRecorder
{
    private IToolInvocationRecorder _target = new NoopToolInvocationRecorder();

    /// <summary>Replaces the active Session lifecycle projection target.</summary>
    public void Bind(IToolInvocationRecorder target) =>
        Volatile.Write(ref _target, target ?? throw new ArgumentNullException(nameof(target)));

    /// <inheritdoc />
    public ValueTask RecordStartedAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        JsonObject arguments,
        CancellationToken cancellationToken = default) =>
        Volatile.Read(ref _target).RecordStartedAsync(context, registration, arguments, cancellationToken);

    /// <inheritdoc />
    public ValueTask RecordTerminalAsync(
        ToolInvocationContext context,
        ToolRegistration registration,
        ToolExecutionResult result,
        TimeSpan duration,
        CancellationToken cancellationToken = default) =>
        Volatile.Read(ref _target).RecordTerminalAsync(
            context,
            registration,
            result,
            duration,
            cancellationToken);
}
