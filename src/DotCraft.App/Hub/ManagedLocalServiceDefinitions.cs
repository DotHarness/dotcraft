namespace DotCraft.Hub;

public sealed record ManagedLocalServiceDefinition(
    string ServiceId,
    string StateRoot,
    string HealthPath,
    IReadOnlyList<string> Arguments);

public static class ManagedLocalServiceDefinitions
{
    public static IReadOnlyList<ManagedLocalServiceDefinition> CreateBuiltIns(HubPaths paths) =>
    [
        new(
            ServiceId: "oratorio",
            StateRoot: Path.Combine(paths.CraftHomePath, "oratorio"),
            HealthPath: "/health",
            Arguments: ["oratorio", "serve"])
    ];
}
