namespace DotCraft.Tools;

/// <summary>
/// Marks an abstract or interface method as a declaration-only generated tool contract.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ToolDeclarationAttribute : Attribute
{
    /// <summary>
    /// Overrides the model-visible tool name. The method name is used when omitted.
    /// </summary>
    public string? Name { get; set; }
}

/// <summary>
/// Controls the model-visible declaration of one generated tool parameter.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class ToolParameterAttribute : Attribute
{
    /// <summary>
    /// Overrides the JSON property name. The C# parameter name is used when omitted.
    /// </summary>
    public string? Name { get; set; }
}
