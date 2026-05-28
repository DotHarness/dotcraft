namespace DotCraft.Abstractions;

/// <summary>
/// Interface for controlling renderer behavior during approval prompts.
/// </summary>
public interface IRenderControl
{
    /// <summary>
    /// Pause rendering, execute an action on the render thread, then resume.
    /// This ensures the action has exclusive console access without cross-thread
    /// live rendering conflicts with Spectre.Console.
    /// </summary>
    Task<T> ExecuteWhilePausedAsync<T>(Func<T> action, CancellationToken cancellationToken = default);
}
