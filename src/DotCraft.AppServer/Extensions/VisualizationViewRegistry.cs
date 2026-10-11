using System.Collections.Concurrent;

namespace DotCraft.AppServer;

internal sealed class VisualizationViewRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, VisualizationViewState> _views = new(StringComparer.Ordinal);

    public VisualizationViewState Add(VisualizationViewState state)
    {
        if (!_views.TryAdd(state.Handle, state))
            throw new InvalidOperationException("The visualization handle already exists.");
        return state;
    }

    public VisualizationViewState Get(string handle) =>
        !string.IsNullOrWhiteSpace(handle) && _views.TryGetValue(handle, out var state)
            ? state
            : throw VisualizationViewErrors.Create("stale_view", "The visualization view is no longer available.");

    public bool Close(string handle) => _views.TryRemove(handle, out _);
    public void Dispose() => _views.Clear();
}

internal sealed class VisualizationViewState
{
    public required string Handle { get; init; }
    public required string ThreadId { get; init; }
    public required string TurnId { get; init; }
    public required string ItemId { get; init; }
    public required string Path { get; init; }
}

internal static class VisualizationViewErrors
{
    public static AppServerException Create(string code, string fallbackText) =>
        new(AppServerErrors.InvalidParamsCode, fallbackText, new AppServerErrorData
        {
            Code = code,
            MessageKey = $"errors.visualization.{code}",
            FallbackText = fallbackText
        });
}
