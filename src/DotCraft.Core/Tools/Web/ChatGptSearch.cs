using System.Text.Json.Nodes;
using DotCraft.Agents;

namespace DotCraft.Tools.Web;

internal static class ChatGptSearch
{
    public static async Task<List<WebSearchResult>> SearchAsync(
        IProviderWebSearch? service, WebSearchTarget target, string query, int count, CancellationToken cancellationToken)
    {
        if (target.Error is not null)
            throw new WebToolException(target.Error);
        if (service is null || target.Runtime is null)
            throw new WebToolException("ChatGPT search is not available for this provider.");

        var commands = new JsonObject { ["search_query"] = new JsonArray(new JsonObject { ["q"] = query }) };
        ProviderWebSearchResult searched;
        try
        {
            searched = await service.SearchAsync(
                target.Runtime, new ProviderWebSearchRequest(Guid.NewGuid().ToString(), [], commands), cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            throw new WebToolException($"ChatGPT search failed: {ex.Message}");
        }

        var items = new List<WebSearchResult>();
        foreach (var entry in searched.Results.OfType<JsonObject>())
        {
            var url = Read(entry, "url");
            if (url is null)
                continue;
            items.Add(new WebSearchResult(Read(entry, "title") ?? url, url, Read(entry, "snippet") ?? string.Empty));
            if (items.Count == count)
                break;
        }
        return items;
    }

    private static string? Read(JsonObject entry, string name) =>
        entry[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;
}
