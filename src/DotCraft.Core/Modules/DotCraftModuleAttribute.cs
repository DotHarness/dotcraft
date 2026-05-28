namespace DotCraft.Modules;

/// <summary>
/// Marks a class as a DotCraft module for automatic discovery by the source generator.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DotCraftModuleAttribute : Attribute
{
    /// <summary>
    /// Gets the unique name of the module.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets or sets the priority of the module (higher = more important).
    /// Default is 0.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Gets or sets the description of the module.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets whether the module can be selected as the primary startup host.
    /// Background capability modules should leave this as <see langword="false"/>.
    /// </summary>
    public bool CanBePrimaryHost { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DotCraftModuleAttribute"/> class.
    /// </summary>
    /// <param name="name">The unique name of the module.</param>
    public DotCraftModuleAttribute(string name)
    {
        Name = name;
        Priority = 0;
        CanBePrimaryHost = false;
    }
}
