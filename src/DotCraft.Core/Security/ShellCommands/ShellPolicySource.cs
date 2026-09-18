namespace DotCraft.Security.ShellCommands;

public sealed class ShellPolicySource
{
    private readonly IReadOnlyList<ShellPrefixRule> _configuredRules;

    private readonly LearnedShellRuleStore? _learned;

    private readonly Lock _lock = new();

    private ShellPolicy _current;

    private DateTime _learnedStamp;

    public ShellPolicySource(IReadOnlyList<ShellPrefixRule> configuredRules, LearnedShellRuleStore? learned)
    {
        _configuredRules = configuredRules;
        _learned = learned;
        _current = Build();
        _learnedStamp = learned?.LastWriteTimeUtc ?? DateTime.MinValue;
    }

    public static ShellPolicySource Empty { get; } = new([], null);

    public static ShellPolicySource ForWorkspace(IReadOnlyList<ShellPrefixRule> configuredRules, string dataPath) =>
        new(configuredRules, new LearnedShellRuleStore(Path.Combine(dataPath, "security", "shell-rules.json")));

    public ShellPolicy Current
    {
        get
        {
            if (_learned is null)
                return _current;

            lock (_lock)
            {
                var stamp = _learned.LastWriteTimeUtc;
                if (stamp != _learnedStamp)
                {
                    _current = Build();
                    _learnedStamp = stamp;
                }

                return _current;
            }
        }
    }

    private ShellPolicy Build() =>
        new(_configuredRules.Concat(_learned?.Load() ?? []));
}
