namespace DotCraft.Configuration;

/// <summary>
/// Configures the timeout range used by the session-backed <c>WaitAgent</c> tool.
/// </summary>
public sealed record SubAgentWaitAgentTimeoutOptions(
    int MinTimeoutMs,
    int DefaultTimeoutMs,
    int MaxTimeoutMs)
{
    public const int HardMinTimeoutMs = 0;
    public const int HardMaxTimeoutMs = 3_600_000;
    public const int BuiltInMinTimeoutMs = 15_000;
    public const int BuiltInDefaultTimeoutMs = 60_000;
    public const int BuiltInMaxTimeoutMs = HardMaxTimeoutMs;

    public static SubAgentWaitAgentTimeoutOptions Defaults { get; } = new(
        BuiltInMinTimeoutMs,
        BuiltInDefaultTimeoutMs,
        BuiltInMaxTimeoutMs);

    public static SubAgentWaitAgentTimeoutOptions FromConfig(AppConfig.SubAgentConfig? config)
    {
        if (config == null)
            return Defaults;

        var options = new SubAgentWaitAgentTimeoutOptions(
            config.MinWaitTimeoutMs,
            config.DefaultWaitTimeoutMs,
            config.MaxWaitTimeoutMs);
        var errors = Validate(options).ToArray();
        if (errors.Length > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        return options;
    }

    public static IReadOnlyList<string> Validate(AppConfig.SubAgentConfig? config) =>
        config == null
            ? []
            : Validate(new SubAgentWaitAgentTimeoutOptions(
                config.MinWaitTimeoutMs,
                config.DefaultWaitTimeoutMs,
                config.MaxWaitTimeoutMs));

    public static IReadOnlyList<string> Validate(SubAgentWaitAgentTimeoutOptions options)
    {
        var errors = new List<string>();
        ValidateValue("SubAgent.MinWaitTimeoutMs", options.MinTimeoutMs, errors);
        ValidateValue("SubAgent.DefaultWaitTimeoutMs", options.DefaultTimeoutMs, errors);
        ValidateValue("SubAgent.MaxWaitTimeoutMs", options.MaxTimeoutMs, errors);

        if (options.MinTimeoutMs > options.MaxTimeoutMs)
            errors.Add("SubAgent.MinWaitTimeoutMs must be at most SubAgent.MaxWaitTimeoutMs.");
        if (options.DefaultTimeoutMs < options.MinTimeoutMs)
            errors.Add("SubAgent.DefaultWaitTimeoutMs must be at least SubAgent.MinWaitTimeoutMs.");
        if (options.DefaultTimeoutMs > options.MaxTimeoutMs)
            errors.Add("SubAgent.DefaultWaitTimeoutMs must be at most SubAgent.MaxWaitTimeoutMs.");

        return errors;
    }

    public int ResolveTimeoutMs(int? requestedTimeoutMs)
    {
        var timeoutMs = requestedTimeoutMs ?? DefaultTimeoutMs;
        if (timeoutMs < MinTimeoutMs)
            throw new ArgumentOutOfRangeException("timeoutMs", $"timeoutMs must be at least {MinTimeoutMs}.");
        if (timeoutMs > MaxTimeoutMs)
            throw new ArgumentOutOfRangeException("timeoutMs", $"timeoutMs must be at most {MaxTimeoutMs}.");

        return timeoutMs;
    }

    private static void ValidateValue(string label, int value, List<string> errors)
    {
        if (value < HardMinTimeoutMs)
            errors.Add($"{label} must be at least {HardMinTimeoutMs}.");
        if (value > HardMaxTimeoutMs)
            errors.Add($"{label} must be at most {HardMaxTimeoutMs}.");
    }
}
