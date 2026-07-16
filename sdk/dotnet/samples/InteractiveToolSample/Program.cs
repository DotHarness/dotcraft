using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Minimal binding-scoped Streamable HTTP MCP + MCP Apps server.
// Start it with a one-time bearer, then submit the printed endpoint and bearer
// through app/binding/activate (or app/binding/rebind) from an authenticated app principal.

var port = args.Length > 0 && int.TryParse(args[0], out var parsed) ? parsed : 5199;
var bearer = args.Length > 1 ? args[1] : Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
var endpoint = $"http://127.0.0.1:{port}/mcp/";
const string resourceUri = "ui://dotcraft-sample/card";

using var listener = new HttpListener();
listener.Prefixes.Add(endpoint);
listener.Start();
Console.WriteLine($"Binding MCP endpoint: {endpoint}");
Console.WriteLine($"One-time bearer: {bearer}");
Console.WriteLine("Submit these values with app/binding/activate. Press Ctrl+C to stop.");

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; stop.Cancel(); listener.Stop(); };

while (!stop.IsCancellationRequested)
{
    HttpListenerContext context;
    try { context = await listener.GetContextAsync(); }
    catch (HttpListenerException) when (stop.IsCancellationRequested) { break; }

    _ = Task.Run(async () =>
    {
        if (!string.Equals(context.Request.Headers["Authorization"], $"Bearer {bearer}", StringComparison.Ordinal))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            context.Response.Close();
            return;
        }

        using var request = await JsonDocument.ParseAsync(context.Request.InputStream);
        var root = request.RootElement;
        if (!root.TryGetProperty("id", out var id))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Accepted;
            context.Response.Close();
            return;
        }

        var method = root.GetProperty("method").GetString();
        object result = method switch
        {
            "initialize" => new
            {
                protocolVersion = "2025-06-18",
                capabilities = new { tools = new { }, resources = new { } },
                serverInfo = new { name = "dotcraft-binding-sample", version = "2.0.0" }
            },
            "tools/list" => new
            {
                tools = new[]
                {
                    new
                    {
                        name = "show_card",
                        title = "Show sample card",
                        description = "Returns a small MCP App card.",
                        inputSchema = new { type = "object", properties = new { message = new { type = "string" } }, additionalProperties = false },
                        _meta = new Dictionary<string, object> { ["ui/resourceUri"] = resourceUri }
                    }
                }
            },
            "resources/list" => new { resources = new[] { new { uri = resourceUri, name = "Sample card", mimeType = "text/html;profile=mcp-app" } } },
            "resources/templates/list" => new { resourceTemplates = Array.Empty<object>() },
            "resources/read" => new
            {
                contents = new[]
                {
                    new
                    {
                        uri = resourceUri,
                        mimeType = "text/html;profile=mcp-app",
                        text = "<!doctype html><meta charset=utf-8><style>body{font:14px system-ui;padding:16px}article{border:1px solid #ddd;border-radius:12px;padding:16px}</style><article><strong>Binding MCP App</strong><p>This view came from a binding-scoped MCP resource.</p></article>"
                    }
                }
            },
            "tools/call" => new
            {
                content = new[] { new { type = "text", text = "Displayed the binding MCP App card." } },
                structuredContent = new { displayed = true },
                _meta = new Dictionary<string, object> { ["ui/resourceUri"] = resourceUri }
            },
            _ => new { }
        };

        var response = JsonSerializer.SerializeToUtf8Bytes(new { jsonrpc = "2.0", id = id.Clone(), result });
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = response.Length;
        await context.Response.OutputStream.WriteAsync(response);
        context.Response.Close();
    }, stop.Token);
}
