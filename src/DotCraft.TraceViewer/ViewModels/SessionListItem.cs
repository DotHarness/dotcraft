using DotCraft.Tracing;
using System.Globalization;

namespace DotCraft.TraceViewer.ViewModels;

public sealed class SessionListItem
{
    private SessionListItem()
    {
    }

    public required string SessionKey { get; init; }

    public required string DisplayTitle { get; init; }

    public required string FullTitle { get; init; }

    public required string ActivitySummary { get; init; }

    public required string RelationshipLabel { get; init; }

    public required string LastActivity { get; init; }

    public required string TokenSummary { get; init; }

    public required string DetailedActivity { get; init; }

    public required string Metrics { get; init; }

    public required string SearchText { get; init; }

    internal static SessionListItem FromTrace(
        TraceSession session,
        TraceSessionRelationshipDescriptor? relationship)
    {
        var title = SessionTitleFormatter.Format(session.FirstUserRequest, session.StartedAt);
        var bindingKind = relationship?.BindingKind ?? "unbound";
        var relationshipLabel = bindingKind switch
        {
            "threadMain" => string.Empty,
            "threadChild" when !string.IsNullOrWhiteSpace(relationship?.ParentSessionKey) =>
                "Sub-agent",
            "threadChild" => "Sub-agent",
            _ => string.Empty,
        };
        var totalTokens = session.TotalInputTokens + session.TotalOutputTokens;
        var metrics = $"{session.RequestCount:N0} requests · {session.ToolCallCount:N0} tools · {CompactNumber(totalTokens)} tokens"
            + (session.ErrorCount > 0 ? $" · {session.ErrorCount:N0} errors" : string.Empty);

        return new SessionListItem
        {
            SessionKey = session.SessionKey,
            DisplayTitle = title.CompactText,
            FullTitle = title.FullText,
            ActivitySummary = $"{session.LastActivityAt.ToLocalTime():g} · {totalTokens:N0} tokens",
            RelationshipLabel = relationshipLabel,
            LastActivity = session.LastActivityAt.ToLocalTime().ToString("F", CultureInfo.CurrentCulture),
            TokenSummary = $"{session.TotalInputTokens:N0} input · {session.TotalOutputTokens:N0} output",
            DetailedActivity = $"{session.RequestCount:N0} requests · {session.ResponseCount:N0} responses · {session.ToolCallCount:N0} tools · {session.ErrorCount:N0} errors",
            Metrics = metrics,
            SearchText = string.Join('\n', title.FullText, session.SessionKey, relationshipLabel, metrics),
        };
    }

    private static string CompactNumber(long value) => value switch
    {
        >= 1_000_000 => $"{value / 1_000_000d:0.#}M",
        >= 1_000 => $"{value / 1_000d:0.#}K",
        _ => value.ToString("N0", CultureInfo.CurrentCulture),
    };
}
