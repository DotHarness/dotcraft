namespace DotCraft.Tracing;

internal static class PromptCacheRequestShapeTraceScope
{
    private static readonly AsyncLocal<int?> RequestIndexLocal = new();

    public static int? RequestIndex => RequestIndexLocal.Value;

    public static IDisposable UseRequestIndex(int requestIndex)
    {
        var previous = RequestIndexLocal.Value;
        RequestIndexLocal.Value = requestIndex;
        return new RestoreRequestIndexScope(previous);
    }

    private sealed class RestoreRequestIndexScope(int? previous) : IDisposable
    {
        public void Dispose() => RequestIndexLocal.Value = previous;
    }
}
