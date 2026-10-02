namespace DotCraft.CodeMode;

public sealed record CodeModeLimits
{
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(300);
    public TimeSpan MaxTimeout { get; init; } = TimeSpan.FromHours(1);
    public int DefaultMaxOutputTokens { get; init; } = 10_000;
    public int MaxOutputTokens { get; init; } = 100_000;
    public long MaxCellMemoryBytes { get; init; } = 256L * 1024 * 1024;
    public int MaxStatements { get; init; } = 50_000_000;
    public int MaxRecursionDepth { get; init; } = 256;
    public TimeSpan EngineTimeout { get; init; } = TimeSpan.FromHours(6);
    public int MaxNestedCalls { get; init; } = 2048;
    public int MaxConcurrentNestedCalls { get; init; } = 64;
    public int MaxNestedResultBytes { get; init; } = 1024 * 1024;
    public int MaxCellOutputBytes { get; init; } = 8 * 1024 * 1024;
    public long MaxWorkerRssBytes { get; init; } = 1024L * 1024 * 1024;
    public int MaxFrameBytes { get; init; } = 16 * 1024 * 1024;
    public int MaxStderrBytes { get; init; } = 1024 * 1024;
    public TimeSpan WorkerStartTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan CancelGrace { get; init; } = TimeSpan.FromSeconds(5);
}
