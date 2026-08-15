namespace DotCraft.Modules;

/// <summary>Reports an optional runtime capability owned by a feature module.</summary>
public interface IRuntimeCapabilityProvider
{
    /// <summary>Gets the stable capability identifier.</summary>
    string Capability { get; }

    /// <summary>Gets whether the capability is currently available.</summary>
    bool IsAvailable { get; }
}
