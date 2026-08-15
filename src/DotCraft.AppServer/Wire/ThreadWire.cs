namespace DotCraft.AppServer;

public static class RuntimeAdditionalContextKinds
{
    public const string Application = "application";
}

/// <summary>Process-local runtime context attached to a loaded thread.</summary>
public sealed class RuntimeAdditionalContextValue
{
    public string Kind { get; set; } = RuntimeAdditionalContextKinds.Application;

    public string Value { get; set; } = string.Empty;
}
