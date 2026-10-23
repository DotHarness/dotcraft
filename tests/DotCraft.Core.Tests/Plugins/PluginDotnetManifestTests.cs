using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Plugins;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DotCraft.Tests.Plugins;

public sealed class PluginDotnetManifestTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dotcraft_dotnet_plugin_{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    [Theory]
    [InlineData("1.2.3", "1.9.0", true)]
    [InlineData("1.2.3", "2.0.0", false)]
    [InlineData("0.2.3", "0.3.0", false)]
    public void DependencyVersion_RequiresACompatibleMinimum(
        string minimum,
        string observed,
        bool expected) =>
        Assert.Equal(expected, PluginDotnetManifestAdmission.SatisfiesMinimum(minimum, observed));

    [Fact]
    public void Load_AcceptsValidBundleWithInheritedEntryContractWithoutExecutingAssembly()
    {
        var pluginRoot = Path.Combine(_root, "valid");
        var dotnetRoot = Path.Combine(pluginRoot, "dotnet");
        Directory.CreateDirectory(dotnetRoot);
        var corePath = Path.Combine(dotnetRoot, "DotCraft.Core.dll");
        Compile(corePath, "namespace DotCraft.Plugins; public interface IDotCraftPlugin { }");
        Compile(Path.Combine(dotnetRoot, "Acme.Api.dll"), "namespace Acme; public interface IReviewApi { }");
        var markerPath = Path.Combine(pluginRoot, "executed.txt");
        Compile(
            Path.Combine(dotnetRoot, "Acme.Plugin.dll"),
            $$"""
            using System.IO;
            using System.Runtime.CompilerServices;
            using System.Runtime.Versioning;
            using DotCraft.Plugins;
            [assembly: TargetFramework(".NETCoreApp,Version=v10.0")]
            namespace Acme;
            public abstract class PluginBase : IDotCraftPlugin { }
            public sealed class ReviewPlugin : PluginBase
            {
                public ReviewPlugin() { }
                [ModuleInitializer]
                public static void Initialize() => File.WriteAllText(@"{{markerPath}}", "executed");
            }
            """,
            corePath);
        File.WriteAllText(Path.Combine(dotnetRoot, "Acme.Plugin.deps.json"), "{}");
        WriteManifest(pluginRoot);

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.NotNull(result.Manifest?.Dotnet);
        Assert.Equal("1.2.3", result.Manifest!.Version);
        Assert.Equal("0.1.0", result.Manifest.Dotnet!.MinHostVersion);
        Assert.Null(result.Manifest.Dotnet.DisplayName);
        Assert.Null(result.Manifest.Dotnet.Description);
        Assert.Equal("2.0.0", result.Manifest.Dependencies["acme.core"]);
        Assert.DoesNotContain(result.Diagnostics, static diagnostic => diagnostic.Severity == PluginDiagnosticSeverity.Error);
        Assert.False(File.Exists(markerPath));
    }

    [Fact]
    public void Load_TrimsOptionalDotnetPresentationMetadata()
    {
        var pluginRoot = Path.Combine(_root, "presentation-metadata");
        WriteManifest(
            pluginRoot,
            dotnetPresentationJson: "\"displayName\": \"  Review integration  \", \"description\": \"  Provides review.  \"");

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.NotNull(result.Manifest?.Dotnet);
        Assert.Equal("Review integration", result.Manifest!.Dotnet!.DisplayName);
        Assert.Equal("Provides review.", result.Manifest.Dotnet.Description);
    }

    [Fact]
    public void Load_RejectsInvalidDotnetPresentationMetadataType()
    {
        var pluginRoot = Path.Combine(_root, "invalid-presentation-metadata");
        WriteManifest(pluginRoot, dotnetPresentationJson: "\"displayName\": 42");

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.NotNull(result.Manifest);
        Assert.Null(result.Manifest!.Dotnet);
        AssertAdmissionFailure(result, "dotnet.displayName", "invalidType");
    }

    [Theory]
    [InlineData("[1.0.0,2.0.0)")]
    public void Load_RejectsNonCanonicalPluginVersion(string version)
    {
        var pluginRoot = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        WriteManifest(pluginRoot, version: version);

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.NotNull(result.Manifest);
        Assert.Null(result.Manifest!.Dotnet);
        AssertAdmissionFailure(result, "version", "invalidFormat");
    }

    [Theory]
    [InlineData("", "missing")]
    [InlineData("\"0.3.0-preview.1\"", "invalidFormat")]
    public void Load_RejectsMalformedMinHostVersion(string minHostVersionJson, string reason)
    {
        var pluginRoot = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        WriteManifest(pluginRoot, minHostVersionJson: minHostVersionJson);

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.NotNull(result.Manifest);
        Assert.Null(result.Manifest!.Dotnet);
        AssertAdmissionFailure(result, "dotnet.minHostVersion", reason);
    }

    [Theory]
    [InlineData("\"../escape.dll\"", "\"./dotnet/Acme.Api.dll\"", "dotnet.entryAssembly", "invalidPath")]
    [InlineData("\"./dotnet/Acme.Plugin.dll\"", "\"./dotnet/Api.dll\", \"./dotnet/api.dll\"", "dotnet.exportedApiAssemblies", "duplicate")]
    [InlineData("\"./dotnet/Api.dll\"", "\"./dotnet/api.dll\"", "dotnet.exportedApiAssemblies", "entryAssemblyExported")]
    public void Load_RejectsInvalidDotnetPaths(string entryAssemblyJson, string exportsJson, string field, string reason)
    {
        var pluginRoot = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        WriteManifest(pluginRoot, entryAssemblyJson: entryAssemblyJson, exportsJson: exportsJson);

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.NotNull(result.Manifest);
        Assert.Null(result.Manifest!.Dotnet);
        AssertAdmissionFailure(result, field, reason);
    }

    [Theory]
    [InlineData("acme.review", "1.0.0", "selfDependency")]
    [InlineData("acme.core", "^2.0.0", "invalidFormat")]
    public void Load_RejectsInvalidDependencies(string dependencyId, string requiredVersion, string reason)
    {
        var pluginRoot = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        WriteManifest(pluginRoot, dependenciesJson: $"\"{dependencyId}\": \"{requiredVersion}\"");

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.NotNull(result.Manifest);
        Assert.Empty(result.Manifest!.Dependencies);
        var diagnostic = AssertAdmissionFailure(result, "dependencies[]", reason);
        Assert.Equal(dependencyId, diagnostic.Parameters["dependencyId"].GetString());
    }

    [Fact]
    public void Load_RejectsDependenciesWithoutDotnet()
    {
        var pluginRoot = Path.Combine(_root, "dependency-only");
        WriteManifest(pluginRoot, includeDotnet: false);

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.NotNull(result.Manifest);
        AssertAdmissionFailure(result, "dependencies", "dotnetRequired");
    }

    [Fact]
    public void Discovery_BadDotnetBundleDoesNotBlockOtherPlugins()
    {
        var workspace = Path.Combine(_root, "workspace");
        var botPath = Path.Combine(workspace, ".craft");
        WriteManifest(Path.Combine(botPath, "plugins", "bad"), id: "bad", entryAssemblyJson: "\"./dotnet/Missing.dll\"");
        WriteInterfacePlugin(Path.Combine(botPath, "plugins", "good"));

        var result = new PluginDiscoveryService(Path.Combine(_root, "global"))
            .DiscoverAll(new AppConfig(), workspace, botPath);

        Assert.Contains(result.Plugins, static plugin => plugin.Manifest.Id == "good");
        Assert.Contains(result.Plugins, static plugin => plugin.Manifest.Id == "bad");
        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == PluginDotnetDiagnosticCodes.EntryAssemblyMissing);
    }

    [Fact]
    public void Discovery_ProjectsDependencyObservationsOntoDotNetPlugins()
    {
        var workspace = Path.Combine(_root, "dependency-workspace");
        var botPath = Path.Combine(workspace, ".craft");
        WriteManifest(
            Path.Combine(botPath, "plugins", "consumer"),
            id: "acme.review",
            dependenciesJson: "\"acme.core\": \"2.0.0\", \"acme.absent\": \"1.0.0\"");
        WriteInterfacePlugin(Path.Combine(botPath, "plugins", "provider"), id: "acme.core", version: "1.0.0");

        var result = new PluginDiscoveryService(Path.Combine(_root, "dependency-global"))
            .DiscoverAll(new AppConfig(), workspace, botPath);

        var consumer = Assert.Single(result.Plugins, plugin => plugin.Manifest.Id == "acme.review");
        Assert.Collection(
            consumer.DependencyObservations,
            observation =>
            {
                Assert.Equal("acme.absent", observation.Id);
                Assert.Equal(PluginDependencyAvailability.Missing, observation.Availability);
            },
            observation =>
            {
                Assert.Equal("acme.core", observation.Id);
                Assert.Equal("1.0.0", observation.ObservedVersion);
                Assert.Equal(PluginDependencyAvailability.VersionUnsatisfied, observation.Availability);
            });
        var provider = Assert.Single(result.Plugins, plugin => plugin.Manifest.Id == "acme.core");
        Assert.Empty(provider.DependencyObservations);
    }

    [Fact]
    public void Load_ReportsInvalidManagedEntryAssembly()
    {
        var pluginRoot = Path.Combine(_root, "invalid-entry");
        Directory.CreateDirectory(Path.Combine(pluginRoot, "dotnet"));
        File.WriteAllText(Path.Combine(pluginRoot, "dotnet", "Acme.Plugin.dll"), "not a managed assembly");
        File.WriteAllText(Path.Combine(pluginRoot, "dotnet", "Acme.Plugin.deps.json"), "{}");
        WriteManifest(pluginRoot, exportsJson: string.Empty);

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == PluginDotnetDiagnosticCodes.EntryAssemblyInvalid);
    }

    [Fact]
    public void Load_ReportsMissingDependencyManifestInvalidEntryTypeAndMissingApiExport()
    {
        var pluginRoot = Path.Combine(_root, "invalid-type");
        var dotnetRoot = Path.Combine(pluginRoot, "dotnet");
        Directory.CreateDirectory(dotnetRoot);
        var corePath = Path.Combine(dotnetRoot, "DotCraft.Core.dll");
        Compile(corePath, "namespace DotCraft.Plugins; public interface IDotCraftPlugin { }");
        Compile(
            Path.Combine(dotnetRoot, "Acme.Plugin.dll"),
            """
            using System.Runtime.Versioning;
            using DotCraft.Plugins;
            [assembly: TargetFramework(".NETCoreApp,Version=v10.0")]
            namespace Acme;
            public sealed class DifferentPlugin : IDotCraftPlugin { public DifferentPlugin() { } }
            """,
            corePath);
        WriteManifest(pluginRoot);

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == PluginDotnetDiagnosticCodes.DependencyManifestMissing);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == PluginDotnetDiagnosticCodes.EntryTypeInvalid
                                 && diagnostic.Parameters["reason"].GetString() == "typeNotFound");
        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == PluginDotnetDiagnosticCodes.ApiExportInvalid
                                 && diagnostic.Parameters["reason"].GetString() == "assemblyMissing");
    }

    [Fact]
    public void Load_AcceptsReferencesToHostProductAssemblies()
    {
        var pluginRoot = Path.Combine(_root, "host-reference");
        var dotnetRoot = Path.Combine(pluginRoot, "dotnet");
        Directory.CreateDirectory(dotnetRoot);
        var runtimePath = Path.Combine(dotnetRoot, "DotCraft.Runtime.dll");
        Compile(runtimePath, "namespace DotCraft.Runtime; public sealed class HostMarker { }");
        Compile(
            Path.Combine(dotnetRoot, "Acme.Plugin.dll"),
            """
            using System.Runtime.Versioning;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            using DotCraft.Runtime;
            [assembly: TargetFramework(".NETCoreApp,Version=v10.0")]
            namespace Acme;
            public sealed class ReviewPlugin : IDotCraftPlugin
            {
                private HostMarker? _marker;
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                    => ValueTask.CompletedTask;
            }
            """,
            typeof(IDotCraftPlugin).Assembly.Location,
            runtimePath);
        File.WriteAllText(Path.Combine(dotnetRoot, "Acme.Plugin.deps.json"), "{}");
        WriteManifest(pluginRoot, exportsJson: string.Empty);

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.DoesNotContain(result.Diagnostics, static diagnostic => diagnostic.Severity == PluginDiagnosticSeverity.Error);
    }

    [Fact]
    public void HostVersionGate_BlocksBundlesThatRequireANewerHost()
    {
        var pluginRoot = Path.Combine(_root, "future-host");
        WriteValidBundle(pluginRoot, minHostVersion: "9999.0.0");

        var result = PluginManifestParser.Load(pluginRoot);

        var diagnostic = Assert.Single(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == PluginDotnetDiagnosticCodes.HostVersionUnsatisfied);
        Assert.Equal(PluginDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("9999.0.0", diagnostic.Parameters["minHostVersion"].GetString());
        Assert.NotNull(diagnostic.Parameters["hostVersion"].GetString());

        var manifest = result.Manifest!;
        var older = PluginDotnetMetadataInspector.Inspect(manifest, new PluginHostVersion(new Version(9998, 9, 9)));
        var exact = PluginDotnetMetadataInspector.Inspect(manifest, new PluginHostVersion(new Version(9999, 0, 0)));
        var newer = PluginDotnetMetadataInspector.Inspect(manifest, new PluginHostVersion(new Version(10000, 0, 0)));

        Assert.Contains(older, static diagnostic => diagnostic.Code == PluginDotnetDiagnosticCodes.HostVersionUnsatisfied);
        Assert.DoesNotContain(exact, static diagnostic => diagnostic.Code == PluginDotnetDiagnosticCodes.HostVersionUnsatisfied);
        Assert.DoesNotContain(newer, static diagnostic => diagnostic.Code == PluginDotnetDiagnosticCodes.HostVersionUnsatisfied);
    }

    [Fact]
    public void Load_KeepsSchemaVersionOneContract()
    {
        var pluginRoot = Path.Combine(_root, "schema-two");
        WriteManifest(pluginRoot, schemaVersion: 2);

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.Null(result.Manifest);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "UnsupportedPluginManifestVersion");
    }

    private static PluginDiagnostic AssertAdmissionFailure(
        PluginManifestParseResult result,
        string field,
        string reason)
    {
        var diagnostic = Assert.Single(
            result.Diagnostics,
            diagnostic => diagnostic.Code == PluginDotnetDiagnosticCodes.AdmissionFailed
                          && diagnostic.Parameters["field"].GetString() == field
                          && diagnostic.Parameters["reasonCode"].GetString() == reason);
        Assert.Equal("acme.review", diagnostic.PluginId);
        return diagnostic;
    }

    private static void WriteValidBundle(string pluginRoot, string minHostVersion = "0.1.0")
    {
        var dotnetRoot = Path.Combine(pluginRoot, "dotnet");
        Directory.CreateDirectory(dotnetRoot);
        var corePath = Path.Combine(dotnetRoot, "DotCraft.Core.dll");
        Compile(corePath, "namespace DotCraft.Plugins; public interface IDotCraftPlugin { }");
        Compile(
            Path.Combine(dotnetRoot, "Acme.Plugin.dll"),
            """
            using System.Runtime.Versioning;
            using DotCraft.Plugins;
            [assembly: TargetFramework(".NETCoreApp,Version=v10.0")]
            namespace Acme;
            public sealed class ReviewPlugin : IDotCraftPlugin { public ReviewPlugin() { } }
            """,
            corePath);
        File.WriteAllText(Path.Combine(dotnetRoot, "Acme.Plugin.deps.json"), "{}");
        WriteManifest(pluginRoot, minHostVersionJson: $"\"{minHostVersion}\"", exportsJson: string.Empty);
    }

    private static void WriteManifest(
        string pluginRoot,
        string id = "acme.review",
        int schemaVersion = 1,
        string version = "1.2.3",
        bool includeDotnet = true,
        string minHostVersionJson = "\"0.1.0\"",
        string entryAssemblyJson = "\"./dotnet/Acme.Plugin.dll\"",
        string exportsJson = "\"./dotnet/Acme.Api.dll\"",
        string dependenciesJson = "\"acme.core\": \"2.0.0\"",
        string dotnetPresentationJson = "")
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        var minHostVersion = string.IsNullOrEmpty(minHostVersionJson)
            ? string.Empty
            : $"\n      \"minHostVersion\": {minHostVersionJson},";
        var dotnetPresentation = string.IsNullOrEmpty(dotnetPresentationJson)
            ? string.Empty
            : $"\n      {dotnetPresentationJson},";
        var dotnet = includeDotnet
            ? $$"""
              ,
                "dotnet": { {{dotnetPresentation}}{{minHostVersion}}
                  "entryAssembly": {{entryAssemblyJson}},
                  "entryType": "Acme.ReviewPlugin",
                  "exportedApiAssemblies": [{{exportsJson}}]
                }
            """
            : string.Empty;
        var interfaceContribution = includeDotnet
            ? string.Empty
            : ",\n  \"interface\": { \"displayName\": \"Acme Review\" }";
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
            {
              "schemaVersion": {{schemaVersion}},
              "id": "{{id}}",
              "version": "{{version}}",
              "displayName": "Acme Review",
              "capabilities": ["dotnet"]
              {{dotnet}}{{interfaceContribution}},
              "dependencies": {
                {{dependenciesJson}}
              }
            }
            """);
    }

    private static void WriteInterfacePlugin(string pluginRoot, string id = "good", string version = "1.0.0")
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
            {
              "schemaVersion": 1,
              "id": "{{id}}",
              "version": "{{version}}",
              "displayName": "Good",
              "capabilities": ["metadata"],
              "interface": { "displayName": "Good" }
            }
            """);
    }

    private static readonly Lazy<(string FileName, MetadataReference Reference)[]> PlatformReferences = new(() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static path => (Path.GetFileName(path), (MetadataReference)MetadataReference.CreateFromFile(path)))
            .ToArray());

    private static void Compile(string outputPath, string source, params string[] additionalReferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var explicitFileNames = additionalReferences
            .Select(Path.GetFileName)
            .Append(Path.GetFileName(outputPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var references = PlatformReferences.Value
            .Where(reference => !explicitFileNames.Contains(reference.FileName))
            .Select(static reference => reference.Reference)
            .Concat(additionalReferences.Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path)));
        var compilation = CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(outputPath),
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = File.Create(outputPath);
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
    }
}
