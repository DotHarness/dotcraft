namespace Oratorio.Server.Services;

public static class OratorioStatePaths
{
    public static string ResolveDefaultStateRoot(string contentRootPath, string? stateRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(stateRoot))
        {
            return Path.GetFullPath(stateRoot);
        }

        return Path.GetFullPath(Path.Combine(contentRootPath, "..", ".craft", "oratorio"));
    }

    public static string ResolveDefaultDatabasePath(string contentRootPath, string? stateRoot = null) =>
        Path.Combine(ResolveDefaultStateRoot(contentRootPath, stateRoot), "oratorio.db");

    public static string ResolveDefaultConfigurationOverlayPath(string contentRootPath, string? stateRoot = null) =>
        Path.Combine(ResolveDefaultStateRoot(contentRootPath, stateRoot), "config.json");
}
