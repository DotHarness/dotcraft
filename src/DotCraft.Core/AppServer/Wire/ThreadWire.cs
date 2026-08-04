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

/// <summary>Internal session projection used outside the AppServer contract boundary.</summary>
public sealed class SessionRuntimeSnapshot
{
    public bool Running { get; set; }

    public bool WaitingOnApproval { get; set; }

    public bool WaitingOnInput { get; set; }

    public bool WaitingOnPlanConfirmation { get; set; }

    public bool Busy { get; set; }

    public string? MaintenanceKind { get; set; }
}
