using System.Globalization;
using System.Text;
using System.Text.Json;
using DotCraft.Plugins;
using DotCraft.Workspaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DotCraft.Runtime;

internal sealed class DotNetPluginCompiler(DotNetPluginReferenceSet referenceSet)
{
    private const string ProjectMissingCode = "DotNetPluginProjectMissing";
    private const string SourceMissingCode = "DotNetPluginSourceMissing";
    private const string SourceLinkUnsupportedCode = "DotNetPluginSourceLinkUnsupported";
    private const string ManifestInvalidCode = "DotNetPluginAuthoringManifestInvalid";
    private const string PluginIdMismatchCode = "DotNetPluginAuthoringIdMismatch";
    private const string EntryPathInvalidCode = "DotNetPluginAuthoringEntryPathInvalid";
    private const string FileAccessFailedCode = "DotNetPluginAuthoringFileAccessFailed";

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp14);

    private static readonly CSharpCompilationOptions CompilationOptions = new(
        OutputKind.DynamicallyLinkedLibrary,
        optimizationLevel: OptimizationLevel.Release,
        platform: Platform.AnyCpu,
        nullableContextOptions: NullableContextOptions.Enable,
        deterministic: true);

    public DotNetPluginBuildPreparation Prepare(string dataPath, string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        if (!PluginManifestParser.IsValidPluginId(pluginId)
            || pluginId.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException("A portable DotCraft plugin id is required.", nameof(pluginId));
        }

        try
        {
            return PrepareCore(dataPath, pluginId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DotNetPluginBuildPreparation.Failed(
                [Error(
                    FileAccessFailedCode,
                    "The .NET plugin project could not be read or staged.",
                    pluginId,
                    phase: "filesystem")]);
        }
    }

    private DotNetPluginBuildPreparation PrepareCore(string dataPath, string pluginId)
    {
        var dataRoot = new DotCraftPathRoot(dataPath);
        var workspaceRoot = Path.GetDirectoryName(dataRoot.RootPath)
                            ?? throw new ArgumentException(
                                "The .NET plugin data root must belong to a workspace.",
                                nameof(dataPath));
        var projectRoot = dataRoot.Resolve("plugin-projects", pluginId);
        var sourceRoot = dataRoot.Resolve("plugin-projects", pluginId, "src");
        var pluginRoot = dataRoot.Resolve("plugin-projects", pluginId, "plugin");
        if (!Directory.Exists(projectRoot))
        {
            return DotNetPluginBuildPreparation.Failed(
                [Error(ProjectMissingCode, "The .NET plugin project does not exist.", pluginId)]);
        }

        var initialParse = PluginManifestParser.Load(pluginRoot);
        var manifest = initialParse.Manifest;
        if (manifest?.Dotnet == null)
        {
            var diagnostics = SanitizeDiagnostics(initialParse.Diagnostics, projectRoot, pluginRoot);
            if (diagnostics.All(static diagnostic => diagnostic.Severity != PluginDiagnosticSeverity.Error))
            {
                diagnostics =
                [
                    .. diagnostics,
                    Error(
                        ManifestInvalidCode,
                        "The authoring manifest must declare a valid dotnet entry.",
                        pluginId,
                        "plugin/.craft-plugin/plugin.json",
                        "preflight")
                ];
            }

            return DotNetPluginBuildPreparation.Failed(diagnostics);
        }

        if (!PluginIds.EqualsCanonical(manifest.Id, pluginId))
        {
            return DotNetPluginBuildPreparation.Failed(
                [Error(
                    PluginIdMismatchCode,
                    "The project directory and manifest plugin ids do not match.",
                    pluginId,
                    "plugin/.craft-plugin/plugin.json",
                    "preflight")]);
        }

        if (!manifest.Dotnet.EntryAssembly.StartsWith("./lib/", StringComparison.Ordinal))
        {
            return DotNetPluginBuildPreparation.Failed(
                [Error(
                    EntryPathInvalidCode,
                    "The authoring entry assembly must be under './lib/'.",
                    pluginId,
                    "plugin/.craft-plugin/plugin.json",
                    "preflight")]);
        }

        var sourceLink = FindSourceLink(sourceRoot);
        if (sourceLink is not null)
        {
            return DotNetPluginBuildPreparation.Failed(
                [Error(
                    SourceLinkUnsupportedCode,
                    "The .NET plugin source tree cannot contain filesystem links.",
                    pluginId,
                    sourceLink)]);
        }

        var sourceFiles = Directory.Exists(sourceRoot)
            ? Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Select(path => new SourceFile(
                    path,
                    "src/" + Path.GetRelativePath(sourceRoot, path).Replace('\\', '/')))
                .OrderBy(static file => file.RelativePath, StringComparer.Ordinal)
                .ToArray()
            : [];
        if (sourceFiles.Length == 0)
        {
            return DotNetPluginBuildPreparation.Failed(
                [Error(SourceMissingCode, "The .NET plugin project contains no C# source files.", pluginId, "src")]);
        }

        var stagingRoot = Path.Combine(projectRoot, $".plugin-stage-{Guid.NewGuid():N}");
        try
        {
            PluginBundleTree.CopyAndFingerprint(pluginRoot, stagingRoot);
            var outputRoot = Path.Combine(stagingRoot, "lib");
            if (Directory.Exists(outputRoot))
                Directory.Delete(outputRoot, recursive: true);
            Directory.CreateDirectory(outputRoot);

            var entryRelativePath = manifest.Dotnet.EntryAssembly[2..]
                .Replace('/', Path.DirectorySeparatorChar);
            var entryPath = Path.Combine(stagingRoot, entryRelativePath);
            var dependencyPath = Path.ChangeExtension(entryPath, ".deps.json");
            Directory.CreateDirectory(Path.GetDirectoryName(entryPath)!);

            var syntaxTrees = sourceFiles
                .Select(static file => CSharpSyntaxTree.ParseText(
                    SourceText.From(File.ReadAllText(file.AbsolutePath), Encoding.UTF8, SourceHashAlgorithm.Sha256),
                    ParseOptions,
                    file.RelativePath))
                .Append(CSharpSyntaxTree.ParseText(
                    SourceText.From(
                        "[assembly: System.Runtime.Versioning.TargetFramework(\".NETCoreApp,Version=v10.0\")]",
                        Encoding.UTF8,
                        SourceHashAlgorithm.Sha256),
                    ParseOptions,
                    "generated/TargetFramework.g.cs"))
                .ToArray();
            var assemblyName = Path.GetFileNameWithoutExtension(entryPath);
            var compilation = CSharpCompilation.Create(
                assemblyName,
                syntaxTrees,
                referenceSet.References,
                CompilationOptions);

            using var assemblyStream = new MemoryStream();
            var emit = compilation.Emit(assemblyStream);
            var compileDiagnostics = emit.Diagnostics
                .Where(static diagnostic => diagnostic.Severity != DiagnosticSeverity.Hidden)
                .Select(diagnostic => ToPluginDiagnostic(diagnostic, pluginId, referenceSet.References))
                .ToArray();
            if (!emit.Success)
            {
                TryDeleteStaging(stagingRoot);
                return DotNetPluginBuildPreparation.Failed(compileDiagnostics);
            }

            File.WriteAllBytes(entryPath, assemblyStream.ToArray());
            WriteDependencyManifest(
                dependencyPath,
                assemblyName,
                manifest.Version!,
                Path.GetFileName(entryPath));

            var preflight = PluginManifestParser.Load(stagingRoot);
            var preflightDiagnostics = SanitizeDiagnostics(preflight.Diagnostics, projectRoot, stagingRoot);
            var diagnostics = compileDiagnostics.Concat(preflightDiagnostics).ToArray();
            if (preflight.Manifest == null
                || diagnostics.Any(static diagnostic => diagnostic.Severity == PluginDiagnosticSeverity.Error))
            {
                TryDeleteStaging(stagingRoot);
                return DotNetPluginBuildPreparation.Failed(diagnostics);
            }

            var fingerprint = PluginBundleFingerprint.Compute(stagingRoot);
            return DotNetPluginBuildPreparation.Successful(
                stagingRoot,
                fingerprint,
                pluginRoot,
                workspaceRoot,
                diagnostics);
        }
        catch
        {
            TryDeleteStaging(stagingRoot);
            throw;
        }
    }

    private static IReadOnlyList<PluginDiagnostic> SanitizeDiagnostics(
        IEnumerable<PluginDiagnostic> diagnostics,
        string projectRoot,
        string pluginRoot)
    {
        return diagnostics.Select(diagnostic =>
        {
            var parameters = new Dictionary<string, JsonElement>(diagnostic.Parameters, StringComparer.Ordinal)
            {
                ["phase"] = JsonSerializer.SerializeToElement("preflight")
            };
            return diagnostic with
            {
                Message = SanitizeMessage(diagnostic.Message, projectRoot, pluginRoot),
                Path = SanitizePath(diagnostic.Path, projectRoot, pluginRoot),
                Parameters = parameters
            };
        }).ToArray();
    }

    private static string SanitizeMessage(string message, string projectRoot, string pluginRoot) =>
        message
            .Replace(pluginRoot, "plugin", StringComparison.OrdinalIgnoreCase)
            .Replace(projectRoot, ".", StringComparison.OrdinalIgnoreCase);

    private static string? SanitizePath(string? path, string projectRoot, string pluginRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
            return path?.Replace('\\', '/');

        var relativeToPlugin = Path.GetRelativePath(pluginRoot, path);
        if (IsConfined(relativeToPlugin))
            return "plugin/" + relativeToPlugin.Replace('\\', '/');

        var relativeToProject = Path.GetRelativePath(projectRoot, path);
        return IsConfined(relativeToProject) ? relativeToProject.Replace('\\', '/') : null;
    }

    private static bool IsConfined(string relative) =>
        !Path.IsPathRooted(relative)
        && !relative.Equals("..", StringComparison.Ordinal)
        && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);

    private static string? FindSourceLink(string sourceRoot)
    {
        if (!Directory.Exists(sourceRoot))
            return null;
        if ((File.GetAttributes(sourceRoot) & FileAttributes.ReparsePoint) != 0)
            return "src";

        var pending = new Stack<string>();
        pending.Push(sourceRoot);
        while (pending.TryPop(out var directory))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory)
                         .OrderBy(static path => path, StringComparer.Ordinal))
            {
                var attributes = File.GetAttributes(path);
                var relative = "src/" + Path.GetRelativePath(sourceRoot, path).Replace('\\', '/');
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    return relative;
                if ((attributes & FileAttributes.Directory) != 0)
                    pending.Push(path);
            }
        }

        return null;
    }

    private static PluginDiagnostic ToPluginDiagnostic(
        Diagnostic diagnostic,
        string pluginId,
        IReadOnlyList<MetadataReference> references)
    {
        var path = diagnostic.Location.IsInSource
            ? diagnostic.Location.SourceTree?.FilePath.Replace('\\', '/')
            : null;
        var parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["phase"] = JsonSerializer.SerializeToElement("compile")
        };
        if (diagnostic.Location.IsInSource)
        {
            var line = diagnostic.Location.GetLineSpan().StartLinePosition;
            parameters["line"] = JsonSerializer.SerializeToElement(line.Line + 1);
            parameters["column"] = JsonSerializer.SerializeToElement(line.Character + 1);
        }

        return new PluginDiagnostic
        {
            Severity = diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => PluginDiagnosticSeverity.Error,
                DiagnosticSeverity.Warning => PluginDiagnosticSeverity.Warning,
                _ => PluginDiagnosticSeverity.Info
            },
            Code = diagnostic.Id,
            Message = SanitizeReferencePaths(
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                references),
            PluginId = pluginId,
            Path = path,
            Parameters = parameters
        };
    }

    private static void WriteDependencyManifest(
        string path,
        string assemblyName,
        string version,
        string fileName)
    {
        var libraryId = $"{assemblyName}/{version}";
        var document = new
        {
            runtimeTarget = new { name = ".NETCoreApp,Version=v10.0", signature = "" },
            compilationOptions = new { },
            targets = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [".NETCoreApp,Version=v10.0"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    [libraryId] = new
                    {
                        runtime = new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            [fileName] = new { }
                        }
                    }
                }
            },
            libraries = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [libraryId] = new { type = "project", serviceable = false, sha512 = "" }
            }
        };
        File.WriteAllText(path, JsonSerializer.Serialize(document));
    }

    private static string SanitizeReferencePaths(
        string message,
        IReadOnlyList<MetadataReference> references)
    {
        foreach (var reference in references)
        {
            if (string.IsNullOrWhiteSpace(reference.Display) || !Path.IsPathRooted(reference.Display))
                continue;
            message = message.Replace(
                reference.Display,
                Path.GetFileName(reference.Display),
                StringComparison.OrdinalIgnoreCase);
        }

        return message;
    }

    private static PluginDiagnostic Error(
        string code,
        string message,
        string pluginId,
        string? path = null,
        string phase = "compile") =>
        PluginDiagnostic.Error(
            code,
            message,
            pluginId,
            path: path,
            parameters: new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["phase"] = JsonSerializer.SerializeToElement(phase)
            });

    internal static void TryDeleteStaging(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record SourceFile(string AbsolutePath, string RelativePath);
}

internal sealed class DotNetPluginBuildPreparation : IDisposable
{
    private DotNetPluginBuildPreparation(
        string? bundlePath,
        string? fingerprint,
        string? projectPluginRoot,
        string? projectWorkspaceRoot,
        bool succeeded,
        IReadOnlyList<PluginDiagnostic> diagnostics)
    {
        BundlePath = bundlePath;
        Fingerprint = fingerprint;
        ProjectPluginRoot = projectPluginRoot;
        ProjectWorkspaceRoot = projectWorkspaceRoot;
        Succeeded = succeeded;
        Diagnostics = diagnostics;
    }

    public string? BundlePath { get; }

    public string? Fingerprint { get; }

    public string? ProjectPluginRoot { get; }

    public string? ProjectWorkspaceRoot { get; }

    public bool Succeeded { get; }

    public IReadOnlyList<PluginDiagnostic> Diagnostics { get; }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(BundlePath))
            return;

        DotNetPluginCompiler.TryDeleteStaging(BundlePath);
    }

    public static DotNetPluginBuildPreparation Failed(
        IReadOnlyList<PluginDiagnostic> diagnostics) =>
        new(null, null, null, null, succeeded: false, diagnostics);

    public static DotNetPluginBuildPreparation Successful(
        string bundlePath,
        string fingerprint,
        string projectPluginRoot,
        string projectWorkspaceRoot,
        IReadOnlyList<PluginDiagnostic> diagnostics) =>
        new(bundlePath, fingerprint, projectPluginRoot, projectWorkspaceRoot, succeeded: true, diagnostics);
}
