using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Processes;
using DotCraft.Protocol.AppServer;

namespace DotCraft.SessionImport;

internal sealed class ImportHistoryStore(string userData, string workspaceData)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private string PathFor(string scope) => Path.Combine(scope == "user" ? userData : workspaceData, "imports", "history.json");

    public IReadOnlyList<ImportCompletedNotification> Read() => new[] { "user", "workspace" }
        .SelectMany(scope => (AtomicConfigDocument.Read(PathFor(scope))["imports"] as JsonArray ?? [])
            .Select(node => node!.Deserialize<ImportCompletedNotification>(Options)!))
        .GroupBy(batch => batch.ImportId)
        .Select(group => new ImportCompletedNotification
        {
            ImportId = group.Key,
            Trigger = group.First().Trigger,
            StartedAt = group.First().StartedAt,
            CompletedAt = group.First().CompletedAt,
            Outcomes = group.SelectMany(batch => batch.Outcomes).ToArray()
        })
        .OrderByDescending(batch => batch.CompletedAt).Take(100).ToArray();

    public void Save(ImportCompletedNotification batch)
    {
        foreach (var scope in new[] { "user", "workspace" })
        {
            var outcomes = batch.Outcomes.Where(o => o.Scope == scope).ToArray();
            if (outcomes.Length == 0) continue;
            AtomicConfigDocument.Update(PathFor(scope), root =>
            {
                var history = root["imports"] as JsonArray ?? new JsonArray();
                if (history.Parent == null) root["imports"] = history;
                history.Add(JsonSerializer.SerializeToNode(new ImportCompletedNotification
                {
                    ImportId = batch.ImportId,
                    Trigger = batch.Trigger,
                    StartedAt = batch.StartedAt,
                    CompletedAt = batch.CompletedAt,
                    Outcomes = outcomes
                }, Options));
                while (history.Count > 100) history.RemoveAt(0);
            });
        }
    }

    public IDisposable? BeginGlobalSync(TimeSpan interval)
    {
        var path = Path.Combine(userData, "imports", "sync.json");
        if (!CrossProcessFileLock.TryAcquire(path + ".lock", out var handle)) return null;
        try
        {
            var state = AtomicConfigDocument.Read(path);
            if (state["lastStartedAt"]?.GetValue<DateTimeOffset>() is { } last && DateTimeOffset.UtcNow - last < interval)
            {
                handle!.DeleteAfterDispose();
                return null;
            }
            AtomicConfigDocument.Update(path, root => root["lastStartedAt"] = DateTimeOffset.UtcNow);
            return handle;
        }
        catch { handle!.Dispose(); throw; }
    }
}
