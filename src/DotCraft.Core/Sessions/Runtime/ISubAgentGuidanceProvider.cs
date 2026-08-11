namespace DotCraft.Sessions;

/// <summary>Contributes stable, purpose-specific instructions to native SubAgent context.</summary>
public interface ISubAgentGuidanceProvider
{
    /// <summary>Returns stable guidance for a child thread, or <see langword="null"/> when not applicable.</summary>
    string? GetGuidance(SessionThread thread);
}
