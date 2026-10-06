namespace DotCraft.Agents.Packages;

public sealed record AgentProfileReferences(
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> McpServers,
    IReadOnlyList<string> Plugins,
    IReadOnlyList<string> ToolNamespaces)
{
    public static readonly AgentProfileReferences None = new([], [], [], []);

    public static AgentProfileReferences Read(string markdown)
    {
        var values = AgentPackageDocument.FrontMatter(markdown);
        if (values is null)
            return None;
        return new AgentProfileReferences(
            Names(Child(values, "skills", "preload")).Concat(Names(Child(values, "skills", "allow"))).Distinct(StringComparer.Ordinal).ToArray(),
            Names(Child(values, "mcp", "servers")).Distinct(StringComparer.Ordinal).ToArray(),
            Names(values.TryGetValue("plugins", out var plugins) ? plugins : null).Distinct(StringComparer.Ordinal).ToArray(),
            Names(Child(values, "tools", "allow"))
                .Select(tool => tool.IndexOf('.') is > 0 and var dot ? tool[..dot] : null)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    private static object? Child(Dictionary<object, object> values, string group, string key) =>
        values.TryGetValue(group, out var node) && node is Dictionary<object, object> map && map.TryGetValue(key, out var child)
            ? child
            : null;

    private static IEnumerable<string> Names(object? node) => node switch
    {
        string text when !string.IsNullOrWhiteSpace(text) && !text.Contains('*') => [text.Trim()],
        IEnumerable<object> items => items.SelectMany(Names),
        Dictionary<object, object> map => map.Values.SelectMany(Names),
        _ => []
    };
}

public sealed record AgentPackageOffer(string Kind, string Name, IReadOnlyList<string> Skills, IReadOnlyList<string> McpServers)
{
    public static AgentPackageOffer From(AgentPackageEntry entry) => new(
        entry.Kind,
        entry.Name,
        entry.Kind == AgentPackageKinds.Skill ? [entry.Name] : entry.Skills,
        entry.McpServers);

    public bool ProvidesSkill(string name) => Skills.Contains(name, StringComparer.OrdinalIgnoreCase);

    public bool ProvidesMcpServer(string name) =>
        Kind == AgentPackageKinds.Plugin
        && McpServers.Any(server => string.Equals(name, $"{Name}:{server}", StringComparison.Ordinal));

    public bool IsPlugin(string name) =>
        Kind == AgentPackageKinds.Plugin && string.Equals(Name, name, StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> Reasons(AgentProfileReferences references)
    {
        var reasons = new List<string>();
        reasons.AddRange(references.Skills.Where(ProvidesSkill).Select(name => $"skill {name}"));
        reasons.AddRange(references.McpServers.Where(ProvidesMcpServer).Select(name => $"MCP server {name}"));
        if (references.Plugins.Any(IsPlugin))
            reasons.Add($"plugin {Name}");
        if (references.ToolNamespaces.Any(IsPlugin))
            reasons.Add($"{Name} tools");
        return reasons;
    }

    public static AgentProfileReferences Unresolved(
        AgentProfileReferences references,
        IReadOnlyCollection<AgentPackageOffer> offers,
        IReadOnlyCollection<string> configuredMcpServers) => new(
        references.Skills.Where(name => !offers.Any(offer => offer.ProvidesSkill(name))).ToArray(),
        references.McpServers
            .Where(name => !configuredMcpServers.Contains(name, StringComparer.Ordinal)
                           && !offers.Any(offer => offer.ProvidesMcpServer(name)))
            .ToArray(),
        references.Plugins.Where(name => !offers.Any(offer => offer.IsPlugin(name))).ToArray(),
        []);
}
