using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>Read-only provider view over Core-owned deferred tool activation state.</summary>
public interface IDeferredToolActivationView
{
    IReadOnlyList<string> GetActivatedToolNames();

    bool TryGetTool(string name, out AITool? tool);
}

/// <summary>Marker carried in chat options for provider-native deferred tool search.</summary>
public interface IDeferredToolSearchMarker
{
    /// <summary>Canonical DotCraft name for the deferred tool search operation.</summary>
    const string CanonicalName = "SearchTools";

    IDeferredToolActivationView Registry { get; }
}
