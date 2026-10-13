using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace DotCraft.AppServerTestClient;

internal sealed class McpOAuthSmokeServer : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ClientRegistration> _clients = new();
    private readonly ConcurrentDictionary<string, Grant> _codes = new();
    private readonly ConcurrentDictionary<string, string> _access = new();
    private readonly ConcurrentDictionary<string, string> _refresh = new();
    private WebApplication? _app;
    private int _registrations;
    private int _authorizations;
    private int _refreshes;
    private int _oldClientRequests;

    public string BaseUrl { get; private set; } = string.Empty;
    public string McpUrl => BaseUrl + "/mcp";
    public int RegistrationRequests => _registrations;
    public int AuthorizationRequests => _authorizations;
    public int RefreshRequests => _refreshes;
    public int OldClientRequests => _oldClientRequests;
    public bool AllowDynamicRegistration { get; init; }

    public void RegisterClient(string id, string secret, string callback) =>
        _clients[id] = new(secret, callback);

    public void ExpireAccessTokens() => _access.Clear();

    public void RotateSecret(string id, string secret) =>
        _clients[id] = _clients[id] with { Secret = secret };

    public async Task StartAsync(int port = 0)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        var app = builder.Build();
        _app = app;
        app.MapGet("/.well-known/oauth-protected-resource/mcp", () => Results.Json(new
        {
            resource = McpUrl,
            authorization_servers = new[] { BaseUrl },
            scopes_supported = new[] { "tools:read" }
        }));
        app.MapGet("/.well-known/oauth-authorization-server", () => Results.Json(new
        {
            issuer = BaseUrl,
            authorization_endpoint = BaseUrl + "/authorize",
            token_endpoint = BaseUrl + "/token",
            registration_endpoint = AllowDynamicRegistration ? BaseUrl + "/register" : null,
            response_types_supported = new[] { "code" },
            grant_types_supported = new[] { "authorization_code", "refresh_token" },
            token_endpoint_auth_methods_supported = new[] { "client_secret_post" },
            code_challenge_methods_supported = new[] { "S256" },
            scopes_supported = new[] { "tools:read", "offline_access" },
            authorization_response_iss_parameter_supported = true
        }));
        app.MapGet("/authorize", (HttpContext context) => Authorize(context));
        app.MapPost("/token", (Func<HttpContext, Task<IResult>>)TokenAsync);
        app.MapPost("/register", async (HttpContext context) =>
        {
            Interlocked.Increment(ref _registrations);
            if (!AllowDynamicRegistration) return Results.BadRequest(new { error = "registration_not_supported" });
            using var body = await JsonDocument.ParseAsync(context.Request.Body);
            var callback = body.RootElement.GetProperty("redirect_uris")[0].GetString()!;
            RegisterClient("dynamic-client", "dynamic-secret", callback);
            return Results.Json(new { client_id = "dynamic-client", client_secret = "dynamic-secret", token_endpoint_auth_method = "client_secret_post" });
        });
        app.MapMethods("/mcp", ["GET", "POST", "DELETE"], (Func<HttpContext, Task<IResult>>)McpAsync);
        app.MapPost("/control/expire", () => { ExpireAccessTokens(); return Results.Ok(); });
        app.MapPost("/control/rotate/{id}", async (string id, HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync();
            if (!_clients.ContainsKey(id) || string.IsNullOrWhiteSpace(form["secret"])) return Results.BadRequest();
            RotateSecret(id, form["secret"]!);
            return Results.Ok();
        });
        app.MapGet("/control/stats", () => Results.Json(new
        {
            registrations = RegistrationRequests,
            authorizations = AuthorizationRequests,
            refreshes = RefreshRequests,
            oldClientRequests = OldClientRequests
        }));
        await app.StartAsync();
        BaseUrl = app.Urls.Single().TrimEnd('/');
    }

    private IResult Authorize(HttpContext context)
    {
        Interlocked.Increment(ref _authorizations);
        var query = context.Request.Query;
        var id = query["client_id"].ToString();
        var callback = query["redirect_uri"].ToString();
        var challenge = query["code_challenge"].ToString();
        if (!_clients.TryGetValue(id, out var client) || callback != client.Callback
            || query["response_type"] != "code" || query["code_challenge_method"] != "S256"
            || challenge.Length == 0 || query["state"].ToString().Length == 0)
            return Results.BadRequest(new { error = "invalid_request" });
        var code = Guid.NewGuid().ToString("N");
        _codes[code] = new(id, callback, challenge);
        return Results.Redirect(callback + "?code=" + code + "&state=" + Uri.EscapeDataString(query["state"]!)
            + "&iss=" + Uri.EscapeDataString(BaseUrl));
    }

    private async Task<IResult> TokenAsync(HttpContext context)
    {
        var form = await context.Request.ReadFormAsync();
        var id = form["client_id"].ToString();
        if (!_clients.TryGetValue(id, out var client) || client.Secret != form["client_secret"].ToString())
            return Results.Json(new { error = "invalid_client" }, statusCode: 401);
        if (form["resource"] != McpUrl) return Results.BadRequest(new { error = "invalid_target" });
        if (form["grant_type"] == "authorization_code")
        {
            if (!_codes.TryRemove(form["code"].ToString(), out var grant) || grant.ClientId != id
                || grant.Callback != form["redirect_uri"].ToString() || grant.Challenge != Challenge(form["code_verifier"]!))
                return Results.BadRequest(new { error = "invalid_grant" });
        }
        else if (form["grant_type"] == "refresh_token")
        {
            Interlocked.Increment(ref _refreshes);
            if (!_refresh.TryRemove(form["refresh_token"].ToString(), out var owner) || owner != id)
            {
                Interlocked.Increment(ref _oldClientRequests);
                return Results.BadRequest(new { error = "invalid_grant" });
            }
        }
        else return Results.BadRequest(new { error = "unsupported_grant_type" });
        var access = Guid.NewGuid().ToString("N");
        var refresh = Guid.NewGuid().ToString("N");
        _access[access] = id;
        _refresh[refresh] = id;
        return Results.Json(new { access_token = access, refresh_token = refresh, token_type = "Bearer", expires_in = 3600, scope = "tools:read" });
    }

    private async Task<IResult> McpAsync(HttpContext context)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        var token = authorization.StartsWith("Bearer ", StringComparison.Ordinal) ? authorization[7..] : string.Empty;
        if (!_access.ContainsKey(token))
        {
            context.Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{BaseUrl}/.well-known/oauth-protected-resource/mcp\", scope=\"tools:read\"";
            return Results.Unauthorized();
        }
        if (context.Request.Method != "POST") return Results.StatusCode(405);
        using var document = await JsonDocument.ParseAsync(context.Request.Body);
        var root = document.RootElement;
        if (!root.TryGetProperty("id", out var id)) return Results.StatusCode(202);
        var method = root.GetProperty("method").GetString();
        object result = method switch
        {
            "initialize" => new { protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(), capabilities = new { tools = new { } }, serverInfo = new { name = "oauth-smoke", version = "1.0.0" } },
            "tools/list" => new { tools = new[] { new { name = "echo", description = "Echo a smoke-test message.", inputSchema = new { type = "object", properties = new { message = new { type = "string" } }, required = new[] { "message" } } } } },
            "tools/call" => new { content = new[] { new { type = "text", text = root.GetProperty("params").GetProperty("arguments").GetProperty("message").GetString() } }, isError = false },
            _ => new { }
        };
        return Results.Json(new { jsonrpc = "2.0", id = id.Clone(), result });
    }

    private static string Challenge(string verifier) => Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public async ValueTask DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
    }

    public static async Task<int> RunAsync(IReadOnlyList<string> args)
    {
        var port = 18554;
        var callbackPort = 18555;
        for (var index = 0; index < args.Count; index++)
        {
            if (args[index] == "--port") port = int.Parse(args[++index]);
            else if (args[index] == "--callback-port") callbackPort = int.Parse(args[++index]);
            else throw new ArgumentException($"Unknown argument: {args[index]}");
        }
        await using var server = new McpOAuthSmokeServer();
        var callback = $"http://127.0.0.1:{callbackPort}/callback/";
        server.RegisterClient("smoke-client-a", "smoke-secret-a", callback);
        server.RegisterClient("smoke-client-b", "smoke-secret-b", callback);
        await server.StartAsync(port);
        Console.WriteLine($"MCP: {server.McpUrl}\nCallback: {callback}\nClients: smoke-client-a / smoke-client-b\nTest secrets: smoke-secret-a / smoke-secret-b\nPOST {server.BaseUrl}/control/expire to expire access tokens.");
        await server._app!.WaitForShutdownAsync();
        return 0;
    }

    private sealed record ClientRegistration(string Secret, string Callback);
    private sealed record Grant(string ClientId, string Callback, string Challenge);
}
