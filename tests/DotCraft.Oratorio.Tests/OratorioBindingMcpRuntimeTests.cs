using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using DotCraft.Oratorio.Integrations;

namespace DotCraft.Oratorio.Tests;

public sealed class OratorioBindingMcpRuntimeTests
{
    [Fact]
    public async Task Initialize_requires_the_binding_bearer()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var runtime = new OratorioBindingMcpRuntime(
            services.GetRequiredService<IServiceScopeFactory>(),
            new OratorioDynamicToolCatalog(NullLogger<OratorioDynamicToolCatalog>.Instance));
        runtime.Issue("binding-1", 1);
        var context = Request("initialize", bearer: "wrong");

        await runtime.HandleAsync(context, "binding-1");

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task Initialize_returns_board_identity_instructions_and_an_isolated_session()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var runtime = new OratorioBindingMcpRuntime(
            services.GetRequiredService<IServiceScopeFactory>(),
            new OratorioDynamicToolCatalog(NullLogger<OratorioDynamicToolCatalog>.Instance));
        var bearer = runtime.Issue("binding-1", 7);
        var context = Request("initialize", bearer);

        await runtime.HandleAsync(context, "binding-1");

        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("oratorio.board", response.RootElement.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.Equal(OratorioBindingMcpCatalog.BoardNamespaceDescription,
            response.RootElement.GetProperty("result").GetProperty("instructions").GetString());
        Assert.False(string.IsNullOrWhiteSpace(context.Response.Headers["Mcp-Session-Id"]));
    }

    [Fact]
    public async Task New_authority_revision_revokes_the_previous_bearer()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var runtime = new OratorioBindingMcpRuntime(
            services.GetRequiredService<IServiceScopeFactory>(),
            new OratorioDynamicToolCatalog(NullLogger<OratorioDynamicToolCatalog>.Instance));
        var oldBearer = runtime.Issue("binding-1", 7);
        runtime.Issue("binding-1", 8);
        var context = Request("initialize", oldBearer);

        await runtime.HandleAsync(context, "binding-1");

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task Mcp_lists_descriptor_schemas_and_dispatches_all_four_board_tools()
    {
        await using var app = new TestOratorioApp();
        using var client = app.CreateClient();
        var runtime = app.Services.GetRequiredService<OratorioBindingMcpRuntime>();
        var bearer = runtime.Issue("binding-tools", 9);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

        using var initialize = await client.PostAsJsonAsync(
            "/dotcraft/bindings/binding-tools/mcp",
            Rpc("initialize", new { protocolVersion = "2025-06-18" }));
        initialize.EnsureSuccessStatusCode();
        var sessionId = Assert.Single(initialize.Headers.GetValues("Mcp-Session-Id"));
        client.DefaultRequestHeaders.Add("Mcp-Session-Id", sessionId);

        using var list = await PostRpcAsync(client, "tools/list", new { });
        var tools = list.RootElement.GetProperty("result").GetProperty("tools");
        Assert.Equal(4, tools.GetArrayLength());
        Assert.All(tools.EnumerateArray(), tool =>
            Assert.False(tool.GetProperty("inputSchema").GetProperty("additionalProperties").GetBoolean()));
        var listTool = tools.EnumerateArray()
            .Single(x => x.GetProperty("name").GetString() == OratorioDynamicToolCatalog.ListBoardItemsName);
        Assert.True(listTool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
        Assert.Equal(
            OratorioBindingMcpCatalog.BoardUiResourceUri,
            listTool.GetProperty("_meta").GetProperty("ui").GetProperty("resourceUri").GetString());

        using var resources = await PostRpcAsync(client, "resources/list", new { });
        Assert.Equal(3, resources.RootElement.GetProperty("result").GetProperty("resources").GetArrayLength());
        using var boardResource = await PostRpcAsync(
            client,
            "resources/read",
            new { uri = OratorioBindingMcpCatalog.BoardUiResourceUri });
        var boardContents = boardResource.RootElement.GetProperty("result").GetProperty("contents")[0];
        Assert.Equal("text/html;profile=mcp-app", boardContents.GetProperty("mimeType").GetString());
        Assert.Contains("<!doctype html>", boardContents.GetProperty("text").GetString());

        using var created = await PostRpcAsync(
            client,
            "tools/call",
            new
            {
                name = OratorioDynamicToolCatalog.CreateBoardTaskName,
                arguments = new { title = "MCP registry task", labels = new[] { "sdk" } }
            });
        var createResult = created.RootElement.GetProperty("result");
        Assert.False(createResult.GetProperty("isError").GetBoolean());
        var itemId = createResult.GetProperty("structuredContent")
            .GetProperty("detail").GetProperty("item").GetProperty("itemId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(itemId));
        Assert.Equal(9, createResult.GetProperty("structuredContent").GetProperty("authorityRevision").GetInt64());

        using var loaded = await PostRpcAsync(
            client,
            "tools/call",
            new { name = OratorioDynamicToolCatalog.GetBoardItemName, arguments = new { itemId } });
        Assert.False(loaded.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());

        using var listed = await PostRpcAsync(
            client,
            "tools/call",
            new { name = OratorioDynamicToolCatalog.ListBoardItemsName, arguments = new { q = "MCP registry task", limit = 10 } });
        Assert.False(listed.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());

        using var queued = await PostRpcAsync(
            client,
            "tools/call",
            new { name = OratorioDynamicToolCatalog.QueueReviewRoundName, arguments = new { itemId, note = "MCP end-to-end" } });
        Assert.False(queued.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());

        using var invalid = await PostRpcAsync(
            client,
            "tools/call",
            new { name = OratorioDynamicToolCatalog.GetBoardItemName, arguments = new { unexpected = true } });
        var invalidResult = invalid.RootElement.GetProperty("result");
        Assert.True(invalidResult.GetProperty("isError").GetBoolean());
        Assert.Equal(
            "InvalidArguments",
            invalidResult.GetProperty("structuredContent").GetProperty("error").GetProperty("code").GetString());
    }

    private static DefaultHttpContext Request(string method, string bearer)
    {
        var json = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method,
            @params = new { protocolVersion = "2025-06-18" }
        });
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Headers.Authorization = $"Bearer {bearer}";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static object Rpc(string method, object parameters) => new
    {
        jsonrpc = "2.0",
        id = Guid.NewGuid().ToString("N"),
        method,
        @params = parameters
    };

    private static async Task<JsonDocument> PostRpcAsync(HttpClient client, string method, object parameters)
    {
        using var response = await client.PostAsJsonAsync(
            "/dotcraft/bindings/binding-tools/mcp",
            Rpc(method, parameters));
        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    }
}
