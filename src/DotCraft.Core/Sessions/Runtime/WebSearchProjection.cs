using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tools;

namespace DotCraft.Sessions;

internal static class WebSearchProjection
{
    public static WebSearchPayload Started(string callId, JsonObject? arguments) => new()
    {
        CallId = callId,
        Arguments = arguments?.DeepClone().AsObject()
    };

    public static WebSearchPayload Completed(WebSearchPayload started, ToolExecutionResult result)
    {
        if (!result.Success)
        {
            return started with
            {
                Status = "failed",
                ErrorMessage = result.Error?.Message ?? result.Content ?? "Web search failed.",
                Output = result.Content
            };
        }

        var action = DeriveAction(started.Arguments);
        return started with
        {
            Status = "completed",
            Query = DeriveQuery(action),
            Action = action,
            Results = result.StructuredContent is { ValueKind: JsonValueKind.Array } results
                ? JsonNode.Parse(results.GetRawText())!.AsArray()
                : [],
            Output = result.Content ?? string.Empty
        };
    }

    internal static JsonObject DeriveAction(JsonObject? commands)
    {
        var queries = Queries(commands, "search_query");
        if (queries.Count == 0)
            queries = Queries(commands, "image_query");
        if (queries.Count > 0)
        {
            return new JsonObject
            {
                ["type"] = "search",
                ["query"] = queries.Count == 1 ? queries[0] : null,
                ["queries"] = queries.Count == 1 ? null : new JsonArray(queries.Select(static q => (JsonNode)q).ToArray())
            };
        }

        var openUrl = Entries(commands, "open")
            .Select(static entry => ReadString(entry, "ref_id"))
            .FirstOrDefault(IsWebUrl);
        if (openUrl is not null)
            return new JsonObject { ["type"] = "openPage", ["url"] = openUrl };

        if (Entries(commands, "find").FirstOrDefault() is { } find)
        {
            var refId = ReadString(find, "ref_id");
            return new JsonObject
            {
                ["type"] = "findInPage",
                ["url"] = IsWebUrl(refId) ? refId : null,
                ["pattern"] = ReadString(find, "pattern")
            };
        }

        return new JsonObject { ["type"] = "other" };
    }

    internal static string DeriveQuery(JsonObject action) => ReadString(action, "type") switch
    {
        "search" => ReadString(action, "query")
                    ?? (action["queries"] is JsonArray { Count: > 0 } queries
                        ? queries[0]!.GetValue<string>() + " ..."
                        : string.Empty),
        "openPage" => ReadString(action, "url") ?? string.Empty,
        "findInPage" => ReadString(action, "url") is { } url
            ? $"'{ReadString(action, "pattern")}' in {url}"
            : $"'{ReadString(action, "pattern")}'",
        _ => string.Empty
    };

    private static List<string> Queries(JsonObject? commands, string name) =>
        Entries(commands, name)
            .Select(static entry => ReadString(entry, "q"))
            .Where(static q => !string.IsNullOrWhiteSpace(q))
            .Select(static q => q!)
            .ToList();

    private static IEnumerable<JsonObject> Entries(JsonObject? commands, string name) =>
        commands?[name] is JsonArray entries ? entries.OfType<JsonObject>() : [];

    private static string? ReadString(JsonObject entry, string name) =>
        entry[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static bool IsWebUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
