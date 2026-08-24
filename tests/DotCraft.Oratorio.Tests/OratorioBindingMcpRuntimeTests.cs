using System.Security.Claims;
using System.Text.Json;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DotCraft.Oratorio.Integrations;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotCraft.Oratorio.Tests;

public sealed class OratorioBindingMcpRuntimeTests
{
    [Fact]
    public void Binding_authority_requires_the_current_bearer()
    {
        var runtime = new OratorioBindingMcpRuntime();
        var bearer = runtime.Issue("binding-1", 7);

        Assert.False(runtime.TryAuthorize("binding-1", "Bearer wrong", out _));
        Assert.False(runtime.TryAuthorize("binding-2", $"Bearer {bearer}", out _));
        Assert.True(runtime.TryAuthorize("binding-1", $"Bearer {bearer}", out var principal));
        Assert.True(principal.Identity?.IsAuthenticated);
        Assert.Equal("binding-1", principal.FindFirstValue(OratorioBindingMcpRuntime.BindingIdClaim));
        Assert.Equal("7", principal.FindFirstValue(OratorioBindingMcpRuntime.AuthorityRevisionClaim));
    }

    [Fact]
    public void Promotion_keeps_the_session_identity_and_updates_authority_revision()
    {
        var runtime = new OratorioBindingMcpRuntime();
        var bearer = runtime.Issue("binding-1", 7);
        Assert.True(runtime.TryAuthorize("binding-1", $"Bearer {bearer}", out var before));

        Assert.True(runtime.Promote("binding-1", bearer, 8));
        Assert.True(runtime.TryAuthorize("binding-1", $"Bearer {bearer}", out var after));

        Assert.Equal(before.FindFirstValue(ClaimTypes.NameIdentifier), after.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("8", after.FindFirstValue(OratorioBindingMcpRuntime.AuthorityRevisionClaim));
        Assert.True(runtime.HasAuthority("binding-1", 8));
        Assert.False(runtime.HasAuthority("binding-1", 7));
    }

    [Fact]
    public void New_authority_or_revoke_invalidates_the_previous_bearer()
    {
        var runtime = new OratorioBindingMcpRuntime();
        var oldBearer = runtime.Issue("binding-1", 7);
        var currentBearer = runtime.Issue("binding-1", 8);

        Assert.False(runtime.TryAuthorize("binding-1", $"Bearer {oldBearer}", out _));
        Assert.True(runtime.TryAuthorize("binding-1", $"Bearer {currentBearer}", out var principal));
        Assert.True(runtime.TryResolve(principal, out var grant));
        Assert.False(grant.Lifetime.IsCancellationRequested);

        runtime.Revoke("binding-1");
        Assert.False(runtime.TryAuthorize("binding-1", $"Bearer {currentBearer}", out _));
        Assert.True(grant.Lifetime.IsCancellationRequested);
    }

    [Fact]
    public async Task Endpoint_rejects_missing_wrong_and_cross_binding_session_authority()
    {
        await using var app = new TestOratorioApp();
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var runtime = app.Services.GetRequiredService<OratorioBindingMcpRuntime>();
        var bearer1 = runtime.Issue("binding-1", 1);
        var bearer2 = runtime.Issue("binding-2", 1);

        using (var missing = await client.PostAsJsonAsync(
                   "/dotcraft/bindings/binding-1/mcp",
                   Rpc("initialize", new { protocolVersion = "2025-06-18" })))
        {
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, missing.StatusCode);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");
        using (var wrong = await client.PostAsJsonAsync(
                   "/dotcraft/bindings/binding-1/mcp",
                   Rpc("initialize", new { protocolVersion = "2025-06-18" })))
        {
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer1);
        using var initialize = await client.PostAsJsonAsync(
            "/dotcraft/bindings/binding-1/mcp",
            Rpc("initialize", new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "test", version = "1" } }));
        initialize.EnsureSuccessStatusCode();
        var sessionId = Assert.Single(initialize.Headers.GetValues("Mcp-Session-Id"));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer2);
        client.DefaultRequestHeaders.Add("Mcp-Session-Id", sessionId);
        using var crossed = await client.PostAsJsonAsync(
            "/dotcraft/bindings/binding-2/mcp",
            Rpc("tools/list", new { }));
        Assert.False(crossed.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Sdk_client_negotiates_stable_protocol_and_dispatches_board_catalog()
    {
        await using var app = new TestOratorioApp();
        using var httpClient = app.CreateClient();
        var runtime = app.Services.GetRequiredService<OratorioBindingMcpRuntime>();
        var bearer = runtime.Issue("binding-tools", 1);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(httpClient.BaseAddress!, "/dotcraft/bindings/binding-tools/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                EnableStandaloneGetStream = false,
                AdditionalHeaders = new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {bearer}"
                }
            },
            httpClient,
            loggerFactory: null,
            ownsHttpClient: false);
        await using var client = await McpClient.CreateAsync(
            transport,
            new McpClientOptions { ProtocolVersion = "2025-06-18" });

        Assert.Equal("2025-06-18", client.NegotiatedProtocolVersion);
        Assert.Equal("oratorio.board", client.ServerInfo.Name);
        Assert.Equal(OratorioBindingMcpCatalog.BoardNamespaceDescription, client.ServerInstructions);
        Assert.True(runtime.Promote("binding-tools", bearer, 9));

        var tools = await client.ListToolsAsync(new ListToolsRequestParams(), CancellationToken.None);
        Assert.Equal(4, tools.Tools.Count);
        Assert.All(tools.Tools, tool =>
            Assert.False(tool.InputSchema.GetProperty("additionalProperties").GetBoolean()));
        var listTool = tools.Tools.Single(tool => tool.Name == OratorioDynamicToolCatalog.ListBoardItemsName);
        Assert.True(listTool.Annotations?.ReadOnlyHint);
        Assert.Equal(
            OratorioBindingMcpCatalog.BoardUiResourceUri,
            listTool.Meta?["ui"]?["resourceUri"]?.GetValue<string>());

        var resources = await client.ListResourcesAsync(new ListResourcesRequestParams(), CancellationToken.None);
        Assert.Equal(3, resources.Resources.Count);
        var boardResource = await client.ReadResourceAsync(
            new ReadResourceRequestParams { Uri = OratorioBindingMcpCatalog.BoardUiResourceUri },
            CancellationToken.None);
        var boardContent = Assert.IsType<TextResourceContents>(Assert.Single(boardResource.Contents));
        Assert.Equal("text/html;profile=mcp-app", boardContent.MimeType);
        Assert.Contains("<!doctype html>", boardContent.Text, StringComparison.OrdinalIgnoreCase);
        Assert.True(boardContent.Meta?["ui"]?["prefersBorder"]?.GetValue<bool>());

        var created = await CallAsync(client, OratorioDynamicToolCatalog.CreateBoardTaskName, new
        {
            title = "MCP registry task",
            labels = new[] { "sdk" }
        });
        Assert.False(created.IsError);
        var createContent = Assert.NotNull(created.StructuredContent);
        var itemId = createContent.GetProperty("detail").GetProperty("item").GetProperty("itemId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(itemId));
        Assert.Equal(9, createContent.GetProperty("authorityRevision").GetInt64());

        Assert.False((await CallAsync(client, OratorioDynamicToolCatalog.GetBoardItemName, new { itemId })).IsError);
        Assert.False((await CallAsync(client, OratorioDynamicToolCatalog.ListBoardItemsName,
            new { q = "MCP registry task", limit = 10 })).IsError);
        Assert.False((await CallAsync(client, OratorioDynamicToolCatalog.QueueReviewRoundName,
            new { itemId, note = "MCP end-to-end" })).IsError);

        var invalid = await CallAsync(client, OratorioDynamicToolCatalog.GetBoardItemName, new { unexpected = true });
        Assert.True(invalid.IsError);
        Assert.Equal(
            "InvalidArguments",
            Assert.NotNull(invalid.StructuredContent)
                .GetProperty("error").GetProperty("code").GetString());

        runtime.Revoke("binding-tools");
        var revoked = await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await CallAsync(client, OratorioDynamicToolCatalog.ListBoardItemsName, new { limit = 1 }));
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    private static ValueTask<CallToolResult> CallAsync(McpClient client, string name, object arguments) =>
        client.CallToolAsync(
            new CallToolRequestParams
            {
                Name = name,
                Arguments = JsonSerializer.SerializeToElement(arguments)
                    .EnumerateObject()
                    .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal)
            },
            CancellationToken.None);

    private static object Rpc(string method, object parameters) => new
    {
        jsonrpc = "2.0",
        id = Guid.NewGuid().ToString("N"),
        method,
        @params = parameters
    };
}
