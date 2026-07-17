namespace DotCraft.Core.Tests.Protocol;

public sealed class InlineVisualizationBrandingTests
{
    [Fact]
    public void FeatureSourcesUseDotCraftProductSemantics()
    {
        var root = FindRepositoryRoot();
        var banned = string.Concat("co", "dex");
        var files = new List<string>();
        files.AddRange(Directory.EnumerateFiles(
            Path.Combine(root, "src", "DotCraft.Core", "Protocol", "InlineVisualizations"),
            "*",
            SearchOption.AllDirectories));
        files.Add(Path.Combine(root, "src", "DotCraft.Core", "Skills", "BuiltIn", "visualize", "SKILL.md"));
        files.AddRange(Directory.EnumerateFiles(
            Path.Combine(root, "desktop", "src", "renderer", "components", "conversation"),
            "*InlineVisualization*",
            SearchOption.TopDirectoryOnly));
        files.AddRange(Directory.EnumerateFiles(
            Path.Combine(root, "desktop", "src", "renderer", "components", "conversation"),
            "inlineVisualization*",
            SearchOption.TopDirectoryOnly));
        files.Add(Path.Combine(root, "specs", "protocols", "tool-result-presentation.md"));

        foreach (var file in files)
            Assert.DoesNotContain(banned, File.ReadAllText(file), StringComparison.OrdinalIgnoreCase);

        var protocol = File.ReadAllText(Path.Combine(root, "specs", "protocols", "appserver-protocol.md"));
        var start = protocol.IndexOf("### 22.10A Inline Visualization Views", StringComparison.Ordinal);
        var end = protocol.IndexOf("### 22.11 Error Codes", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        Assert.DoesNotContain(banned, protocol[start..end], StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "dotcraft.sln")))
                return directory;
            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
