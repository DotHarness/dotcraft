namespace DotCraft.Tools;

/// <summary>A case-sensitive canonical tool name with an optional namespace.</summary>
public readonly record struct ToolName
{
    /// <summary>Creates a canonical tool name.</summary>
    public ToolName(string? @namespace, string name)
    {
        if (@namespace is not null && string.IsNullOrWhiteSpace(@namespace))
            throw new ArgumentException("A tool namespace must be non-empty when supplied.", nameof(@namespace));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A tool name is required.", nameof(name));
        if (@namespace is not null && !IsSafeComponent(@namespace))
            throw new ArgumentException("A tool namespace must contain only ASCII letters, digits, and underscores.", nameof(@namespace));
        if (!IsSafeComponent(name))
            throw new ArgumentException("A tool name must contain only ASCII letters, digits, and underscores.", nameof(name));
        var flatLength = name.Length + (@namespace is null ? 0 : @namespace.Length + 2);
        if (flatLength > ProviderToolProjector.MaximumNameBytes)
            throw new ArgumentException("A flattened tool identity must not exceed 64 ASCII bytes.", nameof(name));

        Namespace = @namespace;
        Name = name;
    }

    /// <summary>Gets the namespace, or null for a top-level function.</summary>
    public string? Namespace { get; }

    /// <summary>Gets the local name.</summary>
    public string Name { get; }

    /// <inheritdoc />
    public override string ToString() => Namespace is null ? Name : $"{Namespace}.{Name}";

    internal static bool TryCreate(string? @namespace, string? name, out ToolName toolName)
    {
        toolName = default;
        if ((@namespace is not null && string.IsNullOrWhiteSpace(@namespace))
            || string.IsNullOrWhiteSpace(name)
            || (@namespace is not null && !IsSafeComponent(@namespace))
            || !IsSafeComponent(name))
        {
            return false;
        }

        if (name.Length + (@namespace is null ? 0 : @namespace.Length + 2)
            > ProviderToolProjector.MaximumNameBytes)
        {
            return false;
        }

        toolName = new ToolName(@namespace, name);
        return true;
    }

    private static bool IsSafeComponent(string value) =>
        value.All(static character => character is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '_');
}
