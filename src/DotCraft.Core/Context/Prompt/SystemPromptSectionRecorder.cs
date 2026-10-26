namespace DotCraft.Context;

internal readonly record struct PromptSectionTokens(string Name, int Tokens);

internal sealed class SystemPromptSectionRecorder : IDisposable
{
    private static readonly AsyncLocal<SystemPromptSectionRecorder?> Active = new();

    private readonly string _threadId;
    private readonly SystemPromptSectionRecorder? _previous;
    private IReadOnlyList<PromptSectionTokens>? _sections;

    private SystemPromptSectionRecorder(string threadId)
    {
        _threadId = threadId;
        _previous = Active.Value;
    }

    public IReadOnlyList<PromptSectionTokens>? Sections => Volatile.Read(ref _sections);

    public string? CraftPath { get; private set; }

    public static SystemPromptSectionRecorder Begin(string threadId)
    {
        var recorder = new SystemPromptSectionRecorder(threadId);
        Active.Value = recorder;
        return recorder;
    }

    internal static SystemPromptSectionRecorder? For(string? threadId) =>
        Active.Value is { } recorder && string.Equals(recorder._threadId, threadId, StringComparison.Ordinal)
            ? recorder
            : null;

    internal void Record(IReadOnlyList<PromptSectionTokens>? sections, string craftPath)
    {
        Volatile.Write(ref _sections, sections);
        CraftPath = craftPath;
    }

    public void Dispose() => Active.Value = _previous;
}
