using System.Text.Json.Nodes;
using System.Text.Json;
using DotCraft.Mcp;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;

namespace DotCraft.Tests.Mcp;

public sealed class McpAppsMetadataTests
{
    [Fact]
    public void ToolMetadata_MissingUi_DefaultsToModelAndAppVisibility()
    {
        Assert.True(McpAppMetadataParser.TryParseToolMetadata(null, out var metadata));

        Assert.Null(metadata.ResourceUri);
        Assert.Equal(McpAppVisibility.Model | McpAppVisibility.App, metadata.Visibility);
    }

    [Fact]
    public void ToolMetadata_AppOnly_MapsToHiddenAppCallableRegistration()
    {
        var meta = JsonNode.Parse("""
            {
              "ui": {
                "resourceUri": "ui://review/result",
                "visibility": ["app"]
              }
            }
            """)!.AsObject();

        Assert.True(McpAppMetadataParser.TryParseToolMetadata(meta, out var metadata));
        var publication = McpToolSource.ResolvePublication(metadata, deferred: false);

        Assert.Equal("ui://review/result", metadata.ResourceUri!.AbsoluteUri.TrimEnd('/'));
        Assert.Equal(ToolExposure.Hidden, publication.Exposure);
        Assert.Equal(ToolInvocationAudience.Host | ToolInvocationAudience.App, publication.Audiences);
    }

    [Fact]
    public void ToolMetadata_AcceptsFlatResourceUriWhenNestedMetadataIsAbsent()
    {
        var meta = new JsonObject
        {
            ["ui/resourceUri"] = "ui://catalog/status"
        };

        Assert.True(McpAppMetadataParser.TryParseToolMetadata(meta, out var metadata));
        Assert.Equal("ui://catalog/status", metadata.ResourceUri?.AbsoluteUri.TrimEnd('/'));
        Assert.Equal(McpAppVisibility.Model | McpAppVisibility.App, metadata.Visibility);
    }

    [Fact]
    public void ToolMetadata_InvalidNestedResourceDoesNotFallBackToFlatAlias()
    {
        var meta = new JsonObject
        {
            ["ui"] = new JsonObject { ["resourceUri"] = "https://invalid.example/view" },
            ["ui/resourceUri"] = "ui://catalog/status"
        };

        Assert.False(McpAppMetadataParser.TryParseToolMetadata(meta, out var metadata));
        Assert.Null(metadata.ResourceUri);
        Assert.Equal(McpAppVisibility.None, metadata.Visibility);
    }

    [Fact]
    public void ToolMetadata_UnknownVisibility_FailsClosed()
    {
        var meta = JsonNode.Parse("""
            { "ui": { "visibility": ["model", "administrator"] } }
            """)!.AsObject();

        Assert.False(McpAppMetadataParser.TryParseToolMetadata(meta, out var metadata));
        var publication = McpToolSource.ResolvePublication(metadata, deferred: false);

        Assert.Equal(McpAppVisibility.None, metadata.Visibility);
        Assert.Equal(ToolExposure.Hidden, publication.Exposure);
        Assert.Equal(ToolInvocationAudience.Host, publication.Audiences);
    }

    [Fact]
    public void ToolMetadata_EmptyVisibility_PreservesHostAuthorityOnly()
    {
        var meta = JsonNode.Parse("""
            { "ui": { "visibility": [] } }
            """)!.AsObject();

        Assert.True(McpAppMetadataParser.TryParseToolMetadata(meta, out var metadata));
        var publication = McpToolSource.ResolvePublication(metadata, deferred: true);

        Assert.Equal(ToolExposure.Hidden, publication.Exposure);
        Assert.Equal(ToolInvocationAudience.Host, publication.Audiences);
    }

    [Fact]
    public void ResourceMetadata_ParsesSecurityContract()
    {
        var meta = JsonNode.Parse("""
            {
              "ui": {
                "csp": {
                  "connectDomains": ["https://api.example.test"],
                  "resourceDomains": ["https://static.example.test"]
                },
                "permissions": {
                  "camera": {},
                  "clipboardWrite": {}
                },
                "domain": "app-sandbox.example.test",
                "prefersBorder": false
              }
            }
            """)!.AsObject();

        Assert.True(McpAppMetadataParser.TryParseResourceMetadata(meta, out var metadata));

        Assert.Equal(["https://api.example.test"], metadata.Csp!.ConnectDomains);
        Assert.Equal(["https://static.example.test"], metadata.Csp.ResourceDomains);
        Assert.True(metadata.Permissions!.Camera);
        Assert.True(metadata.Permissions.ClipboardWrite);
        Assert.Equal("app-sandbox.example.test", metadata.Domain);
        Assert.False(metadata.PrefersBorder);
    }

    [Fact]
    public void ResourceMetadata_RejectsNonHttpsCspOrigin()
    {
        var meta = JsonNode.Parse("""
            { "ui": { "csp": { "connectDomains": ["http://example.test"] } } }
            """)!.AsObject();

        Assert.False(McpAppMetadataParser.TryParseResourceMetadata(meta, out _));
    }

    [Fact]
    public void ResourceContent_RequiresMatchingUiHtmlResource()
    {
        var expected = new Uri("ui://review/result");
        var result = new ReadResourceResult
        {
            Contents =
            [
                new TextResourceContents
                {
                    Uri = expected.AbsoluteUri,
                    MimeType = McpAppMetadataParser.HtmlMimeType,
                    Text = "<main>Safe</main>",
                    Meta = new JsonObject { ["ui"] = new JsonObject { ["prefersBorder"] = true } }
                }
            ]
        };

        Assert.True(McpAppMetadataParser.TryParseResourceContent(
            result,
            expected,
            out var content,
            out var error), error);

        Assert.Equal("<main>Safe</main>", content!.Text);
        Assert.True(content.Metadata.PrefersBorder);
    }

    [Fact]
    public void ClientOptions_AlwaysAdvertiseStableAppsExtension()
    {
        var options = McpClientManager.CreateClientOptions(null);

#pragma warning disable MCPEXP001
        Assert.NotNull(options.Capabilities);
        var capabilities = options.Capabilities!;
        var extension = Assert.IsType<JsonElement>(
            capabilities.Extensions![McpAppMetadataParser.ExtensionIdentifier]);
#pragma warning restore MCPEXP001
        Assert.Equal(
            [McpAppMetadataParser.HtmlMimeType],
            extension.GetProperty("mimeTypes").EnumerateArray().Select(static value => value.GetString()));
    }

    [Fact]
    public async Task ReadResourceAsync_RejectsStaleGenerationBeforeProtocolAccess()
    {
        await using var manager = new McpClientManager((_, _) => Task.FromResult(
            new McpConnectionResult(new FakeClient(), [new FakeFunction("lookup")])));
        await manager.ConnectAsync(
        [
            new McpServerConfig
            {
                Name = "review",
                Enabled = true,
                Transport = "streamableHttp",
                Url = "https://example.test/mcp"
            }
        ]);
        await manager.WaitForStartupCompletionAsync();
        var generation = Assert.NotNull(await manager.GetGenerationAsync("review"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.ReadResourceAsync("review", "ui://review/result", generation - 1));

        Assert.Contains("no longer active", error.Message, StringComparison.Ordinal);
    }

    private sealed class FakeClient : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeFunction(string name) : AIFunction
    {
        public override string Name { get; } = name;

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<object?>(null);
    }
}
