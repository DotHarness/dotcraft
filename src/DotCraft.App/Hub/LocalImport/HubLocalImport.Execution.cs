using DotCraft.Configuration;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.SessionImport;
using Microsoft.Extensions.Logging;

namespace DotCraft.Hub;

internal sealed partial class HubLocalImport
{
    private async Task ExecuteAsync(string id, DateTimeOffset started, IReadOnlyList<string> sources, ImportSelection selection,
        HubLocalImportRequest? manual, IReadOnlySet<string>? syncTargets)
    {
        var outcomes = new List<HubLocalImportOutcome>();
        var childIds = new List<string>();
        try
        {
            var detected = (await DetectAsync(_stopping)).Where(source => sources.Contains(source.Source))
                .SelectMany(source => source.Items).ToDictionary(item => item.Key, StringComparer.Ordinal);
            var chosen = SelectItems(detected, selection, manual, syncTargets, outcomes);
            var total = chosen.Count + outcomes.Count;
            var groups = chosen.GroupBy(DestinationPath, ManagedAppServerRegistry.WorkspaceComparer)
                .OrderBy(group => group.Any(item => item.Destination == "newProject"));
            var completed = outcomes.Count;
            Report(id, sources[0], completed, total);
            foreach (var group in groups)
            {
                _stopping.ThrowIfCancellationRequested();
                var targetSources = group.Select(item => item.Candidate.Source).Distinct(StringComparer.Ordinal).ToArray();
                var count = group.Count();
                var preparing = manual is not null;
                try
                {
                    if (preparing)
                        PrepareDestination(group.Key, group.Any(item => item.Destination == "newProject"));
                    preparing = false;
                    var batch = await _runTarget(group.Key, new ImportRunParams
                    {
                        Sources = targetSources,
                        Items = group.Select(item => new ImportItemReference
                        {
                            Source = item.Candidate.Source, SourceId = item.Candidate.SourceId,
                            Fingerprint = item.Candidate.Fingerprint
                        }).ToArray(),
                        Selection = selection,
                        Offered = Optional<ImportSelection>.FromValue(manual?.Offered ?? new ImportSelection())
                    }, manual is not null, done => Report(id, targetSources[0], completed + Math.Clamp(done, 0, count), total), _stopping);
                    childIds.Add(batch.ImportId);
                    var name = ManagedAppServerRegistry.WorkspaceComparer.Equals(group.Key, _paths.DefaultChatWorkspacePath)
                        ? null : Path.GetFileName(group.Key);
                    outcomes.AddRange(batch.Outcomes.Select(outcome => HubLocalImportOutcome.AtDestination(outcome, group.Key, name)));
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !_stopping.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Import failed for destination {ProjectPath}", group.Key);
                    var error = preparing ? "import_project_unavailable" : "import_pass_failed";
                    outcomes.AddRange(group.Select(item => Failed(item, error)));
                }
                completed += count;
                Report(id, targetSources[0], completed, total);
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Local import could not finish");
            outcomes.Add(Failed(null, "import_pass_failed", sources[0]));
        }
        finally
        {
            lock (_gate)
            {
                try
                {
                    if (!_stopping.IsCancellationRequested || outcomes.Count > 0)
                        _history.Save(new(id, manual is null ? "sync" : "manual", started, DateTimeOffset.UtcNow, outcomes),
                            childIds, manual is null && !_stopping.IsCancellationRequested);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Unable to save desktop import history");
                }
                _running = null;
            }
        }
    }

    private List<LocalImportItem> SelectItems(IReadOnlyDictionary<string, LocalImportItem> detected,
        ImportSelection selection, HubLocalImportRequest? manual, IReadOnlySet<string>? syncTargets, List<HubLocalImportOutcome> outcomes)
    {
        static bool Eligible(LocalImportItem item, ImportSelection selection) =>
            item.Candidate.State is "new" or "changed"
            && item.Destination != "unavailable"
            && ImportCategories.Includes(selection, item.Candidate.Category, item.Candidate.Scope);

        if (manual is null)
        {
            var optOut = new Dictionary<string, bool>(ManagedAppServerRegistry.WorkspaceComparer);
            return detected.Values.Where(item => Eligible(item, selection) && item.Destination != "newProject"
                && syncTargets?.Contains(DestinationPath(item)) == true
                && (item.Destination == "personal" || !IsSyncOptedOut(DestinationPath(item), optOut))).ToList();
        }

        var items = new List<LocalImportItem>();
        foreach (var reference in manual.Items)
        {
            detected.TryGetValue(reference.Key, out var item);
            if (item is not null && Eligible(item, selection)
                && string.Equals(reference.Fingerprint, item.Candidate.Fingerprint, StringComparison.Ordinal))
                items.Add(item);
            else
                outcomes.Add(Failed(item, "import_source_changed", manual.Source, reference.Key));
        }
        return items;
    }

    private bool IsSyncOptedOut(string path, Dictionary<string, bool> cache)
    {
        if (cache.TryGetValue(path, out var optedOut))
            return optedOut;
        try
        {
            return cache[path] = SettingsFor(path).ReadWorkspaceOptOut();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
                                   or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Unable to read import preferences for {ProjectPath}", path);
            return cache[path] = true;
        }
    }

    private void PrepareDestination(string path, bool register)
    {
        if (ManagedAppServerRegistry.WorkspaceComparer.Equals(path, _paths.DefaultChatWorkspacePath))
        {
            DefaultChatWorkspace.Ensure(_paths);
            return;
        }

        if (!register)
            return;

        var configPath = ConfigPath(path);
        AtomicConfigDocument.WithLock(configPath, () =>
        {
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException();
            if (!File.Exists(configPath))
                AtomicConfigDocument.Write(configPath, "{}" + Environment.NewLine);
        });
        _projects.Open(path);
        _events.Publish("projects.changed");
    }

    private string DestinationPath(LocalImportItem item) => item.ProjectPath ?? _paths.DefaultChatWorkspacePath;

    private static HubLocalImportOutcome Failed(LocalImportItem? item, string code, string source = "", string sourceId = "") => new(
        item?.Candidate.Category ?? "sessions", item?.Candidate.Scope ?? "workspace", item?.Candidate.TargetPath ?? "",
        item?.Candidate.Source ?? source, item?.Candidate.SourceId ?? sourceId,
        "failed", null, item?.Candidate.Title, code, null, item?.ProjectPath, item?.ProjectName);
}
