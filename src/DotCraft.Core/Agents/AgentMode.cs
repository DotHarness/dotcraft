namespace DotCraft.Agents;

/// <summary>
/// Operational modes for the DotCraft agent.
/// </summary>
public enum AgentMode
{
    /// <summary>
    /// Full agent mode with all tools available (read, write, execute).
    /// </summary>
    Agent,

    /// <summary>
    /// Read-only planning mode. Write/execute tools are disabled.
    /// </summary>
    Plan
}

/// <summary>
/// Lightweight per-session state holder for agent mode.
/// Tracks current and previous mode to support prompt injection on transitions.
/// </summary>
public sealed class AgentModeManager
{
    private AgentMode _currentMode = AgentMode.Agent;

    public AgentMode CurrentMode => _currentMode;

    public AgentMode? PreviousMode { get; private set; }

    /// <summary>
    /// Fired after the mode changes. Subscribers receive the new mode.
    /// </summary>
    public event Action<AgentMode>? ModeChanged;

    /// <summary>
    /// Switch to a new mode. No-op if already in the requested mode.
    /// </summary>
    public void SwitchMode(AgentMode newMode)
    {
        if (_currentMode == newMode)
            return;

        PreviousMode = _currentMode;
        _currentMode = newMode;
        ModeChanged?.Invoke(newMode);
    }

    /// <summary>
    /// Toggle between Agent and Plan modes.
    /// </summary>
    public AgentMode ToggleMode()
    {
        var next = _currentMode == AgentMode.Agent ? AgentMode.Plan : AgentMode.Agent;
        SwitchMode(next);
        return next;
    }

    /// <summary>
    /// True when the most recent transition was Plan -> Agent.
    /// Used to inject the agent-switch prompt.
    /// </summary>
    public bool JustSwitchedFromPlan =>
        PreviousMode == AgentMode.Plan && _currentMode == AgentMode.Agent;

    /// <summary>
    /// Clear the previous-mode flag after the transition prompt has been injected.
    /// </summary>
    public void AcknowledgeTransition()
    {
        PreviousMode = null;
    }
}
