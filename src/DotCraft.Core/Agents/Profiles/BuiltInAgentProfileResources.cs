using System.Reflection;

namespace DotCraft.Agents;

internal sealed record BuiltInAgentProfileDefinition(string Id, string RawContent);

internal static class BuiltInAgentProfileResources
{
    private const string ResourcePrefix = "DotCraft.Agents.Profiles.BuiltIn.";
    private const string MarkdownSuffix = ".md";

    internal static IReadOnlyList<BuiltInAgentProfileDefinition> Load()
    {
        var assembly = typeof(BuiltInAgentProfileResources).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && name.EndsWith(MarkdownSuffix, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => Load(assembly, name))
            .ToArray();
    }

    private static BuiltInAgentProfileDefinition Load(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var id = resourceName[ResourcePrefix.Length..^MarkdownSuffix.Length];
        return new BuiltInAgentProfileDefinition(id, reader.ReadToEnd());
    }
}
