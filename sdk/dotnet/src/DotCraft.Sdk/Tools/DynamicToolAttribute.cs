namespace DotCraft.Sdk.Tools;

/// <summary>
/// Marks a method as a Runtime Dynamic Tool. The registry (<see cref="DynamicToolRegistry"/>)
/// reflects every method carrying this attribute, generates its JSON Schema from the
/// parameters, and dispatches calls to it.
/// </summary>
/// <remarks>
/// Two authoring conventions are supported (see <see cref="DynamicToolRegistry"/>):
/// a single typed-arguments record parameter, or flat method parameters annotated with
/// <see cref="System.ComponentModel.DescriptionAttribute"/>. In both cases an optional
/// context parameter and/or a <see cref="System.Threading.CancellationToken"/> may be injected.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class DynamicToolAttribute : Attribute
{
    public DynamicToolAttribute(string name, string? description = null)
    {
        Name = name;
        Description = description;
    }

    /// <summary>Short tool name (without the namespace prefix), e.g. <c>get_dashboard</c>.</summary>
    public string Name { get; }

    /// <summary>
    /// Tool description. When null, the registry falls back to the method's
    /// <see cref="System.ComponentModel.DescriptionAttribute"/>.
    /// </summary>
    public string? Description { get; }

    /// <summary>Sort order for declaration. Tools are listed by (Order, Name).</summary>
    public int Order { get; set; }

    /// <summary>Optional hint that the tool body should be loaded lazily by the host.</summary>
    public bool DeferLoading { get; set; }
}
