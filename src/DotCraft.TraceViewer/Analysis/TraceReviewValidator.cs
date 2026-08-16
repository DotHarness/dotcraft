namespace DotCraft.TraceViewer.Analysis;

internal static class TraceReviewValidator
{
    private static readonly HashSet<string> Dimensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Reliability", "Latency", "Tool behavior", "Token efficiency", "Prompt cache"
    };

    public static IReadOnlyList<TraceFinding> ValidateAndOrder(
        IReadOnlyList<TraceFinding> findings,
        TraceSnapshot snapshot)
    {
        var order = snapshot.Events.Select((item, index) => (item.Id, index))
            .ToDictionary(item => item.Id, item => item.index, StringComparer.Ordinal);
        foreach (var finding in findings)
        {
            if (string.IsNullOrWhiteSpace(finding.Id) || string.IsNullOrWhiteSpace(finding.Title))
                throw new InvalidDataException("Every finding requires an id and title.");
            if (!Dimensions.Contains(finding.Dimension))
                throw new InvalidDataException($"Unsupported finding dimension '{finding.Dimension}'.");
            if (finding.Evidence is null || finding.Evidence.Count == 0)
                throw new InvalidDataException($"Finding '{finding.Id}' requires evidence.");
            foreach (var evidence in finding.Evidence)
                ValidateEvidence(evidence, order);
        }

        return findings
            .OrderBy(item => item.Severity)
            .ThenBy(item => item.Evidence.Min(evidence => order[evidence.EventId]))
            .ToArray();
    }

    private static void ValidateEvidence(
        TraceEvidenceReference evidence,
        Dictionary<string, int> order)
    {
        if (!order.TryGetValue(evidence.EventId, out var startIndex))
            throw new InvalidDataException($"Evidence event '{evidence.EventId}' is not in this trace snapshot.");
        if (string.IsNullOrWhiteSpace(evidence.Label))
            throw new InvalidDataException("Evidence label is required.");
        if (evidence.EndEventId is not { Length: > 0 } endId)
            return;
        if (!order.TryGetValue(endId, out var endIndex))
            throw new InvalidDataException($"Evidence end event '{endId}' is not in this trace snapshot.");
        if (endIndex < startIndex)
            throw new InvalidDataException("Evidence range end must not precede its start.");
    }
}
