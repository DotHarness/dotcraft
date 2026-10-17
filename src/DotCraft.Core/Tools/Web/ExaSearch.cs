using System.Net.Http.Json;
using System.Text.Json;

namespace DotCraft.Tools.Web;

internal static class ExaSearch
{
    private const string McpEndpoint = "https://mcp.exa.ai/mcp";

    private const string ApiEndpoint = "https://api.exa.ai/search";

    private const string ConfigHint = "Set Tools.Web.ExaApiKey to use your own Exa API key, or set Tools.Web.SearchProvider to Bing.";

    private const int SnippetMaxChars = 500;

    public static async Task<List<WebSearchResult>> SearchAsync(
        HttpClient client,
        string? apiKey,
        string query,
        int count,
        CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(apiKey)
            ? await SearchMcpAsync(client, query, count, cancellationToken)
            : await SearchApiAsync(client, apiKey, query, count, cancellationToken);

    private static async Task<List<WebSearchResult>> SearchApiAsync(
        HttpClient client,
        string apiKey,
        string query,
        int count,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiEndpoint);
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Content = JsonContent.Create(new
        {
            query,
            type = "auto",
            numResults = count,
            contents = new { highlights = new { highlightsPerUrl = 1 } }
        });

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new WebToolException($"Exa search failed with HTTP {(int)response.StatusCode}: {Shorten(body)}");
        return ParseApiResults(body);
    }

    internal static List<WebSearchResult> ParseApiResults(string body)
    {
        using var document = ParseJson(body);
        if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            throw new WebToolException($"Exa returned an unexpected response: {Shorten(body)}");

        var items = new List<WebSearchResult>();
        foreach (var result in results.EnumerateArray())
        {
            var url = GetString(result, "url");
            if (string.IsNullOrWhiteSpace(url))
                continue;
            var snippet = result.TryGetProperty("highlights", out var highlights)
                          && highlights.ValueKind == JsonValueKind.Array
                          && highlights.GetArrayLength() > 0
                ? highlights[0].GetString() ?? string.Empty
                : string.Empty;
            items.Add(new WebSearchResult(
                GetString(result, "title") is { Length: > 0 } title ? title : url,
                url,
                snippet.Trim(),
                GetString(result, "author"),
                GetString(result, "publishedDate")));
        }

        return items;
    }

    private static async Task<List<WebSearchResult>> SearchMcpAsync(
        HttpClient client,
        string query,
        int count,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, McpEndpoint);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Content = JsonContent.Create(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new
            {
                name = "web_search_exa",
                arguments = new { query, type = "auto", numResults = count, livecrawl = "fallback" }
            }
        });

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new WebToolException($"Exa search failed with HTTP {(int)response.StatusCode}. {ConfigHint}");
        return ParseMcpResponse(body);
    }

    internal static List<WebSearchResult> ParseMcpResponse(string body)
    {
        foreach (var payload in JsonPayloads(body))
        {
            using var document = ParseJson(payload);
            var root = document.RootElement;

            if (root.TryGetProperty("error", out var rpcError))
                throw new WebToolException($"Exa search failed: {GetString(rpcError, "message") ?? rpcError.GetRawText()}. {ConfigHint}");

            if (!root.TryGetProperty("result", out var result))
                continue;

            if (result.TryGetProperty("_meta", out var meta)
                && meta.ValueKind == JsonValueKind.Object
                && meta.TryGetProperty("ai.exa/rateLimited", out var limited)
                && limited.ValueKind == JsonValueKind.True)
                throw new WebToolException($"Exa's free search endpoint is rate limited. {ConfigHint}");

            var text = FirstText(result);
            if (result.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True)
                throw new WebToolException($"Exa search failed: {Shorten(text ?? string.Empty).TrimEnd('.')}. {ConfigHint}");

            var items = string.IsNullOrWhiteSpace(text) ? [] : ParseMcpText(text);
            if (items.Count == 0)
                throw new WebToolException($"Exa returned no readable results: {Shorten(text ?? string.Empty).TrimEnd('.')}. {ConfigHint}");
            return items;
        }

        throw new WebToolException($"Exa returned an unexpected response: {Shorten(body).TrimEnd('.')}. {ConfigHint}");
    }

    private static IEnumerable<string> JsonPayloads(string body)
    {
        var trimmed = body.Trim();
        if (trimmed.StartsWith('{'))
        {
            yield return trimmed;
            yield break;
        }

        foreach (var line in body.Split('\n'))
        {
            var current = line.TrimEnd('\r');
            if (!current.StartsWith("data:", StringComparison.Ordinal))
                continue;
            var data = current[5..].Trim();
            if (data.StartsWith('{'))
                yield return data;
        }
    }

    private static string? FirstText(JsonElement result)
    {
        if (!result.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var item in content.EnumerateArray())
        {
            if (GetString(item, "text") is { } text)
                return text;
        }

        return null;
    }

    private static List<WebSearchResult> ParseMcpText(string rawText)
    {
        var results = new List<WebSearchResult>();
        string? title = null;
        string? url = null;
        string? author = null;
        string? publishedDate = null;
        var snippet = new List<string>();
        var snippetChars = 0;
        var inBody = false;

        void Flush()
        {
            if (title is not null && !string.IsNullOrEmpty(url))
                results.Add(new WebSearchResult(Known(title) ?? url, url, string.Join(' ', snippet), Known(author), Known(publishedDate)));
            title = url = author = publishedDate = null;
            snippet.Clear();
            snippetChars = 0;
            inBody = false;
        }

        void AddSnippet(string part)
        {
            part = part.Trim();
            if (part.Length == 0 || snippetChars >= SnippetMaxChars)
                return;
            var take = Math.Min(part.Length, SnippetMaxChars - snippetChars);
            snippet.Add(part[..take]);
            snippetChars += take;
        }

        foreach (var rawLine in rawText.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith("Title: ", StringComparison.Ordinal) || line == "---")
            {
                Flush();
                if (line != "---")
                    title = line["Title: ".Length..].Trim();
            }
            else if (inBody)
                AddSnippet(line);
            else if (Field(line, "URL: ") is { } urlValue)
                url = urlValue;
            else if (Field(line, "Author: ") is { } authorValue)
                author = authorValue;
            else if (Field(line, "Published: ") is { } dateValue)
                publishedDate = dateValue;
            else if (Field(line, "Highlights:") is { } body)
            {
                inBody = true;
                AddSnippet(body);
            }
        }

        Flush();
        return results;
    }

    private static string? Field(string line, string prefix) =>
        line.StartsWith(prefix, StringComparison.Ordinal) ? line[prefix.Length..].Trim() : null;

    private static string? Known(string? value) =>
        string.IsNullOrWhiteSpace(value) || value == "N/A" ? null : value;

    private static JsonDocument ParseJson(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new WebToolException($"Exa returned invalid JSON: {Shorten(json)}");
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Shorten(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 300 ? trimmed : trimmed[..300] + "...";
    }
}
