using DotCraft.AppBinding;
using DotCraft.Skills;

namespace DotCraft.Plugins;

public sealed record PluginVerificationRequest(string CandidatePath, bool ForPublish = false);

public sealed record PluginVerificationDiagnostic(string Severity, string Code, string Message, string? Path);

public sealed record PluginVerificationResult(
    bool IsValid,
    string CandidatePath,
    string? PluginId,
    string? Version,
    bool ForPublish,
    IReadOnlyList<PluginVerificationDiagnostic> Diagnostics);

public static partial class PluginVerifier
{
    private const string PlaceholderMarker = "[TODO:";

    public static async Task<PluginVerificationResult> VerifyAsync(
        PluginVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var root = Path.GetFullPath(request.CandidatePath);
        var findings = new Findings(root);
        if (!Directory.Exists(root))
        {
            findings.Error("PluginCandidateNotFound", $"Candidate directory not found: {request.CandidatePath}", null);
            return findings.ToResult(null, request.ForPublish);
        }

        var parsed = PluginManifestParser.Load(root);
        foreach (var diagnostic in parsed.Diagnostics)
        {
            if (diagnostic.Severity == PluginDiagnosticSeverity.Error || diagnostic.Code == "PluginManifestMissing")
                findings.Error(diagnostic.Code, diagnostic.Message, diagnostic.Path);
            else if (diagnostic.Severity == PluginDiagnosticSeverity.Warning)
                findings.Warning(diagnostic.Code, diagnostic.Message, diagnostic.Path);
        }

        var manifest = parsed.Manifest;
        if (manifest == null)
            return findings.ToResult(null, request.ForPublish);

        CheckDeclaredDirectories(manifest, findings);
        CheckDescriptors(manifest, findings);
        await CheckSkillsAsync(manifest, findings, cancellationToken).ConfigureAwait(false);
        CheckPlaceholders(manifest, findings);
        CheckPresentation(manifest, findings);
        CheckIcons(manifest, findings);
        if (request.ForPublish)
            CheckPublication(manifest, findings);

        return findings.ToResult(manifest, request.ForPublish);
    }

    private static void CheckDeclaredDirectories(PluginManifest manifest, Findings findings)
    {
        foreach (var (field, path) in new[]
                 {
                     ("skills", manifest.SkillsPath),
                     ("workflows", manifest.WorkflowsPath)
                 })
        {
            if (path != null && !Directory.Exists(path))
                findings.Error("PluginDeclaredPathMissing", $"'{field}' points to a directory that does not exist.", path);
        }

        foreach (var (name, path) in manifest.Paths)
        {
            if (!Directory.Exists(path) && !File.Exists(path))
                findings.Error("PluginDeclaredPathMissing", $"'paths.{name}' points to a path that does not exist.", path);
        }
    }

    private static void CheckDescriptors(PluginManifest manifest, Findings findings)
    {
        var plugin = new DiscoveredPlugin(manifest, PluginDiscoverySourceKind.Explicit, manifest.RootPath, Enabled: true);
        var diagnostics = new List<PluginDiagnostic>();
        PluginMcpServerLoader.LoadPluginServers(plugin, diagnostics);
        PluginLspServerLoader.LoadPluginServers(plugin, diagnostics);
        PluginHookLoader.LoadPluginHooks(plugin, diagnostics);
        AppBindingCatalog.LoadPluginAppDescriptors(plugin, diagnostics);

        foreach (var diagnostic in diagnostics.Where(d => d.Severity != PluginDiagnosticSeverity.Info))
            findings.Error(diagnostic.Code, diagnostic.Message, diagnostic.Path);
    }

    private static async Task CheckSkillsAsync(
        PluginManifest manifest,
        Findings findings,
        CancellationToken cancellationToken)
    {
        var skillsPath = manifest.SkillsPath;
        if (skillsPath == null || !Directory.Exists(skillsPath))
            return;

        var directories = SkillDirectories(skillsPath).ToArray();
        if (directories.Length == 0)
        {
            findings.Error("PluginSkillsEmpty", "'skills' contains no skill directories.", skillsPath);
            return;
        }

        var service = new SkillInstallService(new SkillsLoader(manifest.RootPath));
        foreach (var directory in directories)
        {
            var skillFile = Path.Combine(directory, "SKILL.md");
            var result = await service.VerifyAsync(new SkillInstallVerifyRequest(directory), cancellationToken)
                .ConfigureAwait(false);
            foreach (var error in result.Errors)
                findings.Error("InvalidPluginSkill", error, skillFile);

            var directoryName = Path.GetFileName(directory);
            if (result.SkillName != null
                && !string.Equals(result.SkillName, directoryName, StringComparison.OrdinalIgnoreCase))
            {
                findings.Error(
                    "InvalidPluginSkill",
                    $"Skill name '{result.SkillName}' does not match its directory '{directoryName}'.",
                    skillFile);
            }
        }
    }

    private static void CheckPlaceholders(PluginManifest manifest, Findings findings)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { manifest.ManifestPath };
        files.UnionWith(Directory.EnumerateFiles(manifest.RootPath, "*.json"));
        foreach (var path in new[] { manifest.McpServersPath, manifest.LspServersPath, manifest.AppsPath })
        {
            if (path != null)
                files.Add(path);
        }
        files.UnionWith(manifest.Hooks?.Paths ?? []);

        if (manifest.SkillsPath != null && Directory.Exists(manifest.SkillsPath))
        {
            foreach (var directory in SkillDirectories(manifest.SkillsPath))
            {
                files.Add(Path.Combine(directory, "SKILL.md"));
                files.Add(Path.Combine(directory, "agents", "openai.yaml"));
            }
        }

        foreach (var file in files.Where(File.Exists))
        {
            if (File.ReadAllText(file).Contains(PlaceholderMarker, StringComparison.Ordinal))
                findings.Error("PluginPlaceholderRemaining", "File still contains a [TODO: ...] placeholder.", file);
        }
    }

    private static IEnumerable<string> SkillDirectories(string skillsPath) =>
        Directory.EnumerateDirectories(skillsPath)
            .Where(directory => File.Exists(Path.Combine(directory, "SKILL.md")))
            .Order(StringComparer.Ordinal);

    private sealed class Findings(string root)
    {
        private readonly List<PluginVerificationDiagnostic> _items = [];

        public void Error(string code, string message, string? path) => Add("error", code, message, path);

        public void Warning(string code, string message, string? path) => Add("warning", code, message, path);

        public PluginVerificationResult ToResult(PluginManifest? manifest, bool forPublish) => new(
            _items.All(item => item.Severity != "error"),
            root,
            manifest?.Id,
            manifest?.Version,
            forPublish,
            _items.Distinct().ToArray());

        private void Add(string severity, string code, string message, string? path)
        {
            var relative = path == null || !Path.IsPathRooted(path)
                ? path
                : Path.GetRelativePath(root, path).Replace('\\', '/');
            _items.Add(new PluginVerificationDiagnostic(severity, code, message, relative));
        }
    }
}
