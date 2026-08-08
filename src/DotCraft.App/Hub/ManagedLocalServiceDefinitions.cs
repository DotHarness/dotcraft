namespace DotCraft.Hub;

/// <summary>
/// Fixed launch metadata for a product-owned local service.
/// </summary>
public sealed record ManagedLocalServiceDefinition(
    string ServiceId,
    string StateRoot,
    string HealthPath);

/// <summary>
/// Product composition for local services understood by this DotCraft build.
/// </summary>
public static class ManagedLocalServiceDefinitions
{
    /// <summary>
    /// Creates the closed set of product-owned services available to Hub.
    /// </summary>
    public static IReadOnlyList<ManagedLocalServiceDefinition> CreateBuiltIns(HubPaths paths) =>
    [
        new(
            ServiceId: "oratorio",
            StateRoot: Path.Combine(paths.CraftHomePath, "oratorio"),
            HealthPath: "/health")
    ];
}
