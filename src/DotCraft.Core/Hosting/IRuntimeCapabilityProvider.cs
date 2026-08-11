namespace DotCraft.Hosting;

/// <summary>Reports an optional runtime capability owned by a feature module.</summary>
public interface IRuntimeCapabilityProvider
{
    string Capability { get; }
    bool IsAvailable { get; }
}
