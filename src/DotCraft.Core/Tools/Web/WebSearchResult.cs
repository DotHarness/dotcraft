namespace DotCraft.Tools.Web;

internal sealed record WebSearchResult(
    string Title,
    string Url,
    string Snippet,
    string? Author = null,
    string? PublishedDate = null);
