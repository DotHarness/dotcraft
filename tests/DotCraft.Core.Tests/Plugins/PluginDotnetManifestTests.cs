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
            // Best-effort cleanup.
        }
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3", true)]
    [InlineData("1.2.3", "1.9.0", true)]
    [InlineData("1.2.3", "1.2.2", false)]
    [InlineData("1.2.3", "2.0.0", false)]
    [InlineData("0.2.3", "0.2.9", true)]
    [InlineData("0.2.3", "0.3.0", false)]
    public void DependencyVersion_RequiresACompatibleMinimum(
        string minimum,
        string observed,
        bool expected) =>
        Assert.Equal(expected, PluginDotnetManifestAdmission.SatisfiesMinimum(minimum, observed));

    [Fact]
    public void Load_AcceptsValidBundleWithoutExecutingAssembly()
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
            public sealed class ReviewPlugin : IDotCraftPlugin
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
        Assert.Equal("2.0.0", result.Manifest.Dependencies["acme.core"]);
        Assert.DoesNotContain(result.Diagnostics, static diagnostic => diagnostic.Severity == PluginDiagnosticSeverity.Error);
        Assert.False(File.Exists(markerPath));
    }

    [Theory]
    [InlineData("1.2")]
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

    [Fact]
    public void Load_RejectsMissingMinHostVersion()
    {
        var pluginRoot = Path.Combine(_root, "no-min-host-version");
        WriteManifest(pluginRoot, minHostVersionJson: string.Empty);

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.NotNull(result.Manifest);
        Assert.Null(result.Manifest!.Dotnet);
        AssertAdmissionFailure(result, "dotnet.minHostVersion", "missing");
    }

    [Theory]
    [InlineData("\"0.3\"", "invalidFormat")]
    [InlineData("\"0.3.0-preview.1\"", "invalidFormat")]
    [InlineData("\"\"", "invalidFormat")]
    [InlineData("3", "invalidType")]
    public void Load_RejectsMalformedMinHostVersion(string minHostVersionJson, string reason)
    {
        var pluginRoot = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        WriteManifest(pluginRoot, minHostVersionJson: minHostVersionJson);

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.NotNull(result.Manifest);
        Assert.Null(result.Manifest!.Dotnet);
        AssertAdmissionFailure(result, "dotnet.minHostVersion", reason);
    }

    [Fact]
    public void Load_BlocksBundleThatRequiresANewerHost()
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
    }

    [Theory]
    [InlineData("\"../escape.dll\"", "dotnet.entryAssembly", "invalidPath")]
    [InlineData("\"./dotnet/Api.dll\", \"./dotnet/api.dll\"", "dotnet.exportedApiAssemblies", "duplicate")]
    public void Load_RejectsInvalidDotnetPaths(string entryOrExports, string field, string reason)
    {
        var pluginRoot = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        if (field == "dotnet.entryAssembly")
            WriteManifest(pluginRoot, entryAssemblyJson: entryOrExports);
        else
            WriteManifest(pluginRoot, exportsJson: entryOrExports);

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.NotNull(result.Manifest);
        Assert.Null(result.Manifest!.Dotnet);
        AssertAdmissionFailure(result, field, reason);
    }

    [Fact]
    public void Load_RejectsEntryAssemblyExportedWithDifferentCasing()
    {
        var pluginRoot = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        WriteManifest(
            pluginRoot,
            entryAssemblyJson: "\"./dotnet/Api.dll\"",
            exportsJson: "\"./dotnet/api.dll\"");

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.NotNull(result.Manifest);
        Assert.Null(result.Manifest!.Dotnet);
        AssertAdmissionFailure(result, "dotnet.exportedApiAssemblies", "entryAssemblyExported");
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
    public void Discovery_TreatsDeclaredDependencyVersionsAsMinimums()
    {
        var workspace = Path.Combine(_root, "minimum-workspace");
        var botPath = Path.Combine(workspace, ".craft");
        WriteManifest(
            Path.Combine(botPath, "plugins", "consumer"),
            id: "acme.review",
            dependenciesJson: "\"acme.core\": \"1.0.0\"");
        WriteInterfacePlugin(Path.Combine(botPath, "plugins", "provider"), id: "acme.core", version: "1.2.3");

        var result = new PluginDiscoveryService(Path.Combine(_root, "minimum-global"))
            .DiscoverAll(new AppConfig(), workspace, botPath);

        var observation = Assert.Single(
            Assert.Single(result.Plugins, plugin => plugin.Manifest.Id == "acme.review").DependencyObservations);
        Assert.Equal("1.2.3", observation.ObservedVersion);
        Assert.NotEqual(PluginDependencyAvailability.VersionUnsatisfied, observation.Availability);
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
    public void Load_ReportsMissingDependencyManifestAndInvalidEntryType()
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
        WriteManifest(pluginRoot, exportsJson: string.Empty);

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == PluginDotnetDiagnosticCodes.DependencyManifestMissing);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == PluginDotnetDiagnosticCodes.EntryTypeInvalid
                                 && diagnostic.Parameters["reason"].GetString() == "typeNotFound");
    }

    [Fact]
    public void Load_ReportsMissingApiExport()
    {
        var pluginRoot = Path.Combine(_root, "missing-api");
        WriteValidBundle(pluginRoot);
        WriteManifest(pluginRoot);

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == PluginDotnetDiagnosticCodes.ApiExportInvalid
                                 && diagnostic.Parameters["reason"].GetString() == "assemblyMissing");
    }

    [Fact]
    public void Load_AcceptsEntryContractInheritedFromABaseType()
    {
        var pluginRoot = Path.Combine(_root, "inherited-entry");
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
            public abstract class PluginBase : IDotCraftPlugin { }
            public sealed class ReviewPlugin : PluginBase { public ReviewPlugin() { } }
            """,
            corePath);
        File.WriteAllText(Path.Combine(dotnetRoot, "Acme.Plugin.deps.json"), "{}");
        WriteManifest(pluginRoot, exportsJson: string.Empty);

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.DoesNotContain(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == PluginDotnetDiagnosticCodes.EntryTypeInvalid);
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
    public void Inspect_GatesOnTheSuppliedHostVersion()
    {
        var pluginRoot = Path.Combine(_root, "explicit-host");
        WriteValidBundle(pluginRoot, minHostVersion: "0.3.0");
        WriteManifest(pluginRoot, minHostVersionJson: "\"0.3.0\"", exportsJson: string.Empty);
        var manifest = PluginManifestParser.Load(pluginRoot).Manifest;
        Assert.NotNull(manifest);

        var older = PluginDotnetMetadataInspector.Inspect(manifest!, new PluginHostVersion(new Version(0, 2, 9)));
        var exact = PluginDotnetMetadataInspector.Inspect(manifest!, new PluginHostVersion(new Version(0, 3, 0)));
        var newer = PluginDotnetMetadataInspector.Inspect(manifest!, new PluginHostVersion(new Version(1, 0, 0)));

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

    /// <summary>Emits an entry assembly that satisfies every preflight check except the API export
    /// list, so tests can vary one manifest field at a time.</summary>
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
        string dependenciesJson = "\"acme.core\": \"2.0.0\"")
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        var minHostVersion = string.IsNullOrEmpty(minHostVersionJson)
            ? string.Empty
            : $"\n      \"minHostVersion\": {minHostVersionJson},";
        var dotnet = includeDotnet
            ? $$"""
              ,
                "dotnet": { {{minHostVersion}}
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

    private static void Compile(string outputPath, string source, params string[] additionalReferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var explicitFileNames = additionalReferences
            .Select(Path.GetFileName)
            .Append(Path.GetFileName(outputPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => !explicitFileNames.Contains(Path.GetFileName(path)))
            .Concat(additionalReferences)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static path => MetadataReference.CreateFromFile(path));
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
