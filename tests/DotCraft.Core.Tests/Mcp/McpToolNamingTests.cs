using System.Text.RegularExpressions;
using DotCraft.Mcp;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Core.Tests.Mcp;

public sealed partial class McpToolNamingTests
{
    [Fact]
    public void NormalizeBatch_SanitizesModelIdentityAndPreservesRawRoute()
    {
        var identity = Assert.Single(McpToolNaming.NormalizeBatch([
            new McpToolIdentityInput("plugin:code-host-apps", "code-host-apps", "get me.详情")
        ]));

        Assert.Equal("plugin:code-host-apps", identity.RuntimeName);
        Assert.Equal("code-host-apps", identity.DeclaredName);
        Assert.Equal("get me.详情", identity.RawToolName);
        Assert.Equal(new ToolName("mcp__code_host_apps", "get_me___"), identity.ToolName);
        Assert.Equal("mcp__code_host_apps__get_me___", identity.FlatName);
        Assert.Matches(ProviderSafeComponent(), identity.ToolName.Namespace!);
        Assert.Matches(ProviderSafeComponent(), identity.ToolName.Name);
    }

    [Fact]
    public void NormalizeBatch_DisambiguatesNamespaceAndToolSanitizationCollisionsWithSha1()
    {
        var identities = McpToolNaming.NormalizeBatch([
            new McpToolIdentityInput("plugin-a:basic-server", "basic-server", "tool-name"),
            new McpToolIdentityInput("plugin-b:basic_server", "basic_server", "tool_name")
        ]);

        Assert.Collection(
            identities,
            first => Assert.Equal(
                new ToolName("mcp__basic_server_3dec2669c058", "tool_name"),
                first.ToolName),
            second => Assert.Equal(
                new ToolName("mcp__basic_server_9bfdef50a329", "tool_name"),
                second.ToolName));
    }

    [Fact]
    public void NormalizeBatch_DisambiguatesToolCollisionsWithinOneServerWithSha1()
    {
        var identities = McpToolNaming.NormalizeBatch([
            new McpToolIdentityInput("server", null, "tool-name"),
            new McpToolIdentityInput("server", null, "tool_name")
        ]);

        Assert.Equal("tool_name_71b8387d1286", identities[0].ToolName.Name);
        Assert.Equal("tool_name_9e590e82658b", identities[1].ToolName.Name);
    }

    [Fact]
    public void NormalizeBatch_LongNamesStayWithinProviderLimitAndRemainDeterministic()
    {
        var input = new[]
        {
            new McpToolIdentityInput(new string('s', 80), null, new string('x', 80)),
            new McpToolIdentityInput("ordinary", null, "lookup")
        };

        var forward = McpToolNaming.NormalizeBatch(input);
        var reverse = McpToolNaming.NormalizeBatch(input.Reverse()).ToDictionary(static item => item.RuntimeName);

        Assert.All(forward, identity =>
        {
            Assert.True(identity.FlatName.Length <= McpToolNaming.MaxFlatNameLength);
            Assert.Matches(ProviderSafeComponent(), identity.ToolName.Namespace!);
            Assert.Matches(ProviderSafeComponent(), identity.ToolName.Name);
            Assert.Equal(identity.ToolName, reverse[identity.RuntimeName].ToolName);
        });
        Assert.Equal(49, forward[0].ToolName.Namespace!.Length);
        Assert.Equal(64, forward[0].FlatName.Length);
        Assert.Equal("mcp__sssssssssssssssssssssssssssssss_531eeeebff55", forward[0].ToolName.Namespace);
        Assert.Equal("_9a163175721d", forward[0].ToolName.Name);
    }

    [Fact]
    public void NormalizeBatch_UsesRuntimeNameWhenDeclaredNameIsAbsent()
    {
        var identity = Assert.Single(McpToolNaming.NormalizeBatch([
            new McpToolIdentityInput("binding:board:server", null, "read-card")
        ]));

        Assert.Equal(new ToolName("mcp__binding_board_server", "read_card"), identity.ToolName);
    }

    [Fact]
    public void NormalizeBatch_ColonRuntimeAndHyphenatedServerProduceProviderSafeIdentity()
    {
        var identity = Assert.Single(McpToolNaming.NormalizeBatch([
            new McpToolIdentityInput("plugin:catalog-service", "catalog-service", "get_status")
        ]));

        Assert.Equal(new ToolName("mcp__catalog_service", "get_status"), identity.ToolName);
        Assert.Equal("mcp__catalog_service__get_status", identity.FlatName);
        Assert.Matches(ProviderSafeComponent(), identity.ToolName.Namespace!);
        Assert.Matches(ProviderSafeComponent(), identity.ToolName.Name);
    }

    [Fact]
    public void NormalizeBatch_RuntimeWithRepeatedColonSegmentsUsesDeclaredCanonicalNamespace()
    {
        var identity = Assert.Single(McpToolNaming.NormalizeBatch([
            new McpToolIdentityInput(
                "catalog-service:catalog-service",
                "catalog-service",
                "search_records")
        ]));

        Assert.Equal(new ToolName("mcp__catalog_service", "search_records"), identity.ToolName);
        Assert.Equal("catalog-service:catalog-service", identity.RuntimeName);
    }

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex ProviderSafeComponent();
}
