using DotCraft.TraceViewer.Analysis;
using System.Text.RegularExpressions;

namespace DotCraft.TraceViewer.ViewModels;

public sealed class ReviewFindingItem
{
    internal ReviewFindingItem(TraceFinding finding)
    {
        Finding = finding;
        Evidence = finding.Evidence.Select(item => new ReviewEvidenceItem(item)).ToArray();
    }

    internal TraceFinding Finding { get; }
    public string Id => Finding.Id;
    public string Severity => Finding.Severity.ToString();
    public bool IsMajor => Finding.Severity == TraceFindingSeverity.Major;
    public bool IsMinor => Finding.Severity == TraceFindingSeverity.Minor;
    public bool IsSuggestion => Finding.Severity == TraceFindingSeverity.Suggestion;
    public string Dimension => Finding.Dimension;
    public string Title => Finding.Title;
    public string Body => Finding.Body;
    public string Impact => Finding.Impact;
    public string Recommendation => Finding.Recommendation;
    public string Basis => Finding.Basis.ToString();
    public IReadOnlyList<ReviewEvidenceItem> Evidence { get; }
}

public sealed class ReviewEvidenceItem
{
    internal ReviewEvidenceItem(TraceEvidenceReference evidence) => Evidence = evidence;
    internal TraceEvidenceReference Evidence { get; }
    public string EventId => Evidence.EventId;
    public string Label => Evidence.Label;
    public string RangeLabel => Evidence.EndEventId is null ? EventId : $"{EventId} → {Evidence.EndEventId}";
}

public sealed class ReviewMessageItem
{
    private static readonly Regex EvidenceLinkPattern = new(
        @"trace://event/(?<id>[A-Za-z0-9_-]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal ReviewMessageItem(TraceConversationMessage message, IReadOnlySet<string>? validEventIds = null)
    {
        Role = message.Role;
        Content = message.Content;
        Timestamp = message.Timestamp.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture);
        Evidence = EvidenceLinkPattern.Matches(message.Content)
            .Select(match => match.Groups["id"].Value)
            .Where(id => validEventIds?.Contains(id) == true)
            .Distinct(StringComparer.Ordinal)
            .Select(id => new ReviewEvidenceItem(new TraceEvidenceReference(id, null, "Show cited event")))
            .ToArray();
    }

    public string Role { get; }
    public string Content { get; }
    public string Timestamp { get; }
    public IReadOnlyList<ReviewEvidenceItem> Evidence { get; }
}
