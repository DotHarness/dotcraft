namespace Oratorio.Server.Services;

/// <summary>
/// Hidden marker embedded in published inline review comment bodies so Oratorio can map a posted
/// source review thread back to its originating <c>ReviewDraftComment</c> (design spec §5.7, Step B).
/// Rendered as an HTML comment, so it stays invisible in GitHub/GitLab markdown.
/// </summary>
public static class ReviewFindingMarker
{
    private const string Prefix = "oratorio-finding:";

    public static string Build(string findingId) => $"<!-- {Prefix}{findingId} -->";

    public static string? Extract(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return null;
        }

        var index = body.IndexOf(Prefix, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var start = index + Prefix.Length;
        var end = body.IndexOf("-->", start, StringComparison.Ordinal);
        var value = end < 0 ? body[start..] : body[start..end];
        return value.Trim();
    }
}
