namespace DotCraft.Agents;

internal sealed class ModelStreamAttemptRuntimeContext(int attemptNumber)
{
    public int AttemptNumber { get; } = attemptNumber;

    public int? StatusCode { get; private set; }

    public string? RequestId { get; private set; }

    public string? SessionIdHash { get; private set; }

    public string? ThreadIdHash { get; private set; }

    public string? PromptCacheKeyHash { get; private set; }

    public void CapturePromptCacheKeyHash(string? promptCacheKeyHash)
    {
        PromptCacheKeyHash = NormalizeHash(promptCacheKeyHash);
    }

    public void CaptureOpenAIResponse(
        int? statusCode,
        string? requestId,
        string? sessionIdHash,
        string? threadIdHash,
        string? promptCacheKeyHash)
    {
        StatusCode = statusCode;
        RequestId = NormalizeOptional(requestId);
        SessionIdHash = NormalizeOptional(sessionIdHash);
        ThreadIdHash = NormalizeOptional(threadIdHash);
        PromptCacheKeyHash ??= NormalizeHash(promptCacheKeyHash);
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? NormalizeHash(string? value)
    {
        var normalized = NormalizeOptional(value);
        return normalized?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true
            ? normalized["sha256:".Length..]
            : normalized;
    }
}

internal static class ModelStreamAttemptRuntimeScope
{
    private static readonly AsyncLocal<ModelStreamAttemptRuntimeContext?> CurrentContext = new();

    public static ModelStreamAttemptRuntimeContext? Current => CurrentContext.Value;

    public static IDisposable Begin(int attemptNumber)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = new ModelStreamAttemptRuntimeContext(attemptNumber);
        return new Scope(previous);
    }

    private sealed class Scope(ModelStreamAttemptRuntimeContext? previous) : IDisposable
    {
        public void Dispose() => CurrentContext.Value = previous;
    }
}
