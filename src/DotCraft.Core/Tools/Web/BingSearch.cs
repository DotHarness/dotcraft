using System.Globalization;
using System.Text;
using System.Web;
using HtmlAgilityPack;

namespace DotCraft.Tools.Web;

internal static class BingSearch
{
    private const string Endpoint = "https://www.bing.com/search";

    public static async Task<List<WebSearchResult>> SearchAsync(
        HttpClient client,
        string query,
        int count,
        CancellationToken cancellationToken)
    {
        var language = CultureInfo.CurrentUICulture.Name is { Length: > 0 } name ? name : "en-US";
        var uri = new Uri($"{Endpoint}?q={Uri.EscapeDataString(query)}&count={count}&setlang={Uri.EscapeDataString(language)}");

        var (response, _) = await WebHttp.GetAsync(client, uri, request =>
        {
            request.Headers.TryAddWithoutValidation("Accept", "text/html");
            request.Headers.TryAddWithoutValidation("Accept-Language", $"{language},en;q=0.8");
        }, cancellationToken);

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new WebToolException($"Bing returned HTTP {(int)response.StatusCode}.");
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseResults(html, count);
        }
    }

    public static List<WebSearchResult> ParseResults(string html, int maxResults)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var results = new List<WebSearchResult>();
        var blocks = document.DocumentNode.Descendants("li")
            .Where(li => li.GetClasses().Contains("b_algo"));

        foreach (var block in blocks)
        {
            if (results.Count >= maxResults)
                break;

            var anchor = block.Descendants("h2").SelectMany(h2 => h2.Descendants("a")).FirstOrDefault();
            if (anchor is null)
                continue;

            var url = ResolveResultUrl(HtmlEntity.DeEntitize(anchor.GetAttributeValue("href", string.Empty)));
            var title = Normalize(anchor.InnerText);
            if (url is null || title.Length == 0)
                continue;

            var paragraph = block.Descendants("p").FirstOrDefault();
            var snippet = paragraph is null ? string.Empty : Normalize(paragraph.InnerText);
            results.Add(new WebSearchResult(title, url, snippet));
        }

        return results;
    }

    internal static string? ResolveResultUrl(string href)
    {
        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;

        var isBingHost = uri.Host.Equals("bing.com", StringComparison.OrdinalIgnoreCase)
                         || uri.Host.EndsWith(".bing.com", StringComparison.OrdinalIgnoreCase);
        if (!isBingHost)
            return uri.AbsoluteUri;

        if (!uri.AbsolutePath.Equals("/ck/a", StringComparison.OrdinalIgnoreCase))
            return null;

        var encoded = HttpUtility.ParseQueryString(uri.Query.TrimStart('?').TrimStart('!'))["u"];
        if (encoded is null || !encoded.StartsWith("a1", StringComparison.Ordinal))
            return null;

        var target = DecodeBase64Url(encoded[2..]);
        return target is not null
               && Uri.TryCreate(target, UriKind.Absolute, out var targetUri)
               && (targetUri.Scheme == Uri.UriSchemeHttp || targetUri.Scheme == Uri.UriSchemeHttps)
            ? targetUri.AbsoluteUri
            : null;
    }

    private static string? DecodeBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string Normalize(string innerText) =>
        string.Join(' ', HtmlEntity.DeEntitize(innerText).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
