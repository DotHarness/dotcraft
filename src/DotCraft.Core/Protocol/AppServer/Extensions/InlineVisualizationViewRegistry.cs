using System.Collections.Concurrent;

namespace DotCraft.AppServer;

internal sealed class InlineVisualizationViewRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, InlineVisualizationViewState> _views = new(StringComparer.Ordinal);

    public InlineVisualizationViewState Add(InlineVisualizationViewState state)
    {
        if (!_views.TryAdd(state.Handle, state))
            throw new InvalidOperationException("The inline visualization handle already exists.");
        return state;
    }

    public InlineVisualizationViewState Get(string handle) =>
        !string.IsNullOrWhiteSpace(handle) && _views.TryGetValue(handle, out var state)
            ? state
            : throw InlineVisualizationViewErrors.Create("stale_view", "The inline visualization view is no longer available.");

    public bool Close(string handle) => _views.TryRemove(handle, out _);
    public void Dispose() => _views.Clear();
}

internal sealed class InlineVisualizationViewState
{
    public required string Handle { get; init; }
    public required string ThreadId { get; init; }
    public required string TurnId { get; init; }
    public required string ItemId { get; init; }
    public required string File { get; init; }
}

internal static class InlineVisualizationViewErrors
{
    public static AppServerException Create(string code, string fallbackText) =>
        new(AppServerErrors.InvalidParamsCode, fallbackText, new AppServerErrorData
        {
            Code = code,
            MessageKey = $"errors.inlineVisualization.{code}",
            FallbackText = fallbackText
        });
}
