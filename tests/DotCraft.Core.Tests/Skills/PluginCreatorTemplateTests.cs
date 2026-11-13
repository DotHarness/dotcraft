using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DotCraft.Plugins;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Skills;

public sealed class PluginCreatorTemplateTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "dotcraft-plugin-creator-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void DotnetTemplate_CompilesAgainstTheHostApi()
    {
        var source = ReadTemplate("template-dotnet-extension.txt")
            .Replace("[TODO: PascalCaseId]", "MyPlugin", StringComparison.Ordinal);
        source = Regex.Replace(source, @"\[TODO: [^\]]*\]", "my_plugin");

        AssertCompiles(source);
    }

    [Fact]
    public async Task ManifestTemplate_FailsVerificationUntilFilledIn()
    {
        var pluginRoot = Path.Combine(_root, "plugin");
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "assets"));
        var manifestPath = Path.Combine(pluginRoot, ".craft-plugin", "plugin.json");
        File.WriteAllText(manifestPath, ReadTemplate("template-plugin.json").Replace(
            "[TODO: lowercase hyphen-case id]", "demo-plugin", StringComparison.Ordinal));
        var icon = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><rect width="64" height="64"/></svg>""";
        File.WriteAllText(Path.Combine(pluginRoot, "assets", "logo.svg"), icon);
        File.WriteAllText(Path.Combine(pluginRoot, "assets", "icon.svg"), icon);

        var unfilled = await PluginVerifier.VerifyAsync(new PluginVerificationRequest(pluginRoot));

        Assert.False(unfilled.IsValid);
        Assert.Contains(unfilled.Diagnostics, item => item.Code == "PluginPlaceholderRemaining");

        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest["displayName"] = "Demo Plugin";
        manifest["description"] = "Shows a demo workflow.";
        manifest["capabilities"] = new JsonArray("metadata");
        var metadata = manifest["interface"]!.AsObject();
        metadata["displayName"] = "Demo Plugin";
        metadata["shortDescription"] = "Show a demo workflow";
        metadata["developerName"] = "Acme";
        metadata["category"] = "Productivity";
        metadata["capabilities"] = new JsonArray("Demo");
        metadata["defaultPrompt"] = "Show me the demo workflow.";
        File.WriteAllText(manifestPath, manifest.ToJsonString());

        var filled = await PluginVerifier.VerifyAsync(new PluginVerificationRequest(pluginRoot));

        Assert.True(filled.IsValid, string.Join(Environment.NewLine, filled.Diagnostics.Select(item => item.Message)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string ReadTemplate(string name)
    {
        Directory.CreateDirectory(_root);
        var loader = new SkillsLoader(Path.Combine(_root, ".craft"));
        loader.DeployBuiltInSkills();
        return File.ReadAllText(Path.Combine(loader.WorkspaceSkillsPath, "plugin-creator", "assets", name));
    }

    private static void AssertCompiles(string source)
    {
        var platformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            ?? [];
        var references = platformAssemblies
            .Append(typeof(IDotCraftPlugin).Assembly.Location)
            .Append(typeof(AIFunctionToolSource).Assembly.Location)
            .Append(typeof(AIFunction).Assembly.Location)
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
}
