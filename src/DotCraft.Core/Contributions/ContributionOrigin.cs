namespace DotCraft.Contributions;

/// <summary>The kind of component that owns a contribution.</summary>
public enum ContributionOriginKind
{
    /// <summary>The composition root or a kernel subsystem.</summary>
    Builtin,

    /// <summary>A compiled module, identified by module name.</summary>
    Module,

    /// <summary>One activation generation of an installed plugin.</summary>
    Plugin
}

/// <summary>Identifies the owner of a contribution for diagnostics and group disposal.</summary>
public sealed record ContributionOrigin
{
    private ContributionOrigin(ContributionOriginKind kind, string? name, string? generation)
    {
        Kind = kind;
        Name = name;
        Generation = generation;
    }

    /// <summary>Gets the owner kind.</summary>
    public ContributionOriginKind Kind { get; }

    /// <summary>Gets the module name or plugin identifier, or <see langword="null"/> for built-ins.</summary>
    public string? Name { get; }

    /// <summary>Gets the plugin activation generation identifier, or <see langword="null"/> for non-plugin origins.</summary>
    public string? Generation { get; }

    /// <summary>Gets the shared built-in origin.</summary>
    public static ContributionOrigin Builtin { get; } =
        new(ContributionOriginKind.Builtin, null, null);

    /// <summary>Creates a module origin.</summary>
    public static ContributionOrigin Module(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A module name is required.", nameof(name));
        return new ContributionOrigin(ContributionOriginKind.Module, name, null);
    }

    /// <summary>Creates a plugin generation origin.</summary>
    public static ContributionOrigin Plugin(string pluginId, string generationId)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
            throw new ArgumentException("A plugin identifier is required.", nameof(pluginId));
        if (string.IsNullOrWhiteSpace(generationId))
            throw new ArgumentException("A generation identifier is required.", nameof(generationId));
        return new ContributionOrigin(ContributionOriginKind.Plugin, pluginId, generationId);
    }

    /// <summary>Returns the canonical origin string.</summary>
    public override string ToString() => Kind switch
    {
        ContributionOriginKind.Module => $"module:{Name}",
        ContributionOriginKind.Plugin => $"plugin:{Name}@{Generation}",
        _ => "builtin"
    };
}
