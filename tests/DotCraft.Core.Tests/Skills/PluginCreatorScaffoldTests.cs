using System.Diagnostics;
using System.Text.Json;
using DotCraft.Plugins;
using DotCraft.Skills;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DotCraft.Tests.Skills;

public sealed class PluginCreatorScaffoldTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "dotcraft-plugin-creator-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void DotnetScaffold_UsesDeployedHostVersionAndMinimalProjectLayout()
    {
        var creator = DeployCreator();

        var result = RunCreator(creator, "My Plugin", "--dotnet");

        Assert.Equal(0, result.ExitCode);
        var projectRoot = Path.Combine(_root, ".craft", "plugin-projects", "my-plugin");
        var pluginRoot = Path.Combine(projectRoot, "plugin");
        var sourcePath = Path.Combine(projectRoot, "src", "Plugin.cs");
        var manifestPath = Path.Combine(pluginRoot, ".craft-plugin", "plugin.json");
        var libPath = Path.Combine(pluginRoot, "lib");
        Assert.True(File.Exists(sourcePath));
        Assert.True(File.Exists(manifestPath));
        Assert.True(Directory.Exists(libPath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(libPath));

        var files = Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(projectRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["plugin/.craft-plugin/plugin.json", "src/Plugin.cs"], files);
        Assert.DoesNotContain("TODO", File.ReadAllText(sourcePath), StringComparison.Ordinal);
        Assert.DoesNotContain("TODO", File.ReadAllText(manifestPath), StringComparison.Ordinal);

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = manifest.RootElement;
        Assert.Equal("my-plugin", root.GetProperty("id").GetString());
        Assert.Equal(["dotnet"], root.GetProperty("capabilities").EnumerateArray().Select(value => value.GetString()));
        var dotnet = root.GetProperty("dotnet");
        Assert.Equal(PluginHostVersion.Current.ProductText, dotnet.GetProperty("minHostVersion").GetString());
        Assert.Equal("./lib/MyPlugin.Plugin.dll", dotnet.GetProperty("entryAssembly").GetString());
        Assert.Equal("DotCraft.Plugin.MyPlugin.Plugin", dotnet.GetProperty("entryType").GetString());

        var source = File.ReadAllText(sourcePath);
        Assert.Contains("IDotCraftPlugin", source, StringComparison.Ordinal);
        Assert.Contains("AIFunctionToolSource", source, StringComparison.Ordinal);
        Assert.Contains("AIFunctionFactory.Create", source, StringComparison.Ordinal);
        Assert.Contains("name: \"my_plugin\"", source, StringComparison.Ordinal);
        AssertCompiles(source);
        var customParent = Path.Combine(_root, "custom-projects");
        var customPath = RunCreator(
            creator,
            "custom-path",
            "--dotnet",
            "--path",
            customParent);

        Assert.NotEqual(0, customPath.ExitCode);
        Assert.Contains("--path is not valid with --dotnet", customPath.StandardError, StringComparison.Ordinal);
        Assert.False(Directory.Exists(customParent));
    }

    [Fact]
    public void DefaultScaffold_CreatesWorkspaceLocalSkillPlugin()
    {
        var creator = DeployCreator();

        var result = RunCreator(creator, "Ordinary Plugin");

        Assert.Equal(0, result.ExitCode);
        var pluginRoot = Path.Combine(_root, ".craft", "plugins", "ordinary-plugin");
        Assert.True(File.Exists(Path.Combine(pluginRoot, ".craft-plugin", "plugin.json")));
        Assert.True(File.Exists(Path.Combine(pluginRoot, "skills", "ordinary-plugin", "SKILL.md")));
        Assert.False(Directory.Exists(Path.Combine(_root, ".craft", "plugin-projects", "ordinary-plugin")));
    }

    [Fact]
    public void DesktopScaffold_CreatesTypedSourceAndInlineManifest()
    {
        var creator = DeployCreator();

        var result = RunCreator(creator, "Desktop Plugin", "--without-skill", "--with-desktop");

        Assert.Equal(0, result.ExitCode);
        var pluginRoot = Path.Combine(_root, ".craft", "plugins", "desktop-plugin");
        var manifestPath = Path.Combine(pluginRoot, ".craft-plugin", "plugin.json");
        var packagePath = Path.Combine(pluginRoot, "desktop", "package.json");
        var tsconfigPath = Path.Combine(pluginRoot, "desktop", "tsconfig.json");
        var sourcePath = Path.Combine(pluginRoot, "desktop", "src", "index.tsx");
        Assert.True(File.Exists(manifestPath));
        Assert.True(File.Exists(packagePath));
        Assert.True(File.Exists(tsconfigPath));
        Assert.True(File.Exists(sourcePath));
        Assert.True(File.Exists(Path.Combine(pluginRoot, "desktop", "src", "index.css")));
        Assert.False(Directory.Exists(Path.Combine(pluginRoot, "desktop", "dist")));

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var manifestRoot = manifest.RootElement;
        Assert.Equal(["desktop"], manifestRoot.GetProperty("capabilities").EnumerateArray().Select(value => value.GetString()));
        var desktop = manifestRoot.GetProperty("desktop");
        Assert.Equal("./desktop/dist/index.mjs", desktop.GetProperty("entry").GetString());
        Assert.Equal(
            ["./desktop/dist/index.css"],
            desktop.GetProperty("styles").EnumerateArray().Select(value => value.GetString()));

        using var package = JsonDocument.Parse(File.ReadAllText(packagePath));
        var packageRoot = package.RootElement;
        var devDependencies = packageRoot.GetProperty("devDependencies");
        Assert.Equal(
            "tsc --noEmit && dotcraft-plugin build",
            packageRoot.GetProperty("scripts").GetProperty("build").GetString());
        Assert.Equal(
            PluginHostVersion.Current.ProductText,
            devDependencies.GetProperty("@dotcraft/plugin").GetString());
        Assert.Equal("^19.0.0", devDependencies.GetProperty("react").GetString());
        Assert.Equal("^19.0.0", devDependencies.GetProperty("@types/react").GetString());
        Assert.Equal("^5.7.0", devDependencies.GetProperty("typescript").GetString());

        using var tsconfig = JsonDocument.Parse(File.ReadAllText(tsconfigPath));
        var compilerOptions = tsconfig.RootElement.GetProperty("compilerOptions");
        Assert.Equal("Bundler", compilerOptions.GetProperty("moduleResolution").GetString());
        Assert.Equal("react-jsx", compilerOptions.GetProperty("jsx").GetString());
        Assert.True(compilerOptions.GetProperty("strict").GetBoolean());
        Assert.True(compilerOptions.GetProperty("noEmit").GetBoolean());

        var source = File.ReadAllText(sourcePath);
        Assert.Contains("Button", source, StringComparison.Ordinal);
        Assert.Contains("DesktopPluginActivate", source, StringComparison.Ordinal);
        Assert.Contains("DesktopPluginViewProps", source, StringComparison.Ordinal);
        Assert.Contains("export const activate", source, StringComparison.Ordinal);
        Assert.Contains("import \"./index.css\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DotnetDesktopScaffold_CreatesOneCombinedPluginBundle()
    {
        var creator = DeployCreator();

        var result = RunCreator(creator, "Combined Plugin", "--dotnet", "--with-desktop");

        Assert.Equal(0, result.ExitCode);
        var projectRoot = Path.Combine(_root, ".craft", "plugin-projects", "combined-plugin");
        var pluginRoot = Path.Combine(projectRoot, "plugin");
        var manifestPath = Path.Combine(pluginRoot, ".craft-plugin", "plugin.json");
        Assert.True(File.Exists(Path.Combine(projectRoot, "src", "Plugin.cs")));
        Assert.True(Directory.Exists(Path.Combine(pluginRoot, "lib")));
        Assert.True(File.Exists(Path.Combine(pluginRoot, "desktop", "src", "index.tsx")));
        Assert.True(File.Exists(Path.Combine(pluginRoot, "desktop", "package.json")));
        Assert.False(Directory.Exists(Path.Combine(_root, ".craft", "plugins", "combined-plugin")));

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = manifest.RootElement;
        Assert.Equal(
            ["dotnet", "desktop"],
            root.GetProperty("capabilities").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("./lib/CombinedPlugin.Plugin.dll", root.GetProperty("dotnet").GetProperty("entryAssembly").GetString());
        Assert.Equal("./desktop/dist/index.mjs", root.GetProperty("desktop").GetProperty("entry").GetString());
    }

    [Fact]
    public void DesktopScaffold_MergesIntoExistingPluginWithoutReplacingItsContent()
    {
        var creator = DeployCreator();
        var parent = Path.Combine(_root, "existing-plugins");
        var pluginRoot = Path.Combine(parent, "existing-plugin");
        var manifestPath = Path.Combine(pluginRoot, ".craft-plugin", "plugin.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(
            manifestPath,
            """
            {
              "schemaVersion": 1,
              "id": "existing-plugin",
              "version": "2.3.4",
              "displayName": "Existing Plugin",
              "capabilities": ["skill"],
              "skills": "./custom-skills/",
              "customField": { "keep": true }
            }
            """);
        var existingPath = Path.Combine(pluginRoot, "existing.txt");
        File.WriteAllText(existingPath, "keep me");

        var result = RunCreator(creator, "existing-plugin", "--with-desktop", "--path", parent);

        Assert.Equal(0, result.ExitCode);
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = manifest.RootElement;
        Assert.Equal("2.3.4", root.GetProperty("version").GetString());
        Assert.Equal("./custom-skills/", root.GetProperty("skills").GetString());
        Assert.True(root.GetProperty("customField").GetProperty("keep").GetBoolean());
        Assert.Equal(
            ["skill", "desktop"],
            root.GetProperty("capabilities").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("./desktop/dist/index.mjs", root.GetProperty("desktop").GetProperty("entry").GetString());
        Assert.Equal("keep me", File.ReadAllText(existingPath));
        Assert.False(Directory.Exists(Path.Combine(pluginRoot, "skills")));
        Assert.True(File.Exists(Path.Combine(pluginRoot, "desktop", "src", "index.tsx")));
    }

    [Fact]
    public void DesktopScaffold_RejectsExistingDeclarationOrSource()
    {
        var creator = DeployCreator();
        var parent = Path.Combine(_root, "duplicate-plugins");
        var declaredRoot = Path.Combine(parent, "declared-plugin");
        var declaredManifest = Path.Combine(declaredRoot, ".craft-plugin", "plugin.json");
        Directory.CreateDirectory(Path.GetDirectoryName(declaredManifest)!);
        File.WriteAllText(
            declaredManifest,
            """
            {
              "schemaVersion": 1,
              "id": "declared-plugin",
              "displayName": "Declared Plugin",
              "capabilities": ["desktop"],
              "desktop": { "entry": "./desktop/dist/index.mjs" }
            }
            """);

        var declaredResult = RunCreator(creator, "declared-plugin", "--with-desktop", "--force", "--path", parent);

        Assert.NotEqual(0, declaredResult.ExitCode);
        Assert.Contains("already declares desktop", declaredResult.StandardError, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(declaredRoot, "desktop")));

        var sourceRoot = Path.Combine(parent, "source-plugin");
        var sourceManifest = Path.Combine(sourceRoot, ".craft-plugin", "plugin.json");
        var sourcePath = Path.Combine(sourceRoot, "desktop", "src", "index.tsx");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceManifest)!);
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        File.WriteAllText(
            sourceManifest,
            """
            {
              "schemaVersion": 1,
              "id": "source-plugin",
              "displayName": "Source Plugin",
              "capabilities": ["skill"],
              "skills": "./skills/"
            }
            """);
        File.WriteAllText(sourcePath, "existing source");

        var sourceResult = RunCreator(creator, "source-plugin", "--with-desktop", "--force", "--path", parent);

        Assert.NotEqual(0, sourceResult.ExitCode);
        Assert.Contains("already exists", sourceResult.StandardError, StringComparison.Ordinal);
        using var unchangedManifest = JsonDocument.Parse(File.ReadAllText(sourceManifest));
        Assert.False(unchangedManifest.RootElement.TryGetProperty("desktop", out _));
        Assert.Equal("existing source", File.ReadAllText(sourcePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string DeployCreator()
    {
        Directory.CreateDirectory(_root);
        var loader = new SkillsLoader(Path.Combine(_root, ".craft"));
        loader.DeployBuiltInSkills();
        return Path.Combine(
            loader.WorkspaceSkillsPath,
            "plugin-creator",
            "scripts",
            "create_basic_plugin.py");
    }

    private ProcessResult RunCreator(string creator, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "python" : "python3",
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(creator);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start Python.");
        _ = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Plugin creator did not finish within 30 seconds.");
        }

        return new ProcessResult(process.ExitCode, standardError);
    }

    private static void AssertCompiles(string source)
    {
        var platformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            ?? [];
        var references = platformAssemblies
            .Concat(AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                .Select(assembly => assembly.Location))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "MyPlugin.Plugin",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.ToString())
            .ToArray();
        Assert.Empty(errors);
    }

    private sealed record ProcessResult(int ExitCode, string StandardError);
}
