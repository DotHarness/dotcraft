namespace DotCraft.Configuration;

/// <summary>
/// Declares when a config field takes effect after it is changed.
/// </summary>
public enum ReloadBehavior
{
    /// <summary>
    /// The change takes effect only after the AppServer process restarts.
    /// </summary>
    ProcessRestart = 0,

    /// <summary>
    /// The change takes effect after restarting a specific subsystem.
    /// </summary>
    SubsystemRestart = 1,

    /// <summary>
    /// The change is applied immediately at runtime.
    /// </summary>
    Hot = 2
}
