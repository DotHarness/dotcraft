using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;

namespace DotCraft.Tools.Web;

internal static class DeepSeekSearch
{
    public static async Task<List<WebSearchResult>> SearchAsync(
        HttpClient client, WebSearchTarget target, IProviderHttpTransport? transport,
        string query, int count, CancellationToken cancellationToken)
    {
        if (target.Error is not null)
            throw new WebToolException(target.Error);
        if (query.EnumerateRunes().Count() > 128)
            throw new WebToolException("DeepSeek search query must contain at most 128 Unicode characters.");

        var endpoint = ProviderWebSearchBackend.DeepSeekSearchEndpoint;
        if (target.Runtime is { IsRemote: true } runtime)
        {
            var providerEndpoint = new Uri(runtime.EndPoint.TrimEnd('/') + "/");
            client = (transport ?? throw new WebToolException("Remote model transport is not available."))
                .CreateClient(runtime.ProviderId, providerEndpoint);
            endpoint = new Uri(providerEndpoint, "tools/search");
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (target.Runtime?.IsRemote != true)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.ApiKey);
        request.Content = JsonContent.Create(new { query, count, need_content = false });
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new WebToolException($"DeepSeek search failed with HTTP {(int)response.StatusCode}. Check the provider credentials, balance, and rate limit.");
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!body.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            throw new WebToolException("DeepSeek returned an invalid search response.");
        var items = new List<WebSearchResult>();
        foreach (var item in results.EnumerateArray())
        {
            var url = Read(item, "url");
            if (string.IsNullOrWhiteSpace(url))
                continue;
            var title = Read(item, "title");
            var summary = Read(item, "summary");
            items.Add(new WebSearchResult(string.IsNullOrWhiteSpace(title) ? url : title, url,
                string.IsNullOrWhiteSpace(summary) ? Read(item, "snippet") ?? string.Empty : summary,
                PublishedDate: Read(item, "date")));
            if (items.Count == count)
                break;
        }
        return items;
    }

    private static string? Read(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
}
