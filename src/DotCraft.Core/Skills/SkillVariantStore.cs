using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DotCraft.Skills;

/// <summary>
/// Stores and resolves workspace-local skill variants.
/// </summary>
public sealed class SkillVariantStore(string craftPath)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _variantsRoot = Path.Combine(craftPath, "skill-variants");

    /// <summary>
    /// Returns the storage root for skill variants.
    /// </summary>
    public string VariantsRoot => _variantsRoot;

    /// <summary>
    /// Computes a stable fingerprint for a source skill bundle.
    /// </summary>
    public static string ComputeSourceFingerprint(string skillFilePath)
    {
        var skillDir = Path.GetDirectoryName(skillFilePath)
            ?? throw new InvalidOperationException("Skill file path has no parent directory.");
        var root = Path.GetFullPath(skillDir);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !ShouldIgnoreFingerprintFile(Path.GetFileName(path)))
            .Select(path => Path.GetFullPath(path))
            .OrderBy(path => Path.GetRelativePath(root, path).Replace('\\', '/'), StringComparer.Ordinal)
            .ToArray();

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var relativeBytes = Encoding.UTF8.GetBytes(relative);
            hasher.AppendData(relativeBytes);
            hasher.AppendData([0]);
            hasher.AppendData(File.ReadAllBytes(file));
            hasher.AppendData([0]);
        }

        return "sha256:" + Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>
    /// Builds a target signature for variant compatibility.
    /// </summary>
    public static SkillVariantTarget CreateTarget(
        string? model,
        string? workspacePath,
        bool sandboxEnabled,
        string? approvalPolicy,
        IReadOnlyCollection<string>? toolNames)
    {
        var os = OperatingSystem.IsWindows()
            ? "windows"
            : OperatingSystem.IsMacOS()
                ? "macos"
                : "linux";
        var shell = OperatingSystem.IsWindows() ? "powershell" : "bash";
        var normalizedTools = toolNames == null
            ? string.Empty
            : string.Join('\n', toolNames.Order(StringComparer.OrdinalIgnoreCase));

        return new SkillVariantTarget
        {
            Harness = "dotcraft",
            HarnessVersion = typeof(SkillVariantStore).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            Model = model?.Trim() ?? string.Empty,
            Os = os,
            Shell = shell,
            Sandbox = sandboxEnabled ? "sandbox" : "host",
            ApprovalPolicy = approvalPolicy?.Trim() ?? string.Empty,
            WorkspaceHash = HashText(Path.GetFullPath(workspacePath ?? string.Empty).ToLowerInvariant()),
            ToolProfileHash = HashText(normalizedTools)
        };
    }

    /// <summary>
    /// Returns the current variant for a source skill, if one is compatible.
    /// </summary>
    public SkillVariantManifest? FindCurrentVariant(
        SkillsLoader.SkillInfo source,
        string sourceFingerprint,
        SkillVariantTarget target)
    {
        var sourceKey = GetSourceKey(source);
        var sourceDir = Path.Combine(_variantsRoot, sourceKey);
        if (!Directory.Exists(sourceDir))
            return null;

        SkillVariantManifest? current = null;
        foreach (var manifestPath in Directory.EnumerateFiles(sourceDir, "manifest.json", SearchOption.AllDirectories))
        {
            var manifest = TryReadManifest(manifestPath);
            if (manifest == null)
                continue;

            if (!string.Equals(manifest.Source.Fingerprint, sourceFingerprint, StringComparison.Ordinal))
            {
                if (string.Equals(manifest.Status, SkillVariantStatus.Current, StringComparison.OrdinalIgnoreCase))
                    MarkStatus(manifest, manifestPath, SkillVariantStatus.Stale);
                continue;
            }

            if (!string.Equals(manifest.Status, SkillVariantStatus.Current, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!IsTargetCompatible(manifest.Target, target))
                continue;

            current = manifest;
        }

        return current;
    }

    /// <summary>
    /// Creates a mutable variant snapshot from a source skill, or returns the existing current variant.
    /// </summary>
    public SkillVariantManifest EnsureCurrentVariant(
        SkillsLoader.SkillInfo source,
        string sourceFingerprint,
        SkillVariantTarget target)
    {
        var existing = FindCurrentVariant(source, sourceFingerprint, target);
        if (existing != null)
            return existing;

        var sourceKey = GetSourceKey(source);
        var variantId = "var_" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss") + "_" + Guid.NewGuid().ToString("N")[..8];
        var variantDir = Path.Combine(_variantsRoot, sourceKey, variantId);
        var variantSkillDir = Path.Combine(variantDir, "skill");
        CopyDirectory(Path.GetDirectoryName(source.Path)!, variantSkillDir);

        var now = DateTimeOffset.UtcNow;
        var manifest = new SkillVariantManifest
        {
            VariantId = variantId,
            Source = new SkillVariantSource
            {
                Name = source.Name,
                SourceKind = source.Source,
                Path = source.Path,
                Fingerprint = sourceFingerprint
            },
            Target = target,
            Status = SkillVariantStatus.Current,
            CreatedAt = now,
            UpdatedAt = now,
            Provenance = new SkillVariantProvenance { Kind = "selfLearning" },
            Summary = "Workspace adaptation created by SkillManage."
        };
        WriteManifest(manifest, GetManifestPath(manifest));
        return manifest;
    }

    /// <summary>
    /// Marks the current variant as restored, if one exists.
    /// </summary>
    public bool RestoreOriginal(SkillsLoader.SkillInfo source, string sourceFingerprint, SkillVariantTarget target)
    {
        var current = FindCurrentVariant(source, sourceFingerprint, target);
        if (current == null)
            return false;

        var manifestPath = GetManifestPath(current);
        MarkStatus(current, manifestPath, SkillVariantStatus.Restored);
        return true;
    }

    /// <summary>
    /// Deletes all stored variants associated with a source skill.
    /// </summary>
    public int DeleteVariantsForSource(SkillsLoader.SkillInfo source)
    {
        var sourceDir = Path.Combine(_variantsRoot, GetSourceKey(source));
        if (!Directory.Exists(sourceDir))
            return 0;

        var count = Directory.EnumerateFiles(sourceDir, "manifest.json", SearchOption.AllDirectories).Count();
        Directory.Delete(sourceDir, recursive: true);
        return count;
    }

    /// <summary>
    /// Returns the skill directory for a variant.
    /// </summary>
    public string GetVariantSkillDir(SkillVariantManifest manifest) =>
        Path.Combine(_variantsRoot, GetSourceKey(manifest.Source.SourceKind, manifest.Source.Name), manifest.VariantId, "skill");

    /// <summary>
    /// Updates timestamps after mutating a variant.
    /// </summary>
    public void Touch(SkillVariantManifest manifest)
    {
        manifest.UpdatedAt = DateTimeOffset.UtcNow;
        WriteManifest(manifest, GetManifestPath(manifest));
    }

    internal static string GetSourceKey(SkillsLoader.SkillInfo source) =>
        GetSourceKey(source.Source, source.Name);

    private static string GetSourceKey(string sourceKind, string name) =>
        $"{SanitizeKeyPart(sourceKind)}.{SanitizeKeyPart(name)}";

    private string GetManifestPath(SkillVariantManifest manifest) =>
        Path.Combine(_variantsRoot, GetSourceKey(manifest.Source.SourceKind, manifest.Source.Name), manifest.VariantId, "manifest.json");

    private static bool IsTargetCompatible(SkillVariantTarget variant, SkillVariantTarget current) =>
        string.Equals(variant.Model, current.Model, StringComparison.OrdinalIgnoreCase)
        && string.Equals(variant.Os, current.Os, StringComparison.OrdinalIgnoreCase)
        && string.Equals(variant.Shell, current.Shell, StringComparison.OrdinalIgnoreCase)
        && string.Equals(variant.Sandbox, current.Sandbox, StringComparison.OrdinalIgnoreCase)
        && string.Equals(variant.WorkspaceHash, current.WorkspaceHash, StringComparison.Ordinal);

    private static SkillVariantManifest? TryReadManifest(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<SkillVariantManifest>(File.ReadAllText(path, Encoding.UTF8), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteManifest(SkillVariantManifest manifest, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        File.WriteAllText(path, json + Environment.NewLine, Encoding.UTF8);
    }

    private static void MarkStatus(SkillVariantManifest manifest, string manifestPath, string status)
    {
        manifest.Status = status;
        manifest.UpdatedAt = DateTimeOffset.UtcNow;
        WriteManifest(manifest, manifestPath);
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var directory in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(targetDir, Path.GetRelativePath(sourceDir, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(targetDir, Path.GetRelativePath(sourceDir, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static bool ShouldIgnoreFingerprintFile(string fileName) =>
        string.Equals(fileName, ".builtin", StringComparison.OrdinalIgnoreCase)
        || fileName.StartsWith(".", StringComparison.Ordinal);

    private static string SanitizeKeyPart(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant())
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-');
        }

        return sb.Length == 0 ? "unknown" : sb.ToString();
    }

    private static string HashText(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
