using System.Runtime.CompilerServices;

namespace DotCraft.Agents;

/// <summary>Keeps one provider classification while a failure crosses retry layers.</summary>
public static class ProviderFailureCapture
{
    private static readonly ConditionalWeakTable<Exception, Lazy<ProviderFailure>> Captured = new();

    public static ProviderFailure ClassifyCaptured(this IProviderFailureClassifier classifier, Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is ProviderFailureException carried) return carried.Failure;
            if (Captured.TryGetValue(current, out var failure)) return failure.Value;
        }
        return Captured.GetValue(exception, value => new Lazy<ProviderFailure>(() => classifier.Classify(value))).Value;
    }

    public static async Task WaitForRetryAsync(ProviderFailure failure, int attempt, CancellationToken ct)
    {
        var delay = failure.GetRetryDelay(attempt) ?? TimeSpan.Zero;
        var maximum = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
        while (delay > maximum)
        {
            await Task.Delay(maximum, ct).ConfigureAwait(false);
            delay = failure.RetryDeadline?.Remaining ?? delay - maximum;
        }
        await Task.Delay(delay, ct).ConfigureAwait(false);
    }
}
