using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotCraft.Plugins;

/// <summary>Statically admitted .NET entry metadata from a plugin manifest.</summary>
public sealed record PluginDotnetManifest
{
    /// <summary>Gets the minimum DotCraft Host version this plugin runs on, canonical <c>MAJOR.MINOR.PATCH</c>.</summary>
    public required string MinHostVersion { get; init; }

    /// <summary>Gets the confined <c>./</c>-relative path of the entry assembly.</summary>
    public required string EntryAssembly { get; init; }

    /// <summary>Gets the full name of the type implementing <see cref="IDotCraftPlugin"/>.</summary>
    public required string EntryType { get; init; }

    /// <summary>Gets the confined <c>./</c>-relative paths of assemblies exported to consumers.</summary>
    public IReadOnlyList<string> ExportedApiAssemblies { get; init; } = [];
}

internal sealed record PluginDotnetAdmissionResult(
    string? Version,
    PluginDotnetManifest? Dotnet,
    IReadOnlyDictionary<string, string> Dependencies,
    IReadOnlyList<PluginDiagnostic> Diagnostics,
    bool DotnetDeclared);

internal static partial class PluginDotnetManifestAdmission
{
    private const string AdmissionCode = PluginDotnetDiagnosticCodes.AdmissionFailed;

    public static PluginDotnetAdmissionResult Admit(
        string pluginRoot,
        string pluginId,
        JsonElement versionElement,
        JsonElement dotnetElement,
        JsonElement dependenciesElement)
    {
        var diagnostics = new List<PluginDiagnostic>();
        var dotnetDeclared = dotnetElement.ValueKind != JsonValueKind.Undefined;
        var version = ReadVersion(versionElement, dotnetDeclared, pluginId, diagnostics);

        if (!dotnetDeclared)
        {
            if (dependenciesElement.ValueKind != JsonValueKind.Undefined)
                AddAdmissionFailure(diagnostics, pluginId, "dependencies", "dotnetRequired");

            return new PluginDotnetAdmissionResult(
                version,
                null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                diagnostics,
                false);
        }

        PluginDotnetManifest? dotnet = null;
        if (dotnetElement.ValueKind != JsonValueKind.Object)
        {
            AddAdmissionFailure(diagnostics, pluginId, "dotnet", "invalidType");
        }
        else
        {
            dotnet = ReadDotnet(pluginRoot, pluginId, dotnetElement, diagnostics);
        }

        var dependencies = ReadDependencies(pluginId, dependenciesElement, diagnostics);
        if (diagnostics.Count > 0 || dotnet == null || version == null)
        {
            return new PluginDotnetAdmissionResult(
                version,
                null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                diagnostics,
                true);
        }

        return new PluginDotnetAdmissionResult(version, dotnet, dependencies, diagnostics, true);
    }

    public static bool IsCanonicalVersion(string? value) =>
        !string.IsNullOrWhiteSpace(value) && CanonicalVersionRegex().IsMatch(value);

    /// <summary>A dependency accepts versions at or above its minimum within one compatible release line.</summary>
    /// <remarks>Stable releases share a major version. Pre-1.0 releases also share a minor version.</remarks>
    public static bool SatisfiesMinimum(string minimumVersion, string? observedVersion) =>
        IsCanonicalVersion(observedVersion)
        && Version.TryParse(observedVersion, out var observed)
        && Version.TryParse(minimumVersion, out var minimum)
        && observed >= minimum
        && observed.Major == minimum.Major
        && (minimum.Major != 0 || observed.Minor == minimum.Minor);

    private static string? ReadVersion(
        JsonElement value,
        bool required,
        string pluginId,
        List<PluginDiagnostic> diagnostics)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            if (required)
                AddAdmissionFailure(diagnostics, pluginId, "version", "missing");
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            if (required)
                AddAdmissionFailure(diagnostics, pluginId, "version", "invalidType");
            return null;
        }

        var version = value.GetString()?.Trim();
        if (required && !IsCanonicalVersion(version))
            AddAdmissionFailure(diagnostics, pluginId, "version", "invalidFormat");

        return string.IsNullOrWhiteSpace(version) ? null : version;
    }

    private static PluginDotnetManifest? ReadDotnet(
        string pluginRoot,
        string pluginId,
        JsonElement dotnet,
        List<PluginDiagnostic> diagnostics)
    {
        var minHostVersionValue = ReadSingleProperty(dotnet, "minHostVersion", out var duplicateMinHostVersion);
        if (duplicateMinHostVersion)
            AddAdmissionFailure(diagnostics, pluginId, "dotnet.minHostVersion", "duplicate");
        var minHostVersion = ReadRequiredCanonicalVersion(
            pluginId,
            minHostVersionValue,
            "dotnet.minHostVersion",
            diagnostics);

        var entryAssemblyValue = ReadSingleProperty(dotnet, "entryAssembly", out var duplicateEntryAssembly);
        if (duplicateEntryAssembly)
            AddAdmissionFailure(diagnostics, pluginId, "dotnet.entryAssembly", "duplicate");
        var entryAssembly = ReadAssemblyPath(
            pluginRoot,
            pluginId,
            entryAssemblyValue,
            "dotnet.entryAssembly",
            diagnostics);

        var entryTypeValue = ReadSingleProperty(dotnet, "entryType", out var duplicateEntryType);
        if (duplicateEntryType)
            AddAdmissionFailure(diagnostics, pluginId, "dotnet.entryType", "duplicate");
        var entryType = ReadRequiredString(
            pluginId,
            entryTypeValue,
            "dotnet.entryType",
            diagnostics);

        var exportsValue = ReadSingleProperty(dotnet, "exportedApiAssemblies", out var duplicateExports);
        if (duplicateExports)
            AddAdmissionFailure(diagnostics, pluginId, "dotnet.exportedApiAssemblies", "duplicate");
        var exportedApiAssemblies = ReadExportedApiAssemblies(
            pluginRoot,
            pluginId,
            exportsValue,
            entryAssembly,
            diagnostics);

        if (entryAssembly == null || entryType == null || minHostVersion == null)
            return null;

        return new PluginDotnetManifest
        {
            MinHostVersion = minHostVersion,
            EntryAssembly = entryAssembly,
            EntryType = entryType,
            ExportedApiAssemblies = exportedApiAssemblies
        };
    }

    private static IReadOnlyDictionary<string, string> ReadDependencies(
        string pluginId,
        JsonElement dependencies,
        List<PluginDiagnostic> diagnostics)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (dependencies.ValueKind == JsonValueKind.Undefined)
            return result;

        if (dependencies.ValueKind != JsonValueKind.Object)
        {
            AddAdmissionFailure(diagnostics, pluginId, "dependencies", "invalidType");
            return result;
        }

        foreach (var property in dependencies.EnumerateObject())
        {
            var dependencyId = property.Name.Trim();
            if (!PluginManifestParser.IsValidPluginId(dependencyId))
            {
                AddAdmissionFailure(
                    diagnostics,
                    pluginId,
                    "dependencies[]",
                    "invalidFormat",
                    dependencyId);
                continue;
            }

            if (PluginIds.EqualsCanonical(pluginId, dependencyId))
            {
                AddAdmissionFailure(
                    diagnostics,
                    pluginId,
                    "dependencies[]",
                    "selfDependency",
                    dependencyId);
                continue;
            }

            if (result.ContainsKey(dependencyId))
            {
                AddAdmissionFailure(
                    diagnostics,
                    pluginId,
                    "dependencies[]",
                    "duplicate",
                    dependencyId);
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.String)
            {
                AddAdmissionFailure(
                    diagnostics,
                    pluginId,
                    "dependencies[]",
                    "invalidType",
                    dependencyId);
                continue;
            }

            var requiredVersion = property.Value.GetString()?.Trim();
            if (!IsCanonicalVersion(requiredVersion))
            {
                AddAdmissionFailure(
                    diagnostics,
                    pluginId,
                    "dependencies[]",
                    "invalidFormat",
                    dependencyId);
                continue;
            }

            result.Add(dependencyId, requiredVersion!);
        }

        return result;
    }

    private static IReadOnlyList<string> ReadExportedApiAssemblies(
        string pluginRoot,
        string pluginId,
        JsonElement value,
        string? entryAssembly,
        List<PluginDiagnostic> diagnostics)
    {
        if (value.ValueKind == JsonValueKind.Undefined)
            return [];

        if (value.ValueKind != JsonValueKind.Array)
        {
            AddAdmissionFailure(diagnostics, pluginId, "dotnet.exportedApiAssemblies", "invalidType");
            return [];
        }

        var result = new List<string>();
        var seen = new HashSet<string>(PathComparer);
        foreach (var item in value.EnumerateArray())
        {
            var path = ReadAssemblyPath(
                pluginRoot,
                pluginId,
                item,
                "dotnet.exportedApiAssemblies",
                diagnostics);
            if (path == null)
                continue;

            if (entryAssembly != null && PathComparer.Equals(entryAssembly, path))
            {
                AddAdmissionFailure(
                    diagnostics,
                    pluginId,
                    "dotnet.exportedApiAssemblies",
                    "entryAssemblyExported");
                continue;
            }

            if (!seen.Add(path))
            {
                AddAdmissionFailure(
                    diagnostics,
                    pluginId,
                    "dotnet.exportedApiAssemblies",
                    "duplicate");
                continue;
            }

            result.Add(path);
        }

        return result;
    }

    private static string? ReadAssemblyPath(
        string pluginRoot,
        string pluginId,
        JsonElement value,
        string field,
        List<PluginDiagnostic> diagnostics)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            AddAdmissionFailure(diagnostics, pluginId, field, "missing");
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            AddAdmissionFailure(diagnostics, pluginId, field, "invalidType");
            return null;
        }

        var declaredPath = value.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(declaredPath)
            || !declaredPath.StartsWith("./", StringComparison.Ordinal)
            || !declaredPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            AddAdmissionFailure(diagnostics, pluginId, field, "invalidPath");
            return null;
        }

        var relative = declaredPath[2..];
        if (relative.Length == 0)
        {
            AddAdmissionFailure(diagnostics, pluginId, field, "invalidPath");
            return null;
        }

        if (relative.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == ".."))
        {
            AddAdmissionFailure(diagnostics, pluginId, field, "pathOutsideRoot");
            return null;
        }

        try
        {
            var root = Path.GetFullPath(pluginRoot);
            var absolute = Path.GetFullPath(Path.Combine(root, relative));
            var relativeBack = Path.GetRelativePath(root, absolute);
            if (Path.IsPathRooted(relativeBack)
                || relativeBack.Equals("..", StringComparison.Ordinal)
                || relativeBack.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || relativeBack.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            {
                AddAdmissionFailure(diagnostics, pluginId, field, "pathOutsideRoot");
                return null;
            }

            return "./" + relativeBack.Replace('\\', '/');
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            AddAdmissionFailure(diagnostics, pluginId, field, "invalidPath");
            return null;
        }
    }

    private static string? ReadRequiredString(
        string pluginId,
        JsonElement value,
        string field,
        List<PluginDiagnostic> diagnostics)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            AddAdmissionFailure(diagnostics, pluginId, field, "missing");
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            AddAdmissionFailure(diagnostics, pluginId, field, "invalidType");
            return null;
        }

        var result = value.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(result))
        {
            AddAdmissionFailure(diagnostics, pluginId, field, "invalidFormat");
            return null;
        }

        return result;
    }

    private static string? ReadRequiredCanonicalVersion(
        string pluginId,
        JsonElement value,
        string field,
        List<PluginDiagnostic> diagnostics)
    {
        var declared = ReadRequiredString(pluginId, value, field, diagnostics);
        if (declared == null)
            return null;

        if (IsCanonicalVersion(declared))
            return declared;

        AddAdmissionFailure(diagnostics, pluginId, field, "invalidFormat");
        return null;
    }

    private static JsonElement ReadSingleProperty(JsonElement obj, string name, out bool duplicate)
    {
        duplicate = false;
        var found = default(JsonElement);
        var count = 0;
        foreach (var property in obj.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;

            found = property.Value;
            count++;
        }

        duplicate = count > 1;
        return count == 0 ? default : found;
    }

    private static void AddAdmissionFailure(
        List<PluginDiagnostic> diagnostics,
        string pluginId,
        string field,
        string reasonCode,
        string? dependencyId = null)
    {
        diagnostics.Add(PluginDiagnostic.Error(
            AdmissionCode,
            $"Code plugin manifest field '{field}' failed admission: {reasonCode}.",
            pluginId,
            parameters: new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["field"] = JsonSerializer.SerializeToElement(field),
                ["reasonCode"] = JsonSerializer.SerializeToElement(reasonCode),
                ["dependencyId"] = JsonSerializer.SerializeToElement(dependencyId)
            }));
    }

    private static StringComparer PathComparer => StringComparer.OrdinalIgnoreCase;

    [GeneratedRegex("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$")]
    private static partial Regex CanonicalVersionRegex();
}
