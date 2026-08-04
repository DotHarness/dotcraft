using DotCraft.Sessions;

namespace DotCraft.AppServer;

/// <summary>
/// Mutable helper for assembling initialize capabilities from Core and modules.
/// </summary>
public sealed class AppServerCapabilityBuilder(
    ServerCapabilitySnapshot capabilities,
    string? workspaceCraftPath = null)
{
    public ServerCapabilitySnapshot Capabilities { get; } = capabilities;

    public string? WorkspaceCraftPath { get; } = workspaceCraftPath;

    /// <summary>
    /// Adds or replaces a module extension capability entry.
    /// </summary>
    public void SetExtension(string key, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Capabilities.Extensions ??= new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        Capabilities.Extensions[key] = value;
    }
}
