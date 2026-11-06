using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotCraft.Plugins;

public static partial class PluginVerifier
{
    private const int MaxScannedFileBytes = 2 * 1024 * 1024;

    private static readonly HashSet<string> ExcludedEntries = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", ".builtin", "obj", ".vs", "__pycache__", ".DS_Store", "Thumbs.db"
    };

    [GeneratedRegex(@"^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$")]
    private static partial Regex SemVerRegex();

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex HyphenCaseRegex();

    [GeneratedRegex(@"token|secret|password|passwd|api[-_]?key|authorization|credential", RegexOptions.IgnoreCase)]
    private static partial Regex CredentialNameRegex();

    [GeneratedRegex(@"\b[A-Za-z]:[\\/]|(^|[\s""'=])/(Users|home|root)/|^\\\\|^~[\\/]")]
    private static partial Regex MachinePathRegex();

    [GeneratedRegex(@"ghp_[A-Za-z0-9]{36}|github_pat_[A-Za-z0-9_]{22,}|glpat-[A-Za-z0-9_-]{20,}|sk-[A-Za-z0-9_-]{32,}|xox[abprs]-[A-Za-z0-9-]{10,}|AKIA[0-9A-Z]{16}|-----BEGIN [A-Z ]*PRIVATE KEY-----")]
    private static partial Regex TokenRegex();

    private static void CheckPublication(PluginManifest manifest, Findings findings)
    {
        if (manifest.Version == null || !SemVerRegex().IsMatch(manifest.Version))
            findings.Error("PluginVersionInvalid", "version must be strict SemVer, such as 0.1.0.", manifest.ManifestPath);
        if (manifest.Id.Length > 64 || !HyphenCaseRegex().IsMatch(manifest.Id))
            findings.Error("PluginIdNotHyphenCase", "id must be lowercase hyphen-case with at most 64 characters.", manifest.ManifestPath);
        if (string.IsNullOrWhiteSpace(manifest.Interface?.WebsiteUrl))
            findings.Warning("PluginWebsiteMissing", "interface.websiteUrl is not declared.", manifest.ManifestPath);

        foreach (var entry in PublishableEntries(manifest.RootPath, findings).Where(File.Exists))
            ScanForTokens(entry, findings);

        var documents = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { manifest.ManifestPath };
        foreach (var path in new[] { manifest.McpServersPath, manifest.LspServersPath, manifest.AppsPath })
        {
            if (path != null)
                documents.Add(path);
        }
        documents.UnionWith(manifest.Hooks?.Paths ?? []);

        foreach (var document in documents.Where(File.Exists))
            ScanDescriptor(document, findings);
    }

    private static IEnumerable<string> PublishableEntries(string directory, Findings findings)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (ExcludedEntries.Contains(Path.GetFileName(entry)))
            {
                findings.Error("PluginContainsDevelopmentArtifact", "Remove this development artifact before publishing.", entry);
                continue;
            }

            if (File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint))
            {
                findings.Error("PluginContainsSymbolicLink", "A published plugin must not contain symbolic links.", entry);
                continue;
            }

            yield return entry;
            if (Directory.Exists(entry))
            {
                foreach (var child in PublishableEntries(entry, findings))
                    yield return child;
            }
        }
    }

    private static void ScanForTokens(string file, Findings findings)
    {
        if (new FileInfo(file).Length > MaxScannedFileBytes)
            return;

        var bytes = File.ReadAllBytes(file);
        if (bytes.AsSpan(0, Math.Min(bytes.Length, 4096)).IndexOf((byte)0) >= 0)
            return;

        if (TokenRegex().IsMatch(System.Text.Encoding.UTF8.GetString(bytes)))
            findings.Error("PluginContainsCredential", "File contains text that looks like a credential.", file);
    }

    private static void ScanDescriptor(string file, Findings findings)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException)
        {
            return;
        }

        using (document)
            ScanValue(document.RootElement, null, file, findings);
    }

    private static void ScanValue(JsonElement value, string? name, string file, Findings findings)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    if (property.Name is not ("envHttpHeaders" or "envVars" or "bearerTokenEnvVar"))
                        ScanValue(property.Value, property.Name, file, findings);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                    ScanValue(item, name, file, findings);
                break;
            case JsonValueKind.String:
                var text = value.GetString() ?? string.Empty;
                if (name != null
                    && CredentialNameRegex().IsMatch(name)
                    && text.Length > 0
                    && !text.Contains('$')
                    && !text.Contains('%'))
                {
                    findings.Error("PluginLiteralCredential", $"'{name}' holds a literal value. Reference an environment variable.", file);
                }

                if (MachinePathRegex().IsMatch(text))
                    findings.Error("PluginAbsolutePath", $"'{name}' contains an absolute machine path.", file);
                break;
        }
    }
}
