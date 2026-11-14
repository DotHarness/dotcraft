using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.CodexGateway.Workspace;
using DotCraft.Sdk;
using DotCraft.Sdk.Hub;

namespace DotCraft.CodexGateway.Protocol;

public sealed partial class CodexGatewaySession
{
    private async Task<object> ListModelsAsync(JsonElement p, IReadOnlyList<CodexWorkspace> workspaces, CancellationToken ct)
    {
        var models = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        string? defaultId = null;
        Exception? firstFailure = null;
        var accessible = 0;
        foreach (var route in workspaces)
        {
            try
            {
                var backend = await GetWorkspaceAsync(route, ct);
                string? next = null;
                do
                {
                    var result = await backend.HandleAsync("model/list",
                        WorkspaceJson.Element(new { limit = 100, cursor = next, modelProvider = p.Text("modelProvider"), includeHidden = p.Flag("includeHidden") }), ct);
                    var page = JsonSerializer.SerializeToElement(result);
                    foreach (var item in page.Get("data").Rows())
                    {
                        var id = item.Required("id");
                        if (p.Text("modelProvider") is { } filter && Uri.UnescapeDataString(id.Split('/')[1]) != filter) continue;
                        if (route.Id == workspaces[0].Id && item.Flag("isDefault")) defaultId = id;
                        var incoming = JsonNode.Parse(item.GetRawText())!.AsObject();
                        if (models.TryGetValue(id, out var existing))
                        {
                            Intersect(existing, incoming, "supportedReasoningEfforts", "reasoningEffort");
                            Intersect(existing, incoming, "inputModalities", null);
                            Intersect(existing, incoming, "additionalSpeedTiers", null);
                        }
                        else models.Add(id, incoming);
                    }
                    next = page.Text("nextCursor");
                } while (next is not null);
                accessible++;
            }
            catch (Exception exception) when (workspaces.Count > 1 &&
                exception is IOException or HttpRequestException or DotCraftException or HubClientException)
            {
                firstFailure ??= exception;
                _diagnostic?.Invoke("workspaceUnavailable:" + route.Id);
            }
        }
        if (accessible == 0 && firstFailure is not null) throw firstFailure;
        var scope = "models:" + string.Join(",", workspaces.Select(w => w.Id))
            + ":" + p.Text("modelProvider") + ":" + p.Flag("includeHidden");
        var offset = GatewayCursor.Read(p.Text("cursor"), scope)?.Offset ?? 0;
        var limit = p.Limit(100);
        foreach (var pair in models)
        {
            pair.Value["isDefault"] = pair.Key == defaultId;
            var supported = pair.Value["supportedReasoningEfforts"]!.AsArray();
            var effort = pair.Value["defaultReasoningEffort"]?.GetValue<string>();
            if (!supported.Any(e => e?["reasoningEffort"]?.GetValue<string>() == effort))
                pair.Value["defaultReasoningEffort"] = supported.FirstOrDefault()?["reasoningEffort"]?.GetValue<string>() ?? "none";
        }
        var data = models.Values.Skip(offset).Take(limit).ToArray();
        return new
        {
            data,
            nextCursor = offset + data.Length < models.Count
                ? GatewayCursor.Write(new(scope, Offset: offset + data.Length)) : null
        };
    }

    private static void Intersect(JsonObject target, JsonObject other, string field, string? key)
    {
        var incoming = other[field]!.AsArray().Select(node => key is null ? node!.GetValue<string>() : node![key]!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);
        target[field] = new JsonArray(target[field]!.AsArray()
            .Where(node => incoming.Contains(key is null ? node!.GetValue<string>() : node![key]!.GetValue<string>()))
            .Select(node => node!.DeepClone()).ToArray());
    }
}
