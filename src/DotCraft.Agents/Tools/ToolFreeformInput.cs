namespace DotCraft.Tools;

public sealed record ToolFreeformInput
{
    public ToolFreeformInput(string parameterName, string syntax, string definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        ArgumentException.ThrowIfNullOrWhiteSpace(syntax);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition);
        ParameterName = parameterName;
        Syntax = syntax;
        Definition = definition;
    }

    public string ParameterName { get; }

    public string Syntax { get; }

    public string Definition { get; }
}
