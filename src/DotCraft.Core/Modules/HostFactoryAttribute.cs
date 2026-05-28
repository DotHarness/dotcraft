namespace DotCraft.Modules;

/// <summary>
/// Marks a class as a host factory for a specific module.
/// Used by the source generator to associate factories with modules.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HostFactoryAttribute : Attribute
{
    /// <summary>
    /// Gets the name of the module this factory belongs to.
    /// </summary>
    public string ModuleName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HostFactoryAttribute"/> class.
    /// </summary>
    /// <param name="moduleName">The name of the module this factory belongs to.</param>
    public HostFactoryAttribute(string moduleName)
    {
        ModuleName = moduleName;
    }
}
