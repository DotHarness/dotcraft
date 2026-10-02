using Jint;

namespace DotCraft.Scripting;

public sealed record ScriptEngineLimits(
    long MaxMemoryBytes,
    int MaxStatements,
    int MaxRecursionDepth,
    TimeSpan Timeout,
    TimeSpan? RegexTimeout = null);

public static class ScriptEngineFactory
{
    public static Engine Create(ScriptEngineLimits limits, CancellationToken cancellationToken) =>
        new(options =>
        {
            options.Strict();
            options.DisableStringCompilation();
            options.LimitMemory(limits.MaxMemoryBytes);
            options.MaxStatements(limits.MaxStatements);
            options.TimeoutInterval(limits.Timeout);
            options.LimitRecursion(limits.MaxRecursionDepth);
            options.CancellationToken(cancellationToken);
            options.ExperimentalFeatures = ExperimentalFeature.TaskInterop;
            options.Constraints.PromiseTimeout = limits.Timeout;
            options.Constraints.RegexTimeout = limits.RegexTimeout ?? limits.Timeout;
        });
}
