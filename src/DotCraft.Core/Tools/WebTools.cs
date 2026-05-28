using System.ComponentModel;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotCraft.Configuration;

namespace DotCraft.Tools;

/// <summary>
/// Web tools: WebSearch and WebFetch.
/// </summary>
public sealed class WebTools
{
    private readonly HttpClient _httpClient;
    
    private readonly int _maxChars;
    
    private readonly int _timeoutSeconds;

    private readonly int _searchMaxResults;

    private readonly string _searchProvider;

    private const string UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_7_2) AppleWebKit/537.36";

    private const string BingUrl = "https://www.bing.com/search";

    private const string ExaMcpUrl = "https://mcp.exa.ai/mcp";

    public WebTools(
        int maxChars = 50000,
        int timeoutSeconds = 30,
        int searchMaxResults = 5,
        string searchProvider = WebSearchProvider.Exa)
    {
        _maxChars = maxChars;
        _timeoutSeconds = timeoutSeconds;
        _searchMaxResults = searchMaxResults;
        _searchProvider = searchProvider;
        
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
    }

    /// <summary>
    /// Search the web and return results.
    /// Supports multiple providers: Bing (globally accessible), Exa (AI-optimized, free via MCP).
    /// </summary>
    [Description("Search the web for current information. Returns titles, URLs, and snippets.")]
    [Tool(Icon = "🔍", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.WebSearch))]
    public async Task<string> WebSearch(
        [Description("The search query.")] string query,
        [Description("Maximum number of results to return (1-10).")] int? maxResults = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return JsonSerializer.Serialize(new { error = "Query cannot be empty." });
        }

        var count = Math.Clamp(maxResults ?? _searchMaxResults, 1, 10);

        try
        {
            List<SearchResultItem> items;

            if (_searchProvider.Equals(WebSearchProvider.Exa, StringComparison.OrdinalIgnoreCase))
            {
                items = await SearchExa(query, count);
            }
            else if (_searchProvider.Equals(WebSearchProvider.Bing, StringComparison.OrdinalIgnoreCase))
            {
                var bingResults = await SearchBing(query, count);
                items = bingResults
                    .Select(r => new SearchResultItem(r.Title, r.Url, r.Snippet))
                    .ToList();
            }
            else
            {
                throw new ArgumentException(nameof(_searchProvider));
            }

            if (items.Count == 0)
            {
                return JsonSerializer.Serialize(new { query, message = "No results found." });
            }

            return SerializeSearchOutput(query, _searchProvider, items);
        }
        catch (HttpRequestException ex)
        {
            return JsonSerializer.Serialize(new { error = $"Search request failed: {ex.Message}", query });
        }
        catch (TaskCanceledException)
        {
            return JsonSerializer.Serialize(new { error = $"Search timed out after {_timeoutSeconds} seconds.", query });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, query });
        }
    }

    /// <summary>
    /// Fetch and extract content from a URL.
    /// </summary>
    [Description("Fetch URL and extract readable content. Supports HTML, JSON, and plain text.")]
    [Tool(Icon = "🌐", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.WebFetch))]
    public async Task<string> WebFetch(
        [Description("The URL to fetch.")] string url,
        [Description("Extraction mode: 'markdown', 'text', or 'raw' for HTML.")] string extractMode = "markdown",
        [Description("Maximum characters to extract.")] int? maxChars = null)
    {
        var actualMaxChars = maxChars ?? _maxChars;
        
        if (!IsValidUrl(url, out var validationError))
        {
            return JsonSerializer.Serialize(new { error = $"URL validation failed: {validationError}", url });
        }

        try
        {
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var content = await response.Content.ReadAsStringAsync();
            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;

            string extractedContent;
            string extractor;

            if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                // Pretty print JSON
                try
                {
                    var jsonDoc = JsonDocument.Parse(content);
                    extractedContent = JsonSerializer.Serialize(jsonDoc, new JsonSerializerOptions { WriteIndented = true });
                    extractor = "json";
                }
                catch
                {
                    extractedContent = content;
                    extractor = "raw_json";
                }
            }
            else if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase) || 
                     content.TrimStart().StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
                     content.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            {
                // HTML content - extract readable parts
                if (extractMode == "markdown")
                {
                    extractedContent = HtmlToMarkdown(content);
                    extractor = "markdown";
                }
                else if (extractMode == "text")
                {
                    extractedContent = HtmlToText(content);
                    extractor = "text";
                }
                else
                {
                    extractedContent = content;
                    extractor = "raw_html";
                }
            }
            else
            {
                extractedContent = content;
                extractor = "raw";
            }

            var truncated = extractedContent.Length > actualMaxChars;
            if (truncated)
            {
                extractedContent = extractedContent[..actualMaxChars];
            }

            var result = new
            {
                url,
                finalUrl,
                status = (int)response.StatusCode,
                extractor,
                truncated,
                length = extractedContent.Length,
                text = extractedContent
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = false });
        }
        catch (HttpRequestException ex)
        {
            return JsonSerializer.Serialize(new { error = $"HTTP request failed: {ex.Message}", url });
        }
        catch (TaskCanceledException)
        {
            return JsonSerializer.Serialize(new { error = $"Request timed out after {_timeoutSeconds} seconds.", url });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, url });
        }
    }

    private static bool IsValidUrl(string url, out string error)
    {
        try
        {
            var uri = new Uri(url);
            
            if (uri.Scheme != "http" && uri.Scheme != "https")
            {
                error = $"Only http/https allowed, got '{uri.Scheme}'";
                return false;
            }

            if (string.IsNullOrWhiteSpace(uri.Host))
            {
                error = "Missing domain";
                return false;
            }

            // Reject localhost and private IPs (optional, can be disabled)
            if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.StartsWith("192.168.", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.StartsWith("10.", StringComparison.OrdinalIgnoreCase))
            {
                error = "Localhost and private IP addresses are not allowed";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (UriFormatException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string HtmlToText(string html)
    {
        // Remove script and style blocks
        var withoutScripts = Regex.Replace(html, @"<script[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        var withoutStyles = Regex.Replace(withoutScripts, @"<style[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
        
        // Remove all HTML tags
        var withoutTags = Regex.Replace(withoutStyles, @"<[^>]+>", " ");
        
        // Decode HTML entities
        var decoded = WebUtility.HtmlDecode(withoutTags);
        
        // Normalize whitespace
        var normalized = Regex.Replace(decoded, @"\s+", " ").Trim();
        
        return normalized;
    }

    private static string HtmlToMarkdown(string html)
    {
        // Simple HTML to Markdown conversion
        var result = html;

        // Remove script and style blocks
        result = Regex.Replace(result, @"<script[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<style[\s\S]*?</style>", "", RegexOptions.IgnoreCase);

        // Convert links: <a href="url">text</a> -> [text](url)
        result = Regex.Replace(result, @"<a\s+[^>]*href=[""']([^""']+)[""'][^>]*>([\s\S]*?)</a>",
            m => $"[{HtmlToText(m.Groups[2].Value)}]({m.Groups[1].Value})",
            RegexOptions.IgnoreCase);

        // Convert headings: <h1-6>text</h1-6> -> # text
        result = Regex.Replace(result, @"<h([1-6])[^>]*>([\s\S]*?)</h\1>",
            m => $"\n{new string('#', int.Parse(m.Groups[1].Value))} {HtmlToText(m.Groups[2].Value)}\n",
            RegexOptions.IgnoreCase);

        // Convert lists: <li>text</li> -> - text
        result = Regex.Replace(result, @"<li[^>]*>([\s\S]*?)</li>",
            m => $"\n- {HtmlToText(m.Groups[1].Value)}",
            RegexOptions.IgnoreCase);

        // Convert block elements to newlines
        result = Regex.Replace(result, @"</(p|div|section|article)>", "\n\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<(br|hr)\s*/?>", "\n", RegexOptions.IgnoreCase);

        // Remove remaining HTML tags
        result = Regex.Replace(result, @"<[^>]+>", " ");

        // Decode HTML entities
        result = WebUtility.HtmlDecode(result);

        // Normalize whitespace - remove extra spaces but keep paragraph breaks
        result = Regex.Replace(result, @"[ \t]+", " ");
        result = Regex.Replace(result, @"\n{3,}", "\n\n");

        return result.Trim();
    }

    // Internal record for Bing HTML scraping (unchanged)
    private sealed record SearchResult(string Title, string Url, string Snippet);

    // Unified search result item shared across all providers
    private sealed record SearchResultItem(
        string Title,
        string Url,
        string Snippet,
        string? Author = null,
        string? PublishedDate = null);

    private static string SerializeSearchOutput(string query, string provider, List<SearchResultItem> items)
    {
        var output = new
        {
            query,
            provider = provider.ToLowerInvariant(),
            results = items.Select(r => new
            {
                title = r.Title,
                url = r.Url,
                snippet = r.Snippet,
                author = r.Author,
                publishedDate = r.PublishedDate,
            }).ToArray()
        };
        return JsonSerializer.Serialize(output);
    }

    #region Bing Search Provider

    private async Task<List<SearchResult>> SearchBing(string query, int maxResults)
    {
        var url = $"{BingUrl}?q={Uri.EscapeDataString(query)}&count={maxResults}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept", "text/html");
        request.Headers.Add("Accept-Language", "en-US,en;q=0.9");

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();
        return ParseBingResults(html, maxResults);
    }

    private static List<SearchResult> ParseBingResults(string html, int maxResults)
    {
        var results = new List<SearchResult>();

        var blockMatches = Regex.Matches(html,
            @"<li[^>]*b_algo[^>]*>(.*?)</li>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match blockMatch in blockMatches)
        {
            if (results.Count >= maxResults)
                break;

            var block = blockMatch.Groups[1].Value;

            var titleMatch = Regex.Match(block,
                @"<h2[^>]*>\s*<a[^>]+href=""([^""]*)""[^>]*>(.*?)</a>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            if (!titleMatch.Success)
                continue;

            var title = WebUtility.HtmlDecode(Regex.Replace(titleMatch.Groups[2].Value, @"<[^>]+>", "")).Trim();

            var citeMatch = Regex.Match(block,
                @"<cite[^>]*>(.*?)</cite>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            var resultUrl = string.Empty;
            if (citeMatch.Success)
            {
                resultUrl = WebUtility.HtmlDecode(Regex.Replace(citeMatch.Groups[1].Value, @"<[^>]+>", "")).Trim();
                if (!resultUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    resultUrl = "https://" + resultUrl;
                }
            }

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(resultUrl))
                continue;

            var snippet = string.Empty;
            var snippetMatch = Regex.Match(block,
                @"<p[^>]*>(.*?)</p>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (snippetMatch.Success)
            {
                snippet = WebUtility.HtmlDecode(Regex.Replace(snippetMatch.Groups[1].Value, @"<[^>]+>", "")).Trim();
            }

            results.Add(new SearchResult(title, resultUrl, snippet));
        }

        return results;
    }

    #endregion

    #region Exa Search Provider (Legacy MCP)

    // NOTE: This is a legacy manual MCP call to Exa. Consider using the MCP server integration instead:
    // Add to McpServers config: { "Name": "exa", "Transport": "http", "Url": "https://mcp.exa.ai/mcp" }
    // Then the Exa tools (WebSearch_exa, etc.) will be available as standard MCP tools.
    private async Task<List<SearchResultItem>> SearchExa(string query, int numResults)
    {
        var requestBody = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new
            {
                name = "web_search_exa",
                arguments = new
                {
                    query,
                    type = "auto",
                    numResults,
                    livecrawl = "fallback",
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, ExaMcpUrl);
        request.Headers.Add("Accept", "application/json, text/event-stream");
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseText = await response.Content.ReadAsStringAsync();

        foreach (var line in responseText.Split('\n'))
        {
            if (!line.StartsWith("data: ")) continue;
            var json = line[6..];
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("result", out var result) &&
                result.TryGetProperty("content", out var content) &&
                content.GetArrayLength() > 0)
            {
                var text = content[0].GetProperty("text").GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    return ParseExaText(text);
            }
        }

        return [];
    }

    /// <summary>
    /// Parses the plain-text format returned by the Exa MCP tool into structured result items.
    /// Each result block starts with a "Title:" line and may contain URL, Author, Published Date, and Text fields.
    /// The Text field content is truncated to <paramref name="snippetMaxChars"/> characters for the snippet.
    /// </summary>
    private static List<SearchResultItem> ParseExaText(string rawText, int snippetMaxChars = 500)
    {
        var results = new List<SearchResultItem>();

        string? title = null;
        string? url = null;
        string? author = null;
        string? publishedDate = null;
        var snippetChars = 0;
        var snippetParts = new List<string>();
        bool inText = false;

        void FlushResult()
        {
            if (title == null || url == null) return;
            var snippet = string.Join(" ", snippetParts
                .Select(p => p.Trim())
                .Where(p => p.Length > 0));
            results.Add(new SearchResultItem(title, url, snippet, author, publishedDate));
            title = null; url = null; author = null; publishedDate = null;
            snippetParts.Clear(); snippetChars = 0; inText = false;
        }

        foreach (var rawLine in rawText.Split('\n'))
        {
            var line = rawLine.TrimEnd();

            if (line.StartsWith("Title: ", StringComparison.Ordinal))
            {
                FlushResult();
                title = line["Title: ".Length..].Trim();
                inText = false;
            }
            else if (!inText && line.StartsWith("URL: ", StringComparison.Ordinal))
            {
                url = line["URL: ".Length..].Trim();
            }
            else if (!inText && line.StartsWith("Author: ", StringComparison.Ordinal))
            {
                author = line["Author: ".Length..].Trim();
            }
            else if (!inText && line.StartsWith("Published Date: ", StringComparison.Ordinal))
            {
                publishedDate = line["Published Date: ".Length..].Trim();
            }
            else if (!inText && line.StartsWith("Text: ", StringComparison.Ordinal))
            {
                inText = true;
                var firstPart = line["Text: ".Length..].Trim();
                if (firstPart.Length > 0 && snippetChars < snippetMaxChars)
                {
                    var take = Math.Min(firstPart.Length, snippetMaxChars - snippetChars);
                    snippetParts.Add(firstPart[..take]);
                    snippetChars += take;
                }
            }
            else if (inText && snippetChars < snippetMaxChars)
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    var take = Math.Min(trimmed.Length, snippetMaxChars - snippetChars);
                    snippetParts.Add(trimmed[..take]);
                    snippetChars += take;
                }
            }
        }

        FlushResult();
        return results;
    }

    #endregion
}
