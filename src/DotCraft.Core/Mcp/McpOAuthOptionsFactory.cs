using System.Net;
using System.Net.Sockets;
using ModelContextProtocol.Authentication;

namespace DotCraft.Mcp;

internal static class McpOAuthOptionsFactory
{
    public static void Validate(McpServerConfig server)
    {
        if (server.OAuth is not { } oauth) return;
        if (server.NormalizedTransport != "streamableHttp")
            throw Invalid("OAuth configuration requires streamable HTTP transport.");
        if (!string.IsNullOrWhiteSpace(server.BearerTokenEnvVar)
            || server.Headers.Keys.Concat(server.EnvHttpHeaders.Keys).Any(key => key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)))
            throw Invalid("OAuth configuration cannot be combined with explicit Authorization headers.");
        if ((!string.IsNullOrWhiteSpace(oauth.ClientSecret) || !string.IsNullOrWhiteSpace(oauth.ClientSecretEnvVar))
            && string.IsNullOrWhiteSpace(oauth.ClientId))
            throw Invalid("A client secret requires a client ID.");
        if (oauth.CallbackPort is < 1 or > 65535)
            throw Invalid("Callback port must be between 1 and 65535.");
        if (!string.IsNullOrWhiteSpace(oauth.CallbackUrl))
        {
            if (!Uri.TryCreate(oauth.CallbackUrl.Trim(), UriKind.Absolute, out var uri)
                || uri.Scheme != "http" || !uri.IsLoopback
                || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
                throw Invalid("Callback URL must be an HTTP loopback URL without credentials, query, or fragment.");
            if (HasExplicitPort(uri) && oauth.CallbackPort is { } port && uri.Port != port)
                throw Invalid("Callback URL and callback port must agree.");
        }
    }

    public static string? ResolveSecret(McpServerConfig server)
    {
        Validate(server);
        if (server.OAuth is not { } oauth) return null;
        if (!string.IsNullOrWhiteSpace(oauth.ClientSecretEnvVar))
        {
            var value = Environment.GetEnvironmentVariable(oauth.ClientSecretEnvVar.Trim());
            if (string.IsNullOrWhiteSpace(value))
                throw Invalid($"Required client secret environment variable '{oauth.ClientSecretEnvVar.Trim()}' is missing or empty.");
            return value;
        }
        return string.IsNullOrWhiteSpace(oauth.ClientSecret) ? null : oauth.ClientSecret;
    }

    public static ClientOAuthOptions Create(McpServerConfig server, string? userDataPath, Uri? redirectUri = null) => new()
    {
        ClientId = string.IsNullOrWhiteSpace(server.OAuth?.ClientId) ? null : server.OAuth.ClientId.Trim(),
        ClientSecret = ResolveSecret(server),
        RedirectUri = redirectUri ?? ResolveCallback(server, allocatePort: false),
        TokenCache = McpOAuthTokenStore.Create(server, userDataPath)
    };

    public static Uri ResolveCallback(McpServerConfig server, bool allocatePort)
    {
        Validate(server);
        var uri = new Uri(!string.IsNullOrWhiteSpace(server.OAuth?.CallbackUrl) ? server.OAuth.CallbackUrl.Trim() : "http://127.0.0.1/callback/");
        int? port = server.OAuth?.CallbackPort ?? (HasExplicitPort(uri) ? uri.Port : null);
        if (port is null && allocatePort)
        {
            var address = uri.HostNameType == UriHostNameType.IPv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
            var listener = new TcpListener(address, 0);
            listener.Start();
            try { port = ((IPEndPoint)listener.LocalEndpoint).Port; }
            finally { listener.Stop(); }
        }
        return port is { } selected ? new UriBuilder(uri) { Port = selected }.Uri : uri;
    }

    private static bool HasExplicitPort(Uri uri) =>
        System.Text.RegularExpressions.Regex.IsMatch(uri.OriginalString, @"^http://(?:\[[^\]]+\]|[^/:]+):[0-9]+(?:/|$)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static InvalidOperationException Invalid(string message) => new($"mcp_oauth_config_invalid: {message}");
}
