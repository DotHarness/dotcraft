using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.SessionImport;
using Microsoft.Extensions.Logging;

namespace DotCraft.Hub;

internal sealed class HubLocalImportHistory(HubPaths paths, ProjectRegistry projects, ILogger logger)
{
    private string FilePath => Path.Combine(paths.CraftHomePath, "imports", "desktop-history.json");

    public (IReadOnlyList<HubLocalImportBatch> Imports, IReadOnlyList<HubLocalImportOutcome> Attention, DateTimeOffset? LastSyncAt)
        Read(DateTimeOffset? incompleteSince = null)
    {
        var stored = ReadStored();
        var covered = stored.Runs.SelectMany(run => run.ChildIds).ToHashSet(StringComparer.Ordinal);
        var imports = stored.Runs.Select(run => run.Batch).ToList();
        var attention = new List<HubLocalImportOutcome>();
        foreach (var root in projects.List().Select(project => project.Path).Append(paths.DefaultChatWorkspacePath))
        {
            try
            {
                var data = Path.Combine(root, ".craft");
                var name = root == paths.DefaultChatWorkspacePath ? null : Path.GetFileName(root);
                var history = LocalImportDiscovery.ReadHistory(paths.CraftHomePath, data);
                imports.AddRange(history.Where(batch => !covered.Contains(batch.ImportId)
                    && (incompleteSince is null || batch.StartedAt < incompleteSince))
                    .Select(batch => new HubLocalImportBatch(batch.ImportId, batch.Trigger, batch.StartedAt, batch.CompletedAt,
                        batch.Outcomes.Select(outcome => HubLocalImportOutcome.AtDestination(outcome, root, name)).ToArray())));
                if (history.Any(batch => batch.Outcomes.Any(outcome => outcome.Category is "hooks" or "mcp" or "plugins")))
                {
                    var settings = new SessionImportSettingsStore(paths.GlobalConfigPath, Path.Combine(data, "config.json"));
                    var service = new SessionImportService(new(root, data, new SessionImportConfig()), settings, []);
                    attention.AddRange(service.ReadAttention(new HashSet<string>())
                        .Select(outcome => HubLocalImportOutcome.AtDestination(outcome, root, name)));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                logger.LogDebug(ex, "Unable to read import history for {ProjectPath}", root);
            }
        }

        return (imports.GroupBy(batch => batch.ImportId)
                .Select(group => group.First() with { Outcomes = group.SelectMany(batch => batch.Outcomes).Distinct().ToArray() })
                .OrderByDescending(batch => batch.CompletedAt).Take(100).ToArray(),
            attention.Distinct().ToArray(), stored.LastSyncAt);
    }

    public void Save(HubLocalImportBatch batch, IReadOnlyList<string> childIds, bool recordSync)
    {
        AtomicConfigDocument.Update(FilePath, root =>
        {
            var stored = Decode(root);
            var runs = stored.Runs;
            var lastSyncAt = recordSync ? batch.CompletedAt : stored.LastSyncAt;
            if (batch.Outcomes.Count != 0 || batch.Trigger == "manual")
                runs = [.. runs.TakeLast(99), new StoredRun(batch, childIds)];
            root["lastSyncAt"] = JsonValue.Create(lastSyncAt);
            root["runs"] = JsonSerializer.SerializeToNode(runs, HubJson.Options);
        });
    }

    private StoredHistory ReadStored()
    {
        try
        {
            return Decode(AtomicConfigDocument.Read(FilePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogDebug(ex, "Unable to read desktop import runs");
            return new(null, []);
        }
    }

    private static StoredHistory Decode(JsonObject root) =>
        root.Deserialize<StoredHistory>(HubJson.Options) is { Runs: not null } stored ? stored : new(null, []);

    private sealed record StoredHistory(DateTimeOffset? LastSyncAt, IReadOnlyList<StoredRun> Runs);
    private sealed record StoredRun(HubLocalImportBatch Batch, IReadOnlyList<string> ChildIds);
}
