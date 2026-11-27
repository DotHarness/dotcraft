using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DotCraft.Oratorio.Integrations;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Oratorio.Tests;

public sealed class OratorioMcpEndpointTests
{
    private const string ServiceTokenKey = "Oratorio:Hosting:ServiceToken";

    [Fact]
    public async Task McpEndpoint_RequiresManagedServiceToken_WhenConfigured()
    {
        await using var app = new TestOratorioApp(settings: new Dictionary<string, string?>
        {
            [ServiceTokenKey] = "secret-mcp-token"
        });
        using var client = app.CreateClient();

        using var unauthorized = await PostMcpAsync(client, "initialize", new
        {
            protocolVersion = "2025-03-26",
            capabilities = new { },
            clientInfo = new { name = "test", version = "1.0" }
        });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "secret-mcp-token");
        using var authorized = await PostMcpAsync(client, "initialize", new
        {
            protocolVersion = "2025-03-26",
            capabilities = new { },
            clientInfo = new { name = "test", version = "1.0" }
        });
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_IsNotExposed_WhenManagedServiceTokenIsUnconfigured()
    {
        await using var app = new TestOratorioApp();
        using var client = app.CreateClient();

        using var response = await PostMcpAsync(client, "initialize", new
        {
            protocolVersion = "2025-03-26",
            capabilities = new { },
            clientInfo = new { name = "test", version = "1.0" }
        });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_ServesToolsResourcesAndBoardToolCalls()
    {
        await using var app = new TestOratorioApp(settings: new Dictionary<string, string?>
        {
            [ServiceTokenKey] = "secret-mcp-token"
        });
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "secret-mcp-token");

        using var initResponse = await PostMcpAsync(client, "initialize", new
        {
            protocolVersion = "2025-03-26",
            capabilities = new { },
            clientInfo = new { name = "test", version = "1.0" }
        });
        initResponse.EnsureSuccessStatusCode();
        var sessionId = initResponse.Headers.TryGetValues("Mcp-Session-Id", out var values)
            ? values.FirstOrDefault()
            : null;

        var expectedToolCount = app.Services.GetRequiredService<OratorioDynamicToolCatalog>().McpDescriptors.Count;
        using var toolsDoc = await PostMcpJsonAsync(client, "tools/list", new { }, sessionId);
        var tools = toolsDoc.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
        Assert.Equal(expectedToolCount, tools.Length);
        Assert.Contains(tools, tool => tool.GetProperty("name").GetString() == OratorioDynamicToolCatalog.ListBoardItemsName);

        using var resourcesDoc = await PostMcpJsonAsync(client, "resources/list", new { }, sessionId);
        var resources = resourcesDoc.RootElement.GetProperty("result").GetProperty("resources").EnumerateArray().ToArray();
        Assert.Contains(resources, resource => resource.GetProperty("uri").GetString() == OratorioMcpCatalog.BoardUiResourceUri);

        using var readDoc = await PostMcpJsonAsync(client, "resources/read", new { uri = OratorioMcpCatalog.BoardUiResourceUri }, sessionId);
        var content = Assert.Single(readDoc.RootElement.GetProperty("result").GetProperty("contents").EnumerateArray());
        Assert.Equal("text/html;profile=mcp-app", content.GetProperty("mimeType").GetString());
        Assert.False(string.IsNullOrWhiteSpace(content.GetProperty("text").GetString()));

        using var createDoc = await PostMcpJsonAsync(client, "tools/call", new
        {
            name = OratorioDynamicToolCatalog.CreateBoardTaskName,
            arguments = new { title = "Ship standard MCP", repository = "example-owner/oratorio" }
        }, sessionId);
        var createResult = createDoc.RootElement.GetProperty("result");
        Assert.False(createResult.TryGetProperty("isError", out var isError) && isError.GetBoolean(), createResult.GetRawText());

        using var listDoc = await PostMcpJsonAsync(client, "tools/call", new
        {
            name = OratorioDynamicToolCatalog.ListBoardItemsName,
            arguments = new { }
        }, sessionId);
        var items = listDoc.RootElement.GetProperty("result")
            .GetProperty("structuredContent")
            .GetProperty("items")
            .EnumerateArray()
            .ToArray();
        Assert.Contains(items, item => item.GetProperty("title").GetString() == "Ship standard MCP");
    }

    private static Task<HttpResponseMessage> PostMcpAsync(
        HttpClient client,
        string method,
        object parameters,
        string? sessionId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method,
                    @params = parameters
                }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (!string.IsNullOrWhiteSpace(sessionId))
            request.Headers.Add("Mcp-Session-Id", sessionId);
        return client.SendAsync(request);
    }

    private static async Task<JsonDocument> PostMcpJsonAsync(
        HttpClient client,
        string method,
        object parameters,
        string? sessionId = null)
    {
        using var response = await PostMcpAsync(client, method, parameters, sessionId);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            var dataLine = body
                .Split('\n')
                .Select(static line => line.Trim())
                .Last(static line => line.StartsWith("data:", StringComparison.Ordinal));
            return JsonDocument.Parse(dataLine["data:".Length..].Trim());
        }
        return JsonDocument.Parse(body);
    }
}
