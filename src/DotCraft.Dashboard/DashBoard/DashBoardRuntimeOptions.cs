namespace DotCraft.DashBoard;

/// <summary>
/// Describes the Dashboard host mode and the capabilities exposed by its HTTP API.
/// </summary>
public sealed record DashBoardRuntimeOptions(
    string Mode,
    bool ReadOnly,
    DashBoardRuntimeCapabilities Capabilities)
{
    /// <summary>
    /// Normal Dashboard mode used by AppServer.
    /// </summary>
    public static DashBoardRuntimeOptions Interactive() => new(
        "interactive",
        ReadOnly: false,
        new DashBoardRuntimeCapabilities(
            Settings: true,
            Dreams: true,
            Automations: true,
            SessionDeletion: true));

    /// <summary>
    /// Standalone trace-inspection mode that exposes no mutating operations.
    /// </summary>
    public static DashBoardRuntimeOptions ReadOnlyViewer() => new(
        "readOnly",
        ReadOnly: true,
        new DashBoardRuntimeCapabilities(
            Settings: false,
            Dreams: false,
            Automations: false,
            SessionDeletion: false));
}

/// <summary>
/// Feature switches that determine which Dashboard routes and UI actions are available.
/// </summary>
public sealed record DashBoardRuntimeCapabilities(
    bool Settings,
    bool Dreams,
    bool Automations,
    bool SessionDeletion);
