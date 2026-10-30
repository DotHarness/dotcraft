namespace DotCraft.Oratorio.Services;

public static class OratorioStatePaths
{
    public static string ResolveDefaultStateRoot(string? stateRoot = null) =>
        !string.IsNullOrWhiteSpace(stateRoot)
            ? Path.GetFullPath(stateRoot)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".craft", "oratorio");

    public static string ResolveDefaultDatabasePath(string? stateRoot = null) =>
        Path.Combine(ResolveDefaultStateRoot(stateRoot), "oratorio.db");

    public static string ResolveDefaultConfigurationOverlayPath(string? stateRoot = null) =>
        Path.Combine(ResolveDefaultStateRoot(stateRoot), "config.json");
}
