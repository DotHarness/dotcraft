using DotCraft.Sessions;

namespace DotCraft.SessionImport;

internal sealed class ImportTurnBuilder(DateTimeOffset fallbackTime)
{
    private readonly List<ImportedTurnInput> _turns = [];
    private readonly List<string> _agentTexts = [];
    private string? _userText;
    private DateTimeOffset _startedAt;
    private DateTimeOffset _completedAt;
    private DateTimeOffset? _lastSeen;

    public void AddUser(string text, DateTimeOffset? timestamp)
    {
        Flush();
        if (string.IsNullOrWhiteSpace(text))
            return;
        var at = Resolve(timestamp);
        _userText = text;
        _startedAt = at;
        _completedAt = at;
    }

    public void AddAgent(string text, DateTimeOffset? timestamp)
    {
        var at = Resolve(timestamp);
        if (_userText is null || string.IsNullOrWhiteSpace(text))
            return;
        _agentTexts.Add(text);
        if (at > _completedAt)
            _completedAt = at;
    }

    public IReadOnlyList<ImportedTurnInput> Build()
    {
        Flush();
        return _turns.ToArray();
    }

    private DateTimeOffset Resolve(DateTimeOffset? timestamp)
    {
        if (timestamp is { } value)
            _lastSeen = value;
        return _lastSeen ?? fallbackTime;
    }

    private void Flush()
    {
        if (_userText is not null)
        {
            _turns.Add(new ImportedTurnInput
            {
                UserText = _userText,
                AgentTexts = _agentTexts.ToArray(),
                StartedAt = _startedAt,
                CompletedAt = _completedAt
            });
        }

        _userText = null;
        _agentTexts.Clear();
    }
}
