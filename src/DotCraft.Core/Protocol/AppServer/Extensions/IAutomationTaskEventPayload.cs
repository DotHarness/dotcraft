namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Marker interface for automation task update payloads that may be forwarded through
/// workspace-runtime events without introducing a dependency from DotCraft.Core back to
/// the automations implementation assembly.
/// </summary>
public interface IAutomationTaskEventPayload
{
}
