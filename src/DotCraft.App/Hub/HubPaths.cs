using DotCraft.Sessions;

namespace DotCraft.Hub;

/// <summary>
/// Resolved global paths used by Hub.
/// </summary>
public sealed record HubPaths(
    string CraftHomePath,
    string HubStatePath,
    string LockFilePath,
    string GlobalConfigPath)
{
    /// <summary>
    /// Best-effort persisted registry of Hub-known workspace AppServers.
    /// </summary>
    public string AppServersRegistryPath => Path.Combine(HubStatePath, "appservers.json");

    /// <summary>
    /// Persisted runtime tool hints donated by Desktop or configured through the Hub CLI.
    /// </summary>
    public string RuntimeToolsPath => Path.Combine(HubStatePath, "runtime.json");

    /// <summary>
    /// Default local workspace used for projectless Chat entry points.
    /// </summary>
    public string DefaultChatWorkspacePath => Path.Combine(CraftHomePath, "workspaces", "chats");

    /// <summary>
    /// Resolves Hub paths for the current user.
    /// </summary>
    public static HubPaths ForCurrentUser()
        => Resolve();

    /// <summary>
    /// Resolves Hub paths for a specific user profile directory.
    /// </summary>
    /// <param name="userProfilePath">Optional user profile override for tests.</param>
    public static HubPaths Resolve(string? userProfilePath = null)
    {
        var profilePath = string.IsNullOrWhiteSpace(userProfilePath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : userProfilePath;

        var craftHomePath = Path.Combine(profilePath, ".craft");
        var hubStatePath = Path.Combine(craftHomePath, "hub");
        return new HubPaths(
            CraftHomePath: craftHomePath,
            HubStatePath: hubStatePath,
            LockFilePath: Path.Combine(hubStatePath, "hub.lock"),
            GlobalConfigPath: Path.Combine(craftHomePath, "config.json"));
    }
}
