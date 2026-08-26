using System.Globalization;
using DotCraft.Plugins;
using DotCraft.Runtime;
using Xunit;

namespace DotCraft.Tests.Runtime.Plugins.Authoring;

public sealed class DotNetPluginCompilerTests :
    IClassFixture<AuthoringReferencePackFixture>,
    IDisposable
{
    private const string PluginId = "acme.authoring-test";

    private readonly string _dataRoot;
    private readonly DotNetPluginCompiler _compiler;

    public DotNetPluginCompilerTests(AuthoringReferencePackFixture fixture)
    {
        _dataRoot = Path.Combine(
            fixture.Root,
            "compiler",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataRoot);
        _compiler = new DotNetPluginCompiler(fixture.Load());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Prepare_CompilesAStandardStagedBundleDeterministically(bool includeSecondSource)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Plugin.cs"] = ValidPluginSource
        };
        if (includeSecondSource)
        {
            sources["Text.cs"] = """
                namespace Acme;
                internal static class Text
                {
                    public static string Normalize(string value) => value.Trim();
                }

                internal sealed class State
                {
                    public string Value
                    {
                        get => field;
                        set => field = value.Trim();
                    } = string.Empty;
                }
                """;
        }

        var project = CreateProject(sources);
        var originalFingerprint = PluginBundleFingerprint.Compute(project.PluginRoot);

        using (var preparation = _compiler.Prepare(_dataRoot, PluginId))
        {
            Assert.True(
                preparation.Succeeded,
                string.Join(Environment.NewLine, preparation.Diagnostics.Select(static diagnostic => diagnostic.Message)));
            var fingerprint = Assert.IsType<string>(preparation.Fingerprint);
            var bundlePath = Assert.IsType<string>(preparation.BundlePath);

            var entryPath = Path.Combine(bundlePath, "lib", "Acme.Plugin.dll");
            Assert.True(File.Exists(entryPath));
            Assert.True(File.Exists(Path.ChangeExtension(entryPath, ".deps.json")));
            Assert.False(File.Exists(Path.Combine(bundlePath, "lib", "stale.txt")));
            Assert.Empty(Directory.EnumerateFiles(bundlePath, "DotCraft.*.dll", SearchOption.AllDirectories));
            Assert.False(File.Exists(Path.Combine(project.Root, "Acme.Plugin.csproj")));

            var parsed = PluginManifestParser.Load(bundlePath);
            Assert.NotNull(parsed.Manifest);
            Assert.DoesNotContain(
                parsed.Diagnostics,
                static diagnostic => diagnostic.Severity == PluginDiagnosticSeverity.Error);

            using var repeated = _compiler.Prepare(_dataRoot, PluginId);
            Assert.True(repeated.Succeeded);
            Assert.Equal(fingerprint, repeated.Fingerprint);
        }

        Assert.Equal(originalFingerprint, PluginBundleFingerprint.Compute(project.PluginRoot));
        Assert.Empty(Directory.EnumerateDirectories(project.Root, ".plugin-stage-*"));
    }

    [Fact]
    public void Prepare_ReportsRelativeRoslynDiagnosticsWithoutChangingTheBundle()
    {
        var project = CreateProject(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Broken.cs"] = """
                    using DotCraft.Plugins;
                    namespace Acme;
                    public sealed class Plugin : IDotCraftPlugin
                    {
                        this is not valid C#
                    }
                    """
            });
        var originalFingerprint = PluginBundleFingerprint.Compute(project.PluginRoot);

        using var preparation = _compiler.Prepare(_dataRoot, PluginId);

        Assert.False(preparation.Succeeded);
        Assert.Contains(
            preparation.Diagnostics,
            static diagnostic => diagnostic.Code.StartsWith("CS", StringComparison.Ordinal)
                && diagnostic.Path == "src/Broken.cs"
                && diagnostic.Parameters["phase"].GetString() == "compile"
                && diagnostic.Parameters.ContainsKey("line")
                && diagnostic.Parameters.ContainsKey("column"));
        AssertDiagnosticsAreSanitized(preparation.Diagnostics);
        Assert.Equal(
            "keep",
            File.ReadAllText(Path.Combine(project.PluginRoot, "lib", "stale.txt")));
        Assert.Equal(originalFingerprint, PluginBundleFingerprint.Compute(project.PluginRoot));
    }

    [Fact]
    public void Prepare_UsesInvariantRoslynMessages()
    {
        CreateProject(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Broken.cs"] = """
                    namespace Acme;
                    public sealed class Broken
                    {
                        this is not valid C#
                    }
                    """
            });
        var previousCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            using var preparation = _compiler.Prepare(_dataRoot, PluginId);

            Assert.False(preparation.Succeeded);
            Assert.Contains(
                preparation.Diagnostics,
                static diagnostic => diagnostic.Code == "CS1519"
                    && diagnostic.Message.Contains("Invalid token", StringComparison.Ordinal));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [Fact]
    public void Prepare_RejectsAnEntryTypeThatDoesNotMatchTheCompiledAssembly()
    {
        var project = CreateProject(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Plugin.cs"] = ValidPluginSource
            },
            entryType: "Acme.MissingPlugin");
        var originalFingerprint = PluginBundleFingerprint.Compute(project.PluginRoot);

        using var preparation = _compiler.Prepare(_dataRoot, PluginId);

        Assert.False(preparation.Succeeded);
        Assert.Contains(
            preparation.Diagnostics,
            static diagnostic => diagnostic.Code == "PluginEntryTypeInvalid"
                && diagnostic.Parameters["phase"].GetString() == "preflight");
        AssertDiagnosticsAreSanitized(preparation.Diagnostics);
        Assert.Equal(
            "keep",
            File.ReadAllText(Path.Combine(project.PluginRoot, "lib", "stale.txt")));
        Assert.Equal(originalFingerprint, PluginBundleFingerprint.Compute(project.PluginRoot));
    }

    [Fact]
    public void Prepare_RejectsAnInvalidManifestWithoutChangingTheBundle()
    {
        var project = CreateProject(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Plugin.cs"] = ValidPluginSource
            });
        File.WriteAllText(
            Path.Combine(project.PluginRoot, ".craft-plugin", "plugin.json"),
            "{");
        var originalFingerprint = PluginBundleFingerprint.Compute(project.PluginRoot);

        using var preparation = _compiler.Prepare(_dataRoot, PluginId);

        Assert.False(preparation.Succeeded);
        Assert.Contains(
            preparation.Diagnostics,
            static diagnostic => diagnostic.Code == "InvalidPluginManifestJson");
        AssertDiagnosticsAreSanitized(preparation.Diagnostics);
        Assert.Equal(originalFingerprint, PluginBundleFingerprint.Compute(project.PluginRoot));
    }

    [Fact]
    public void Prepare_AppliesTheCurrentHostVersionPreflight()
    {
        var project = CreateProject(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Plugin.cs"] = ValidPluginSource
            },
            minHostVersion: "999.0.0");
        var originalFingerprint = PluginBundleFingerprint.Compute(project.PluginRoot);

        using var preparation = _compiler.Prepare(_dataRoot, PluginId);

        Assert.False(preparation.Succeeded);
        Assert.Contains(
            preparation.Diagnostics,
            static diagnostic => diagnostic.Code == "PluginHostVersionUnsatisfied");
        AssertDiagnosticsAreSanitized(preparation.Diagnostics);
        Assert.Equal(originalFingerprint, PluginBundleFingerprint.Compute(project.PluginRoot));
    }

    [Fact]
    public void Prepare_RejectsAProjectAndManifestIdMismatch()
    {
        CreateProject(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Plugin.cs"] = ValidPluginSource
            },
            manifestId: "acme.other");

        using var preparation = _compiler.Prepare(_dataRoot, PluginId);

        Assert.False(preparation.Succeeded);
        Assert.Contains(
            preparation.Diagnostics,
            static diagnostic => diagnostic.Code == "DotNetPluginAuthoringIdMismatch");
        AssertDiagnosticsAreSanitized(preparation.Diagnostics);
    }

    [Fact]
    public void Prepare_RejectsAProjectWithoutSourceFiles()
    {
        CreateProject(new Dictionary<string, string>(StringComparer.Ordinal));

        using var preparation = _compiler.Prepare(_dataRoot, PluginId);

        Assert.False(preparation.Succeeded);
        var diagnostic = Assert.Single(preparation.Diagnostics);
        Assert.Equal("DotNetPluginSourceMissing", diagnostic.Code);
        Assert.Equal("src", diagnostic.Path);
        AssertDiagnosticsAreSanitized(preparation.Diagnostics);
    }

    [Fact]
    public void Prepare_RejectsASourceRootLink()
    {
        var project = CreateProject(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Plugin.cs"] = ValidPluginSource
            });
        var sourceRoot = Path.Combine(project.Root, "src");
        var linkedTarget = Path.Combine(_dataRoot, "linked-source-root");
        Directory.CreateDirectory(linkedTarget);
        File.WriteAllText(Path.Combine(linkedTarget, "Plugin.cs"), ValidPluginSource);
        Directory.Delete(sourceRoot, recursive: true);
        try
        {
            Directory.CreateSymbolicLink(sourceRoot, linkedTarget);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        using var preparation = _compiler.Prepare(_dataRoot, PluginId);

        Assert.False(preparation.Succeeded);
        var diagnostic = Assert.Single(preparation.Diagnostics);
        Assert.Equal("DotNetPluginSourceLinkUnsupported", diagnostic.Code);
        Assert.Equal("src", diagnostic.Path);
    }

    [Fact]
    public void Prepare_RejectsANestedSourceFileLink()
    {
        var project = CreateProject(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Plugin.cs"] = ValidPluginSource
            });
        var linkedTarget = Path.Combine(_dataRoot, "linked-source.cs");
        var link = Path.Combine(project.Root, "src", "Linked.cs");
        File.WriteAllText(linkedTarget, ValidPluginSource);
        try
        {
            File.CreateSymbolicLink(link, linkedTarget);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        using var preparation = _compiler.Prepare(_dataRoot, PluginId);

        Assert.False(preparation.Succeeded);
        var diagnostic = Assert.Single(preparation.Diagnostics);
        Assert.Equal("DotNetPluginSourceLinkUnsupported", diagnostic.Code);
        Assert.Equal("src/Linked.cs", diagnostic.Path);
    }

    [Fact]
    public void Prepare_ReportsAStableDiagnosticWhenSourceCannotBeRead()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var project = CreateProject(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Plugin.cs"] = ValidPluginSource
            });
        var sourcePath = Path.Combine(project.Root, "src", "Plugin.cs");
        using var lease = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.None);

        using var preparation = _compiler.Prepare(_dataRoot, PluginId);

        Assert.False(preparation.Succeeded);
        var diagnostic = Assert.Single(preparation.Diagnostics);
        Assert.Equal("DotNetPluginAuthoringFileAccessFailed", diagnostic.Code);
        Assert.Null(diagnostic.Path);
        AssertDiagnosticsAreSanitized(preparation.Diagnostics);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    private ProjectLayout CreateProject(
        IReadOnlyDictionary<string, string> sources,
        string entryType = "Acme.Plugin",
        string? manifestId = null,
        string minHostVersion = "0.0.0")
    {
        var root = Path.Combine(_dataRoot, "plugin-projects", PluginId);
        var sourceRoot = Path.Combine(root, "src");
        var pluginRoot = Path.Combine(root, "plugin");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "lib"));
        File.WriteAllText(Path.Combine(pluginRoot, "lib", "stale.txt"), "keep");

        foreach (var source in sources)
        {
            var sourcePath = Path.Combine(sourceRoot, source.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(sourcePath, source.Value);
        }

        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
            {
              "schemaVersion": 1,
              "id": "{{manifestId ?? PluginId}}",
              "version": "1.0.0",
              "displayName": "Authoring test",
              "capabilities": ["dotnet"],
              "dotnet": {
                "minHostVersion": "{{minHostVersion}}",
                "entryAssembly": "./lib/Acme.Plugin.dll",
                "entryType": "{{entryType}}"
              }
            }
            """);

        return new ProjectLayout(root, pluginRoot);
    }

    private void AssertDiagnosticsAreSanitized(IReadOnlyList<PluginDiagnostic> diagnostics)
    {
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.DoesNotContain(_dataRoot, diagnostic.Message, StringComparison.OrdinalIgnoreCase);
            if (diagnostic.Path != null)
                Assert.DoesNotContain(_dataRoot, diagnostic.Path, StringComparison.OrdinalIgnoreCase);
        });
    }

    private const string ValidPluginSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using DotCraft.Plugins;

        namespace Acme;

        public sealed class Plugin : IDotCraftPlugin
        {
            public ValueTask ActivateAsync(
                IPluginActivationContext context,
                CancellationToken cancellationToken)
            {
                return ValueTask.CompletedTask;
            }
        }
        """;

    private sealed record ProjectLayout(string Root, string PluginRoot);
}
