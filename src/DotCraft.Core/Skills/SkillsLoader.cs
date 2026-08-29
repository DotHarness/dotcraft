using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using DotCraft.Plugins;
using DotCraft.Workspaces;

namespace DotCraft.Skills;

/// <summary>
/// Loader for agent skills from Skills/ directory.
/// Skills are markdown files (SKILL.md) that teach the agent specific capabilities.
/// </summary>
public sealed class SkillsLoader
{
    private const int MaxIconBytes = 512 * 1024;

    private HashSet<string> _disabledSkills = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<PluginSkillSource> _pluginSkillSources = [];

    private HashSet<string> _disabledPluginSkillNames = new(StringComparer.OrdinalIgnoreCase);

    private readonly SkillVariantStore _variantStore;

    /// <summary>Creates a loader from the runtime path context.</summary>
    public SkillsLoader(DotCraftPaths paths)
        : this(paths.Data.RootPath, paths.UserData.ResolveOrNull("skills"))
    {
    }

    /// <summary>Creates a loader from already resolved skill roots.</summary>
    public SkillsLoader(string workspaceRoot, string? userSkillsPath = null)
    {
        WorkspaceSkillsPath = Path.Combine(workspaceRoot, "skills");
        UserSkillsPath = userSkillsPath;
        _variantStore = new SkillVariantStore(workspaceRoot);
    }

    /// <summary>
    /// Gets the workspace skills path.
    /// </summary>
    public string WorkspaceSkillsPath { get; }

    /// <summary>
    /// Gets the user skills path.
    /// </summary>
    public string? UserSkillsPath { get; }

    /// <summary>
    /// Gets the workspace-local variant store.
    /// </summary>
    public SkillVariantStore VariantStore => _variantStore;

    /// <summary>
    /// Replaces the set of disabled skill names (workspace UI / config). Disabled skills stay on disk but are omitted from agent context.
    /// </summary>
    public void SetDisabledSkills(IEnumerable<string>? names)
    {
        _disabledSkills = new HashSet<string>(names ?? [], StringComparer.OrdinalIgnoreCase);
    }

    public void SetPluginSkillSources(
        IEnumerable<PluginSkillSource>? sources,
        IEnumerable<string>? disabledPluginSkillNames = null)
    {
        _pluginSkillSources = (sources ?? [])
            .Where(source => !string.IsNullOrWhiteSpace(source.PluginId)
                             && !string.IsNullOrWhiteSpace(source.SkillsPath)
                             && !string.IsNullOrWhiteSpace(source.PluginRoot))
            .ToArray();
        _disabledPluginSkillNames = new HashSet<string>(
            disabledPluginSkillNames ?? [],
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns whether a skill is enabled (not listed in disabled set).
    /// </summary>
    public bool IsSkillEnabled(string name) => !_disabledSkills.Contains(name);

    /// <summary>
    /// Returns the directory where a workspace-owned skill with the given name should live.
    /// </summary>
    public string ResolveWorkspaceSkillDir(string name) => Path.Combine(WorkspaceSkillsPath, name);

    /// <summary>
    /// Returns whether the given skill currently resolves to the workspace skill directory.
    /// </summary>
    public bool IsWorkspaceSkill(string name)
    {
        var skillFile = Path.Combine(ResolveWorkspaceSkillDir(name), "SKILL.md");
        return File.Exists(skillFile);
    }

    /// <summary>
    /// Invalidates cached skill descriptors. This loader currently scans on demand,
    /// but mutation callers use this hook so future caching can be added centrally.
    /// </summary>
    public void RefreshDescriptors()
    {
    }

    /// <summary>
    /// List all available skills (workspace and builtin).
    /// </summary>
    /// <param name="filterUnavailable">If true, filter out skills with unmet requirements.</param>
    public List<SkillInfo> ListSkills(bool filterUnavailable = true)
    {
        var skills = new List<SkillInfo>();

        // User-owned workspace skills have the highest priority.
        if (Directory.Exists(WorkspaceSkillsPath))
        {
            foreach (var dir in EnumerateSkillDirectories(WorkspaceSkillsPath))
            {
                var skillFile = Path.Combine(dir, "SKILL.md");
                if (!File.Exists(skillFile) || File.Exists(Path.Combine(dir, ".builtin")))
                    continue;

                AddSkillInfo(skills, Path.GetFileName(dir), skillFile, "workspace");
            }
        }

        foreach (var source in _pluginSkillSources.OrderBy(s => s.PluginId, StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(source.SkillsPath))
                continue;

            foreach (var dir in EnumerateSkillDirectories(source.SkillsPath))
            {
                var skillFile = Path.Combine(dir, "SKILL.md");
                if (!File.Exists(skillFile))
                    continue;

                AddSkillInfo(
                    skills,
                    Path.GetFileName(dir),
                    skillFile,
                    "plugin",
                    source.PluginId,
                    source.PluginDisplayName);
            }
        }

        // Built-in workspace skills are kept for compatibility and are lower priority than plugin-contained skills.
        if (Directory.Exists(WorkspaceSkillsPath))
        {
            foreach (var dir in EnumerateSkillDirectories(WorkspaceSkillsPath))
            {
                var skillFile = Path.Combine(dir, "SKILL.md");
                if (!File.Exists(skillFile) || !File.Exists(Path.Combine(dir, ".builtin")))
                    continue;

                var name = Path.GetFileName(dir);
                if (_disabledPluginSkillNames.Contains(name))
                    continue;

                AddSkillInfo(skills, name, skillFile, "builtin");
            }
        }

        // User skills
        if (UserSkillsPath is not null && Directory.Exists(UserSkillsPath))
        {
            foreach (var dir in EnumerateSkillDirectories(UserSkillsPath))
            {
                var skillFile = Path.Combine(dir, "SKILL.md");
                if (File.Exists(skillFile))
                {
                    var name = Path.GetFileName(dir);
                    // Skip if workspace has skill with same name
                    if (skills.Any(s => s.Name == name))
                        continue;

                    AddSkillInfo(skills, name, skillFile, "user");
                }
            }
        }

        // Filter by requirements if requested
        if (filterUnavailable)
            return SortSkills(skills.Where(s => s.Available)).ToList();

        return SortSkills(skills).ToList();
    }

    private static IEnumerable<string> EnumerateSkillDirectories(string path) =>
        Directory.GetDirectories(path)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);

    private static IOrderedEnumerable<SkillInfo> SortSkills(IEnumerable<SkillInfo> skills) =>
        skills.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase);

    private void AddSkillInfo(
        List<SkillInfo> skills,
        string name,
        string skillFile,
        string source,
        string? pluginId = null,
        string? pluginDisplayName = null)
    {
        if (skills.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            return;

        var metadata = GetSkillMetadataFromFile(skillFile);
        var requirements = GetSkillRequirements(metadata);

        var skillInfo = new SkillInfo
        {
            Name = name,
            Path = skillFile,
            Source = source,
            PluginId = pluginId,
            PluginDisplayName = pluginDisplayName,
            Requirements = requirements
        };

        CheckRequirements(requirements, out var unavailableReason);
        skillInfo.Available = string.IsNullOrEmpty(unavailableReason);
        skillInfo.UnavailableReason = unavailableReason;
        skillInfo.Enabled = !_disabledSkills.Contains(name);

        skills.Add(skillInfo);
    }

    /// <summary>
    /// Load a skill by name.
    /// </summary>
    public string? LoadSkill(string name)
    {
        var skillFile = ResolveSkillFileDirect(name, out _);
        return skillFile == null ? null : File.ReadAllText(skillFile, Encoding.UTF8);
    }

    /// <summary>
    /// Loads the effective source-or-variant skill body.
    /// </summary>
    public EffectiveSkill? LoadEffectiveSkill(
        string name,
        bool variantModeEnabled,
        SkillVariantTarget? target,
        bool stripFrontmatter = true)
    {
        var source = ResolveSkillInfo(name);
        if (source == null)
            return null;

        var effectivePath = ResolveEffectiveSkillFile(source, variantModeEnabled, target);
        if (effectivePath == null)
            return null;

        var content = File.ReadAllText(effectivePath, Encoding.UTF8);
        if (stripFrontmatter)
            content = StripFrontmatter(content);

        return new EffectiveSkill(
            source.Name,
            content,
            effectivePath,
            string.Equals(effectivePath, source.Path, StringComparison.OrdinalIgnoreCase) ? "source" : "variant");
    }

    /// <summary>
    /// Resolves the effective physical <c>SKILL.md</c> path for source or current variant.
    /// </summary>
    public string? ResolveEffectiveSkillFile(SkillInfo source, bool variantModeEnabled, SkillVariantTarget? target)
    {
        if (!variantModeEnabled || target == null)
            return source.Path;

        var fingerprint = SkillVariantStore.ComputeSourceFingerprint(source.Path);
        var variant = _variantStore.FindCurrentVariant(source, fingerprint, target);
        if (variant == null)
            return source.Path;

        var variantSkillFile = Path.Combine(_variantStore.GetVariantSkillDir(variant), "SKILL.md");
        return File.Exists(variantSkillFile) ? variantSkillFile : source.Path;
    }

    /// <summary>
    /// Restores the original source skill for the given target.
    /// </summary>
    public bool RestoreOriginalSkill(string name, SkillVariantTarget target)
    {
        var source = ResolveSkillInfo(name);
        if (source == null)
            return false;

        var fingerprint = SkillVariantStore.ComputeSourceFingerprint(source.Path);
        return _variantStore.RestoreOriginal(source, fingerprint, target);
    }

    /// <summary>
    /// Reads optional display metadata from <c>agents/openai.yaml</c>.
    /// Missing or invalid interface metadata is treated as absent.
    /// </summary>
    public SkillInterfaceInfo? GetSkillInterface(string name)
    {
        var skillFile = ResolveSkillFileDirect(name, out var pluginRoot);
        if (skillFile == null)
            return null;

        return pluginRoot == null
            ? GetSkillInterfaceFromFile(skillFile)
            : GetPluginSkillInterfaceFromFile(skillFile, pluginRoot);
    }

    /// <summary>
    /// Reads optional display metadata from <c>agents/openai.yaml</c> beside a specific skill file.
    /// Missing or invalid interface metadata is treated as absent.
    /// </summary>
    public static SkillInterfaceInfo? GetSkillInterfaceFromFile(string skillFile) =>
        ReadSkillInterfaceFromFile(skillFile, pluginRoot: null);

    /// <summary>
    /// Reads optional display metadata beside a plugin-contained skill file and permits icons from
    /// the owning plugin's shared <c>assets/</c> directory.
    /// </summary>
    public static SkillInterfaceInfo? GetPluginSkillInterfaceFromFile(string skillFile, string pluginRoot) =>
        ReadSkillInterfaceFromFile(skillFile, pluginRoot);

    private static SkillInterfaceInfo? ReadSkillInterfaceFromFile(string skillFile, string? pluginRoot)
    {
        var skillDir = Path.GetDirectoryName(skillFile);
        if (string.IsNullOrEmpty(skillDir))
            return null;

        var manifestPath = Path.Combine(skillDir, "agents", "openai.yaml");
        if (!File.Exists(manifestPath))
            return null;

        Dictionary<string, string> values;
        try
        {
            values = ParseOpenAiInterfaceManifest(File.ReadAllText(manifestPath, Encoding.UTF8));
        }
        catch
        {
            return null;
        }

        if (values.Count == 0)
            return null;

        values.TryGetValue("display_name", out var displayName);
        values.TryGetValue("short_description", out var shortDescription);
        values.TryGetValue("default_prompt", out var defaultPrompt);
        values.TryGetValue("icon_small", out var iconSmall);
        values.TryGetValue("icon_large", out var iconLarge);

        return new SkillInterfaceInfo
        {
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName,
            ShortDescription = string.IsNullOrWhiteSpace(shortDescription) ? null : shortDescription,
            DefaultPrompt = string.IsNullOrWhiteSpace(defaultPrompt) ? null : defaultPrompt,
            IconSmallDataUrl = TryReadIconDataUrl(skillDir, iconSmall, pluginRoot),
            IconLargeDataUrl = TryReadIconDataUrl(skillDir, iconLarge, pluginRoot)
        };
    }

    /// <summary>
    /// Load specific skills for inclusion in agent context.
    /// </summary>
    public string LoadSkillsForContext(IEnumerable<string> skillNames)
    {
        return LoadSkillsForContext(skillNames, variantModeEnabled: false, target: null);
    }

    /// <summary>
    /// Load specific effective skills for inclusion in agent context.
    /// </summary>
    public string LoadSkillsForContext(
        IEnumerable<string> skillNames,
        bool variantModeEnabled,
        SkillVariantTarget? target)
    {
        var parts = new List<string>();

        foreach (var name in skillNames)
        {
            if (_disabledSkills.Contains(name))
                continue;
            var effective = LoadEffectiveSkill(name, variantModeEnabled, target);
            if (effective == null)
                continue;

            parts.Add($"### Skill: {name}\n\n{effective.Content}");
        }

        return parts.Count > 0 ? string.Join("\n\n---\n\n", parts) : string.Empty;
    }

    /// <summary>
    /// Deploy built-in skills (embedded in the assembly) to the user skills directory.
    /// Skips skills that were created by the user (no .builtin marker) and skills
    /// that are already up to date.
    /// </summary>
    /// <param name="resourceAssembly">
    /// The assembly that contains the embedded skill resources.
    /// Pass <c>typeof(Program).Assembly</c> (or equivalent) from the host application.
    /// Falls back to the assembly containing <see cref="SkillsLoader"/> when null.
    /// </param>
    public void DeployBuiltInSkills(Assembly? resourceAssembly = null)
    {
        const string resourcePrefix = "DotCraft.Skills.BuiltIn.";
        const string markerFile = ".builtin";

        var assembly = resourceAssembly ?? typeof(SkillsLoader).Assembly;
        var currentVersion = PluginHostVersion.Current.ProductText;

        // Group resources by skill name
        var resourcesBySkill = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(resourcePrefix, StringComparison.Ordinal))
            .Select(name =>
            {
                var remainder = name[resourcePrefix.Length..];
                var dotIndex = remainder.IndexOf('.');
                // Resources at the BuiltIn root (e.g. .gitkeep) have no skill prefix
                if (dotIndex <= 0)
                    return (SkillName: string.Empty, FileName: remainder, ResourceName: name);
                return (
                    SkillName: remainder[..dotIndex],
                    FileName: remainder[(dotIndex + 1)..],
                    ResourceName: name
                );
            })
            .Where(r => !string.IsNullOrEmpty(r.SkillName))
            .GroupBy(r => r.SkillName);

        Directory.CreateDirectory(WorkspaceSkillsPath);

        foreach (var skillGroup in resourcesBySkill)
        {
            var embeddedSkillName = skillGroup.Key;
            var skillName = ReadBuiltInSkillName(assembly, skillGroup) ?? embeddedSkillName;
            var skillDir = Path.Combine(WorkspaceSkillsPath, skillName);
            var markerPath = Path.Combine(skillDir, markerFile);
            MigrateLegacyBuiltInSkillDir(embeddedSkillName, skillName, markerFile);

            // If the skill directory exists but has no .builtin marker, the user owns it
            if (Directory.Exists(skillDir) && !File.Exists(markerPath))
                continue;

            // If the skill is already at the current version, skip it
            if (File.Exists(markerPath) && File.ReadAllText(markerPath).Trim() == currentVersion)
                continue;

            Directory.CreateDirectory(skillDir);

            foreach (var resource in skillGroup)
            {
                using var stream = assembly.GetManifestResourceStream(resource.ResourceName);
                if (stream == null)
                    continue;

                var targetPath = Path.Combine(skillDir, NormalizeBuiltInResourceFileName(resource.FileName));
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                using var file = File.Create(targetPath);
                stream.CopyTo(file);
            }

            File.WriteAllText(markerPath, currentVersion);
        }
    }

    private static string? ReadBuiltInSkillName(
        Assembly assembly,
        IEnumerable<(string SkillName, string FileName, string ResourceName)> resources)
    {
        var skillResource = resources.FirstOrDefault(
            resource => string.Equals(resource.FileName, "SKILL.md", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(skillResource.ResourceName))
            return null;

        using var stream = assembly.GetManifestResourceStream(skillResource.ResourceName);
        if (stream == null)
            return null;

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var content = reader.ReadToEnd();
        return ReadFrontmatterValue(content, "name");
    }

    private static string NormalizeBuiltInResourceFileName(string fileName)
    {
        if (fileName.StartsWith("agents.", StringComparison.Ordinal))
            return Path.Combine("agents", fileName["agents.".Length..]);
        if (fileName.StartsWith("assets.", StringComparison.Ordinal))
            return Path.Combine("assets", fileName["assets.".Length..]);
        if (fileName.StartsWith("scripts.", StringComparison.Ordinal))
            return Path.Combine("scripts", fileName["scripts.".Length..]);
        if (fileName.StartsWith("references.", StringComparison.Ordinal))
            return Path.Combine("references", fileName["references.".Length..]);
        return fileName;
    }

    private void MigrateLegacyBuiltInSkillDir(string legacyName, string canonicalName, string markerFile)
    {
        if (string.Equals(legacyName, canonicalName, StringComparison.Ordinal))
            return;

        var legacyDir = Path.Combine(WorkspaceSkillsPath, legacyName);
        if (!Directory.Exists(legacyDir) || !File.Exists(Path.Combine(legacyDir, markerFile)))
            return;

        var canonicalDir = Path.Combine(WorkspaceSkillsPath, canonicalName);
        if (!Directory.Exists(canonicalDir))
        {
            Directory.Move(legacyDir, canonicalDir);
            return;
        }

        // Legacy built-ins are generated artifacts. Keep user-owned canonical skills intact.
        Directory.Delete(legacyDir, recursive: true);
    }

    /// <summary>
    /// Build a summary of all skills (for progressive loading).
    /// The agent can read the full skill content using ReadFile when needed.
    /// Shows availability status and missing requirements for unavailable skills.
    /// </summary>
    public string BuildSkillsSummary(IReadOnlyCollection<string>? availableToolNames = null)
    {
        return BuildSkillsSummary(availableToolNames, variantModeEnabled: false, target: null);
    }

    /// <summary>
    /// Build a summary of all skills with effective locations when variants are enabled.
    /// </summary>
    public string BuildSkillsSummary(
        IReadOnlyCollection<string>? availableToolNames,
        bool variantModeEnabled,
        SkillVariantTarget? target)
    {
        var allSkills = ListSkills(filterUnavailable: false);
        if (allSkills.Count == 0)
            return string.Empty;

        var toolSet = availableToolNames == null
            ? null
            : new HashSet<string>(availableToolNames, StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        sb.AppendLine("<skills>");

        foreach (var skill in allSkills)
        {
            if (!skill.Enabled)
                continue;

            var description = GetSkillDescription(skill.Name);
            var metadata = GetSkillMetadata(skill.Name);
            var alwaysLoad = metadata?.GetValueOrDefault("always", "false").ToLowerInvariant() == "true";
            var available = skill.Available;
            var unavailableReason = skill.UnavailableReason;
            if (toolSet != null && skill.Requirements?.Tools.Count > 0)
            {
                var missingTools = skill.Requirements.Tools
                    .Where(t => !toolSet.Contains(t))
                    .ToArray();
                if (missingTools.Length > 0)
                {
                    available = false;
                    unavailableReason = "Missing tools: " + string.Join(", ", missingTools);
                }
            }

            sb.AppendLine(
                $"  <skill available=\"{available.ToString().ToLower()}\" always=\"{alwaysLoad.ToString().ToLower()}\">");
            sb.AppendLine($"    <name>{EscapeXml(skill.Name)}</name>");
            sb.AppendLine($"    <description>{EscapeXml(description)}</description>");
            sb.AppendLine($"    <location>{EscapeXml(ResolveEffectiveSkillFile(skill, variantModeEnabled, target) ?? skill.Path)}</location>");

            // Show missing requirements for unavailable skills
            if (!available && unavailableReason != null)
            {
                sb.AppendLine($"    <requires>{EscapeXml(unavailableReason)}</requires>");
            }

            sb.AppendLine("  </skill>");
        }

        sb.AppendLine("</skills>");
        return sb.ToString();
    }

    /// <summary>
    /// Get skills marked as always=true.
    /// </summary>
    public List<string> GetAlwaysSkills(IReadOnlyCollection<string>? availableToolNames = null)
    {
        var result = new List<string>();
        var toolSet = availableToolNames == null
            ? null
            : new HashSet<string>(availableToolNames, StringComparer.OrdinalIgnoreCase);

        foreach (var skill in ListSkills())
        {
            if (!skill.Enabled)
                continue;

            if (toolSet != null && skill.Requirements?.Tools.Count > 0)
            {
                var missingTool = skill.Requirements.Tools.Any(t => !toolSet.Contains(t));
                if (missingTool)
                    continue;
            }

            var metadata = GetSkillMetadata(skill.Name);
            if (metadata != null && metadata.GetValueOrDefault("always", "false").ToLowerInvariant() == "true")
            {
                result.Add(skill.Name);
            }
        }

        return result;
    }

    /// <summary>
    /// Get skill metadata from frontmatter (simple YAML parsing).
    /// </summary>
    public Dictionary<string, string>? GetSkillMetadata(string name)
    {
        var skillFile = ResolveSkillFileDirect(name, out _);
        if (skillFile == null)
            return null;

        return GetSkillMetadataFromFile(skillFile);
    }

    /// <summary>
    /// Reads frontmatter metadata from a specific <c>SKILL.md</c> file.
    /// </summary>
    public static Dictionary<string, string>? GetSkillMetadataFromFile(string skillFile)
    {
        var content = File.ReadAllText(skillFile, Encoding.UTF8);
        if (content == null || !content.StartsWith("---"))
            return null;

        var match = Regex.Match(content, @"^---\r?\n(.*?)\r?\n---", RegexOptions.Singleline);
        if (!match.Success)
            return null;

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in match.Groups[1].Value.Split('\n'))
        {
            if (!line.Contains(':'))
                continue;

            var parts = line.Split(':', 2);
            if (parts.Length == 2)
            {
                var key = parts[0].Trim();
                var value = parts[1].Trim().Trim('"', '\'');
                metadata[key] = value;
            }
        }

        return metadata;
    }

    private string? ResolveSkillFileDirect(string name, out string? pluginRoot)
    {
        pluginRoot = null;
        var workspaceSkill = Path.Combine(WorkspaceSkillsPath, name, "SKILL.md");
        if (File.Exists(workspaceSkill) && !File.Exists(Path.Combine(WorkspaceSkillsPath, name, ".builtin")))
            return workspaceSkill;

        foreach (var source in _pluginSkillSources)
        {
            var pluginSkill = Path.Combine(source.SkillsPath, name, "SKILL.md");
            if (File.Exists(pluginSkill))
            {
                pluginRoot = source.PluginRoot;
                return pluginSkill;
            }
        }

        if (File.Exists(workspaceSkill) && !_disabledPluginSkillNames.Contains(name))
            return workspaceSkill;

        if (UserSkillsPath is not null)
        {
            var userSkill = Path.Combine(UserSkillsPath, name, "SKILL.md");
            if (File.Exists(userSkill))
                return userSkill;
        }

        return null;
    }

    /// <summary>
    /// Resolves a source skill descriptor by name.
    /// </summary>
    public SkillInfo? ResolveSkillInfo(string name) =>
        ListSkills(filterUnavailable: false)
            .FirstOrDefault(skill => string.Equals(skill.Name, name, StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, string> ParseOpenAiInterfaceManifest(string content)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inInterface = false;

        foreach (var rawLine in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                continue;

            var trimmed = rawLine.Trim();
            if (trimmed.StartsWith('#'))
                continue;

            if (!char.IsWhiteSpace(rawLine[0]))
            {
                inInterface = string.Equals(trimmed, "interface:", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inInterface)
                continue;

            var separator = trimmed.IndexOf(':');
            if (separator <= 0)
                continue;

            var key = trimmed[..separator].Trim();
            var value = trimmed[(separator + 1)..].Trim().Trim('"', '\'');
            metadata[key] = value;
        }

        return metadata;
    }

    private static string? TryReadIconDataUrl(string skillDir, string? relativePath, string? pluginRoot)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        try
        {
            var normalizedRelativePath = relativePath
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalizedRelativePath))
                return null;

            var hasParentTraversal = normalizedRelativePath
                .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment == "..");
            var allowedRoot = hasParentTraversal
                ? pluginRoot == null ? null : Path.Combine(pluginRoot, "assets")
                : Path.Combine(skillDir, "assets");
            if (allowedRoot == null)
                return null;

            var fullPath = Path.GetFullPath(Path.Combine(skillDir, normalizedRelativePath));
            if (!IsPathInsideDirectory(fullPath, allowedRoot))
                return null;
            if (!File.Exists(fullPath))
                return null;

            var mimeType = Path.GetExtension(fullPath).ToLowerInvariant() switch
            {
                ".svg" => "image/svg+xml",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                _ => null
            };
            if (mimeType == null)
                return null;

            var info = new FileInfo(fullPath);
            if (info.Length <= 0 || info.Length > MaxIconBytes)
                return null;

            var bytes = File.ReadAllBytes(fullPath);
            return $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
        }
        catch
        {
            return null;
        }
    }

    private static bool IsPathInsideDirectory(string path, string directory)
    {
        var normalizedPath = Path.GetFullPath(path);
        var normalizedDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return normalizedPath.Equals(normalizedDirectory, comparison)
            || normalizedPath.StartsWith(normalizedDirectory + Path.DirectorySeparatorChar, comparison);
    }

    private static string? ReadFrontmatterValue(string content, string key)
    {
        if (!content.StartsWith("---"))
            return null;

        var match = Regex.Match(content, @"^---\r?\n(.*?)\r?\n---", RegexOptions.Singleline);
        if (!match.Success)
            return null;

        foreach (var line in match.Groups[1].Value.Split('\n'))
        {
            if (!line.Contains(':'))
                continue;

            var parts = line.Split(':', 2);
            if (parts.Length == 2 && string.Equals(parts[0].Trim(), key, StringComparison.OrdinalIgnoreCase))
                return parts[1].Trim().Trim('"', '\'');
        }

        return null;
    }

    /// <summary>
    /// Human-readable description from frontmatter, or the skill name.
    /// </summary>
    public string GetSkillDescription(string name)
    {
        var metadata = GetSkillMetadata(name);
        return metadata?.GetValueOrDefault("description", name) ?? name;
    }

    /// <summary>
    /// Reads the human-readable description from a specific <c>SKILL.md</c> file, or returns null.
    /// </summary>
    public static string? GetSkillDescriptionFromFile(string skillFile)
    {
        var metadata = GetSkillMetadataFromFile(skillFile);
        return metadata?.GetValueOrDefault("description");
    }

    public static string StripFrontmatter(string content)
    {
        if (!content.StartsWith("---"))
            return content;

        var match = Regex.Match(content, @"^---\r?\n.*?\r?\n---\r?\n", RegexOptions.Singleline);
        return match.Success ? content[match.Length..].Trim() : content;
    }

    private static string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    public sealed class SkillInfo
    {
        public string Name { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        public string Source { get; set; } = string.Empty;

        public string? PluginId { get; set; }

        public string? PluginDisplayName { get; set; }

        /// <summary>
        /// Whether the skill is available (all requirements met).
        /// </summary>
        public bool Available { get; set; } = true;

        /// <summary>
        /// Reason why the skill is unavailable (if applicable).
        /// </summary>
        public string? UnavailableReason { get; set; }

        /// <summary>
        /// Skill requirements (bins, env vars).
        /// </summary>
        public SkillRequirements? Requirements { get; set; }

        /// <summary>
        /// When false, the skill is disabled via workspace config and omitted from agent context.
        /// </summary>
        public bool Enabled { get; set; } = true;
    }

    /// <summary>Identifies a plugin-contained skill directory and its owning plugin root.</summary>
    /// <param name="PluginId">Canonical plugin id.</param>
    /// <param name="PluginDisplayName">User-facing plugin name.</param>
    /// <param name="SkillsPath">Validated plugin skill directory.</param>
    /// <param name="PluginRoot">Validated owning plugin root.</param>
    public sealed record PluginSkillSource(
        string PluginId,
        string PluginDisplayName,
        string SkillsPath,
        string PluginRoot);

    public sealed class SkillInterfaceInfo
    {
        public string? DisplayName { get; set; }

        public string? ShortDescription { get; set; }

        public string? IconSmallDataUrl { get; set; }

        public string? IconLargeDataUrl { get; set; }

        public string? DefaultPrompt { get; set; }
    }

    /// <summary>
    /// Represents skill requirements.
    /// </summary>
    public sealed class SkillRequirements
    {
        /// <summary>
        /// Required executables/bins.
        /// </summary>
        public List<string> Bins { get; set; } = [];

        /// <summary>
        /// Required environment variables.
        /// </summary>
        public List<string> Env { get; set; } = [];

        /// <summary>
        /// Required agent tools.
        /// </summary>
        public List<string> Tools { get; set; } = [];
    }

    /// <summary>
    /// Check if skill requirements are met (bins, env vars).
    /// </summary>
    /// <param name="requires">Requirements to check.</param>
    /// <param name="reason">Output parameter for missing requirements description.</param>
    /// <returns>True if all requirements are met, false otherwise.</returns>
    private static bool CheckRequirements(SkillRequirements? requires, out string? reason)
    {
        reason = null;
        if (requires == null || (requires.Bins.Count == 0 && requires.Env.Count == 0))
            return true;

        var missing = new List<string>();

        // Check required executables
        foreach (var bin in requires.Bins)
        {
            if (!IsCommandAvailable(bin))
                missing.Add($"Executable: {bin}");
        }

        // Check required environment variables
        foreach (var env in requires.Env)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(env)))
                missing.Add($"Environment variable: {env}");
        }

        if (missing.Count > 0)
        {
            reason = "Missing requirements: " + string.Join(", ", missing);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Check if a command is available in PATH.
    /// </summary>
    /// <param name="command">The command to check.</param>
    /// <returns>True if command is available, false otherwise.</returns>
    private static bool IsCommandAvailable(string command)
    {
        try
        {
            var isWindows = OperatingSystem.IsWindows();
            var checkCommand = isWindows ? $"where {command}" : $"which {command}";
            var shell = isWindows ? "cmd.exe" : "/bin/bash";
            var shellArg = isWindows ? "/c" : "-c";

            var psi = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = $"{shellArg} \"{checkCommand}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = Process.Start(psi);
            if (process == null)
                return false;

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parse skill requirements from metadata.
    /// </summary>
    /// <param name="metadata">Skill metadata dictionary.</param>
    /// <returns>SkillRequirements object, or null if no requirements.</returns>
    private static SkillRequirements? GetSkillRequirements(Dictionary<string, string>? metadata)
    {
        if (metadata == null)
            return null;

        // Check for 'requires' field with bins and env
        var hasRequirements = metadata.ContainsKey("bins") || metadata.ContainsKey("env") || metadata.ContainsKey("tools");

        if (!hasRequirements)
            return null;

        var requirements = new SkillRequirements();

        // Parse bins (comma-separated)
        if (metadata.TryGetValue("bins", out var binsStr))
        {
            requirements.Bins.AddRange(binsStr.Split(',')
                .Select(b => b.Trim())
                .Where(b => !string.IsNullOrEmpty(b)));
        }

        // Parse env (comma-separated)
        if (metadata.TryGetValue("env", out var envStr))
        {
            requirements.Env.AddRange(envStr.Split(',')
                .Select(e => e.Trim())
                .Where(e => !string.IsNullOrEmpty(e)));
        }

        // Parse tools (comma-separated). Tool requirements are evaluated against
        // the per-agent tool list when building the prompt.
        if (metadata.TryGetValue("tools", out var toolsStr))
        {
            requirements.Tools.AddRange(toolsStr.Split(',')
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t)));
        }

        return requirements.Bins.Count == 0 && requirements.Env.Count == 0 && requirements.Tools.Count == 0
            ? null
            : requirements;
    }
}
