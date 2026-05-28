using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotCraft.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotCraft.DashBoard;

public static class DashBoardAuth
{
    private const string CookieName = "dotcraft_dashboard_session";
    private const string LoginPath = "/dashboard/login";

    private static readonly HashSet<string> PublicPaths =
        [LoginPath, "/dashboard/login/"];

    /// <summary>
    /// Random nonce generated once per process lifetime.
    /// Ensures all session tokens become invalid after a server restart,
    /// even if the credentials remain unchanged.
    /// </summary>
    private static readonly string ServerNonce = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Tokens that have been explicitly revoked via the logout endpoint.
    /// Checked during auth validation so a stolen cookie cannot be reused
    /// after the legitimate user has logged out.
    /// </summary>
    private static readonly ConcurrentDictionary<string, byte> RevokedTokens = new();

    /// <summary>
    /// Returns true when dashboard auth is configured (both username and password are non-empty).
    /// </summary>
    public static bool IsEnabled(AppConfig config) =>
        !string.IsNullOrEmpty(config.DashBoard.Username) &&
        !string.IsNullOrEmpty(config.DashBoard.Password);

    /// <summary>
    /// Registers the auth middleware that protects all /dashboard/ routes.
    /// Must be called before MapDashBoard.
    /// </summary>
    public static void UseDashBoardAuth(this IApplicationBuilder app, AppConfig config)
    {
        if (!IsEnabled(config)) return;

        var logger = app.ApplicationServices.GetService<ILoggerFactory>()?.CreateLogger("DashBoard.Auth");

        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path.Value ?? "";
            if (!path.StartsWith("/dashboard", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            if (PublicPaths.Contains(path))
            {
                await next();
                return;
            }

            if (ctx.Request.Cookies.TryGetValue(CookieName, out var cookie) && IsTokenValid(cookie))
            {
                await next();
                return;
            }

            if (IsApiRequest(path))
            {
                logger?.LogDebug("Unauthorized dashboard API request: {Path}", path);
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"error":"unauthorized"}""");
                return;
            }

            ctx.Response.Redirect(LoginPath);
        });
    }

    /// <summary>
    /// Maps login/logout endpoints. Must be called before MapDashBoard so that
    /// the login page route takes precedence.
    /// </summary>
    public static void MapDashBoardAuth(this IApplicationBuilder app, AppConfig config)
    {
        if (!IsEnabled(config)) return;

        var logger = app.ApplicationServices.GetService<ILoggerFactory>()?.CreateLogger("DashBoard.Auth");

        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path.Value ?? "";

            // GET /dashboard/login — serve login page
            if (path.TrimEnd('/').Equals("/dashboard/login", StringComparison.OrdinalIgnoreCase)
                && ctx.Request.Method == "GET")
            {
                if (ctx.Request.Cookies.TryGetValue(CookieName, out var c) && IsTokenValid(c))
                {
                    ctx.Response.Redirect("/dashboard/");
                    return;
                }

                ctx.Response.ContentType = "text/html; charset=utf-8";
                await ctx.Response.WriteAsync(DashBoardFrontend.GetLoginHtml());
                return;
            }

            // POST /dashboard/login — validate credentials
            if (path.TrimEnd('/').Equals("/dashboard/login", StringComparison.OrdinalIgnoreCase)
                && ctx.Request.Method == "POST")
            {
                LoginRequest? body;
                try
                {
                    body = await JsonSerializer.DeserializeAsync<LoginRequest>(ctx.Request.Body, JsonOptions);
                }
                catch
                {
                    ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync("""{"error":"invalid request body"}""");
                    return;
                }

                if (body != null
                    && body.Username == config.DashBoard.Username
                    && body.Password == config.DashBoard.Password)
                {
                    var token = IssueToken();
                    ctx.Response.Cookies.Append(CookieName, token, new CookieOptions
                    {
                        HttpOnly = true,
                        SameSite = SameSiteMode.Strict,
                        Path = "/dashboard",
                        MaxAge = TimeSpan.FromDays(7)
                    });
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync("""{"ok":true}""");
                    return;
                }

                logger?.LogWarning("Dashboard login failed for user {Username}", body?.Username ?? "(unknown)");
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"error":"invalid username or password"}""");
                return;
            }

            // POST /dashboard/logout
            if (path.TrimEnd('/').Equals("/dashboard/logout", StringComparison.OrdinalIgnoreCase)
                && ctx.Request.Method == "POST")
            {
                if (ctx.Request.Cookies.TryGetValue(CookieName, out var token))
                    RevokedTokens.TryAdd(token, 0);

                ctx.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/dashboard" });
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("""{"ok":true}""");
                return;
            }

            await next();
        });
    }

    private static bool IsApiRequest(string path) =>
        path.StartsWith("/dashboard/api", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Issues a new per-session token: random component + server nonce,
    /// so every login gets a unique, individually revocable token.
    /// </summary>
    private static string IssueToken()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var payload = $"{ServerNonce}:{sessionId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return $"{sessionId}.{Convert.ToHexStringLower(hash)}";
    }

    /// <summary>
    /// Validates a token by recomputing its HMAC from the embedded session id
    /// and the current server nonce. Tokens from a previous server instance
    /// will fail because the nonce differs. Revoked tokens are also rejected.
    /// </summary>
    private static bool IsTokenValid(string token)
    {
        if (RevokedTokens.ContainsKey(token))
            return false;

        var dotIndex = token.IndexOf('.');
        if (dotIndex <= 0 || dotIndex >= token.Length - 1)
            return false;

        var sessionId = token[..dotIndex];
        var expectedPayload = $"{ServerNonce}:{sessionId}";
        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(expectedPayload)));

        return token[(dotIndex + 1)..] == expectedHash;
    }

    private sealed class LoginRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
