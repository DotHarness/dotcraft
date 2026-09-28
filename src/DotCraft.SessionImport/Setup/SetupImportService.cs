using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Plugins.Marketplaces;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.SessionImport;

public sealed class SetupImportService(SetupImportPaths paths, IMarketplaceGitFetcher? pluginFetcher = null)
{
    internal IReadOnlyList<SetupImportItem> Scan(string source) => new SetupImportScanner(paths).Scan(source);

    internal async Task<ImportOutcome> InstallAsync(SetupImportItem item, string expectedFingerprint, CancellationToken ct)
    {
        var candidate = item.Candidate;
        if (candidate.Fingerprint != expectedFingerprint) return Outcome(candidate, "failed", "import_source_changed");
        if (candidate.State == "current") return Outcome(candidate, "existing");
        if (candidate.State != "new") return Outcome(candidate, "unsupported", candidate.Reason);
        try
        {
            ct.ThrowIfCancellationRequested();
            if (candidate.Category == "plugins") return await InstallPluginAsync(item, ct).ConfigureAwait(false);
            var imported = false;
            AtomicConfigDocument.WithLock(candidate.TargetPath, () =>
            {
                if (candidate.Category == "mcp")
                {
                    AtomicConfigDocument.Update(candidate.TargetPath, root =>
                    {
                        var servers = AtomicConfigDocument.Object(root, "McpServers");
                        if (AtomicConfigDocument.Key(servers, candidate.Title) != null) return;
                        servers[candidate.Title] = item.Config!.DeepClone();
                        imported = true;
                    });
                }
                else if (item.Directory != null)
                {
                    if (Directory.Exists(candidate.TargetPath)) return;
                    StageDirectory(item.Directory, candidate.TargetPath, staged =>
                    {
                        foreach (var skill in Directory.EnumerateFiles(staged, "SKILL.md", SearchOption.AllDirectories))
                            File.WriteAllText(skill, ImportTextRewrite.Apply(File.ReadAllText(skill), candidate.Source));
                    }, item.DirectoryHash);
                    imported = true;
                }
                else
                {
                    if (SetupImportScanner.NonEmpty(candidate.TargetPath)) return;
                    if (candidate.Category == "commands" && File.Exists(candidate.TargetPath)) return;
                    if (candidate.Category == "instructions" && SetupImportScanner.NonEmpty(Path.Combine(Path.GetDirectoryName(candidate.TargetPath)!, "AGENTS.override.md"))) return;
                    if (item.Scripts != null)
                    {
                        var targetScripts = Path.Combine(paths.Root(candidate.Scope), "hooks");
                        CopyMissingScripts(item.Scripts, targetScripts, item.ScriptsHash);
                    }
                    AtomicConfigDocument.Write(candidate.TargetPath, item.Text ?? item.Config!.ToJsonString());
                    imported = true;
                }
            });
            if (imported) PublishRevision(candidate.Scope);
            return Outcome(candidate, !imported ? "existing" : candidate.Category == "hooks"
                || candidate.Category == "mcp" && SetupConfigConverters.NeedsEnvironment(item.Config!) ? "attention" : "imported",
                imported && candidate.Category == "hooks" ? "import_hooks_need_trust"
                    : imported && candidate.Category == "mcp" && SetupConfigConverters.NeedsEnvironment(item.Config!) ? "import_environment_missing" : null);
        }
        catch (OperationCanceledException) { throw; }
        catch (NotSupportedException ex) { return Outcome(candidate, "unsupported", ex.Message); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException or MarketplaceException)
        {
            return Outcome(candidate, "failed", "import_write_failed");
        }
    }

    private async Task<ImportOutcome> InstallPluginAsync(SetupImportItem item, CancellationToken ct)
    {
        var plugin = item.Plugin!;
        var root = plugin.LocalRoot;
        string? checkout = null;
        try
        {
            if (plugin.Repository != null)
            {
                checkout = Path.Combine(paths.UserData, "imports", "staging", Guid.NewGuid().ToString("N"));
                await (pluginFetcher ?? new MarketplaceGitFetcher()).FetchAsync(MarketplaceSourceParser.Parse(plugin.Repository, plugin.Ref), checkout, ct).ConfigureAwait(false);
                root = ExternalPluginSource.ResolveEntry(checkout, Path.Combine(checkout, plugin.MarketplaceManifest!), plugin.Name);
            }
            if (root == null) return Outcome(item.Candidate, "failed", "import_plugin_content_missing");
            var attention = false;
            var installed = false;
            AtomicConfigDocument.WithLock(item.Candidate.TargetPath, () =>
            {
                if (Directory.Exists(item.Candidate.TargetPath)) return;
                StageDirectory(root, item.Candidate.TargetPath, staged =>
                    attention = ExternalPluginConverter.Convert(staged, item.Candidate.Source, plugin.Id, item.Candidate.TargetPath), item.DirectoryHash);
                installed = true;
            });
            if (installed) PublishRevision("user");
            return Outcome(item.Candidate, !installed ? "existing" : attention ? "attention" : "imported",
                attention ? "import_plugin_review" : null);
        }
        finally { if (checkout != null && Directory.Exists(checkout)) Directory.Delete(checkout, true); }
    }

    private static void StageDirectory(string source, string destination, Action<string> convert, string? expectedHash = null)
    {
        AtomicConfigDocument.RejectLinks(destination);
        var parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        var stage = Path.Combine(parent, ".import-" + Guid.NewGuid().ToString("N"));
        try
        {
            var actualHash = PluginContentTree.CopyAndFingerprint(source, stage, []);
            if (expectedHash != null && actualHash != expectedHash) throw new IOException("Source content changed during import.");
            foreach (var marker in Directory.EnumerateFiles(stage, ".builtin", SearchOption.AllDirectories)) File.Delete(marker);
            convert(stage);
            Directory.Move(stage, destination);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    private void PublishRevision(string scope) => AtomicConfigDocument.Update(Path.Combine(paths.Root(scope), "imports", "revision.json"),
        root => root["revision"] = Guid.NewGuid().ToString("N"));

    private static void CopyMissingScripts(string source, string target, string? expectedHash)
    {
        var parent = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(parent);
        var stage = Path.Combine(parent, ".import-hooks-" + Guid.NewGuid().ToString("N"));
        try
        {
            var hash = PluginContentTree.CopyAndFingerprint(source, stage, []);
            if (hash != expectedHash) throw new IOException("Hook scripts changed during import.");
            foreach (var file in Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(target, Path.GetRelativePath(stage, file));
                AtomicConfigDocument.RejectLinks(destination);
                if (File.Exists(destination)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(file, destination);
            }
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    internal static ImportOutcome Outcome(ImportCandidate candidate, string status, string? code = null) => new()
    {
        Source = candidate.Source, SourceId = candidate.SourceId, Category = candidate.Category, Scope = candidate.Scope,
        TargetPath = candidate.TargetPath, Title = candidate.Title, Status = status,
        ErrorCode = code == null ? default : Optional<string>.FromValue(code),
        Error = code == null ? default : Optional<string>.FromValue(ImportDiagnostics.Fallback(code))
    };
}
