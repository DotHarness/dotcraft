namespace DotCraft.Mcp;

internal static class McpAuthenticationStatuses
{
    public const string Unsupported = "unsupported";
    public const string NotLoggedIn = "notLoggedIn";
    public const string BearerToken = "bearerToken";
    public const string OAuth = "oAuth";
}

internal sealed class McpAuthenticationRequiredException(bool reauthenticationRequired)
    : InvalidOperationException(reauthenticationRequired
        ? "MCP authentication must be renewed."
        : "MCP authentication is required.")
{
    public bool ReauthenticationRequired { get; } = reauthenticationRequired;
}
