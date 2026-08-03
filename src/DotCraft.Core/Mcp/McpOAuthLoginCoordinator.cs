using System.Net;
using System.Net.Sockets;
using ModelContextProtocol.Authentication;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;

namespace DotCraft.Mcp;

/// <summary>Runs an MCP OAuth authorization-code flow with an explicit loopback callback.</summary>
internal static class McpOAuthLoginCoordinator
{
    /// <summary>
    /// Starts a login, returns the authorization URL as soon as the SDK produces it, and reports
    /// terminal completion through <paramref name="onCompleted"/>.
    /// </summary>
    public static async Task<string> BeginAsync(
        McpServerConfig server,
        IReadOnlyList<string>? scopes,
        double? timeoutSecs,
        Func<bool, string?, Task> onCompleted,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(server.NormalizedTransport, "streamableHttp", StringComparison.Ordinal))
            throw new InvalidOperationException("MCP OAuth is only available for streamable HTTP servers.");
        if (!Uri.TryCreate(server.Url, UriKind.Absolute, out var endpoint))
            throw new InvalidOperationException("The MCP server endpoint is invalid.");

        var tokenCache = McpOAuthTokenStore.Create(server);
        await tokenCache.ClearAsync(cancellationToken);

        var port = ReserveLoopbackPort();
        var redirectUri = new Uri($"http://127.0.0.1:{port}/callback/");
        var authorizationUrl = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri.AbsoluteUri);
        listener.Start();

        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeoutSecs is > 0)
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Min(timeoutSecs.Value, 3600)));
        var loginCancellationToken = timeoutCts.Token;
        var oauth = new ClientOAuthOptions
        {
            RedirectUri = redirectUri,
            TokenCache = tokenCache,
            Scopes = scopes,
            AuthorizationRedirectDelegate = async (url, _, ct) =>
            {
                if (!IsSafeAuthorizationUrl(url))
                    throw new InvalidOperationException("The MCP authorization URL must use HTTPS or loopback HTTP.");
                authorizationUrl.TrySetResult(url.AbsoluteUri);
                return await WaitForAuthorizationCodeAsync(listener, ct);
            }
        };

        _ = Task.Run(async () =>
        {
            var terminalCompletionAttempted = false;
            try
            {
                await using var client = await McpClientManager.CreateClientAsync(
                    server,
                    elicitationHandler: null,
                    oauthOptions: oauth,
                    cancellationToken: loginCancellationToken);
                _ = await client.ListToolsAsync(cancellationToken: loginCancellationToken);
                if (!authorizationUrl.Task.IsCompletedSuccessfully)
                {
                    authorizationUrl.TrySetException(
                        new InvalidOperationException("The MCP server did not request OAuth authorization."));
                    return;
                }
                terminalCompletionAttempted = true;
                await onCompleted(true, null);
            }
            catch (Exception ex)
            {
                if (authorizationUrl.Task.IsCompletedSuccessfully && !terminalCompletionAttempted)
                {
                    terminalCompletionAttempted = true;
                    await onCompleted(false, ex.Message);
                }
                else
                    authorizationUrl.TrySetException(ex);
            }
            finally
            {
                listener.Close();
                timeoutCts.Dispose();
            }
        }, CancellationToken.None);

        try
        {
            return await authorizationUrl.Task.WaitAsync(TimeSpan.FromSeconds(30), loginCancellationToken);
        }
        catch
        {
            timeoutCts.Cancel();
            listener.Close();
            throw;
        }
    }

    private static async Task<string> WaitForAuthorizationCodeAsync(
        HttpListener listener,
        CancellationToken cancellationToken)
    {
        var context = await listener.GetContextAsync().WaitAsync(cancellationToken);
        var query = context.Request.QueryString;
        var error = query["error"];
        var code = query["code"];
        var responseText = string.IsNullOrWhiteSpace(error) && !string.IsNullOrWhiteSpace(code)
            ? "Authentication complete. You can return to DotCraft."
            : "Authentication failed. You can return to DotCraft.";
        var bytes = System.Text.Encoding.UTF8.GetBytes(responseText);
        context.Response.StatusCode = string.IsNullOrWhiteSpace(error) ? 200 : 400;
        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
        context.Response.Close();

        if (!string.IsNullOrWhiteSpace(error))
            throw new InvalidOperationException($"MCP OAuth authorization failed: {error}.");
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("MCP OAuth callback did not contain an authorization code.");
        return code;
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static bool IsSafeAuthorizationUrl(Uri uri) =>
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
        (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback);
}
