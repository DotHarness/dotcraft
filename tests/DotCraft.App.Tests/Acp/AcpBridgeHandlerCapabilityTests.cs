using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Acp;
using DotCraft.AppServer;
using DotHarness.Acp.V1;
using Xunit;
using ClientCapabilities = DotHarness.Acp.V1.ClientCapabilities;

namespace DotCraft.Tests.Acp;

public sealed class AcpBridgeHandlerCapabilityTests
{
    [Fact]
    public void BuildAcpClientCapability_MapsDeclaredCapabilities()
    {
        var caps = new ClientCapabilities
        {
            Fs = new FileSystemCapabilities
            {
                ReadTextFile = true,
                WriteTextFile = true
            },
            Terminal = true
        };

        var descriptors = new[]
        {
            new AcpRuntimeToolDescriptor { AcpMethod = "_unity/scene_query" }
        };

        var ext = AcpRuntimeTools.BuildAcpClientCapability(caps, descriptors);

        Assert.NotNull(ext);
        Assert.True(ext!.FsReadTextFile);
        Assert.True(ext.FsWriteTextFile);
        Assert.True(ext.TerminalCreate);
        Assert.Equal(["_unity"], ext.Extensions);
    }

    [Fact]
    public void TryBuildRuntimeTools_MapsClientRuntimeToolsToDynamicToolSpecs()
    {
        var caps = CreateRuntimeToolsCapabilities(
            version: 1,
            new AcpRuntimeToolDescriptor
            {
                Namespace = "unity",
                Name = "unity_scene_query",
                Description = "Query Unity scene hierarchy.",
                InputSchema = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["query"] = new JsonObject { ["type"] = "string" }
                    }
                },
                AcpMethod = "_unity/scene_query",
                Kind = "read",
                DeferLoading = true
            });

        Assert.True(AcpRuntimeTools.TryBuildRuntimeTools(
            caps,
            out var dynamicTools,
            out var descriptors,
            out var message), message);

        var toolNamespace = Assert.IsType<RuntimeDynamicToolNamespaceSpec>(Assert.Single(dynamicTools));
        Assert.Equal("unity", toolNamespace.Name);
        var tool = Assert.IsType<RuntimeDynamicToolFunctionSpec>(Assert.Single(toolNamespace.Tools));
        Assert.Equal("unity_scene_query", tool.Name);
        Assert.Equal("Query Unity scene hierarchy.", tool.Description);
        Assert.Equal("object", tool.InputSchema?["type"]?.GetValue<string>());
        Assert.True(tool.DeferLoading);

        var descriptor = Assert.Single(descriptors);
        Assert.Equal("unity", descriptor.Namespace);
        Assert.Equal("unity_scene_query", descriptor.Name);
        Assert.Equal("_unity/scene_query", descriptor.AcpMethod);
        Assert.Equal("read", descriptor.Kind);
    }

    [Fact]
    public void TryBuildRuntimeTools_RejectsCustomMethodWithoutUnderscore()
    {
        var caps = CreateRuntimeToolsCapabilities(
            version: 1,
            new AcpRuntimeToolDescriptor
            {
                Name = "unity_scene_query",
                Description = "Query Unity scene hierarchy.",
                InputSchema = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject()
                },
                AcpMethod = "unity/scene_query"
            });

        Assert.False(AcpRuntimeTools.TryBuildRuntimeTools(
            caps,
            out var dynamicTools,
            out var descriptors,
            out var message));

        Assert.Empty(dynamicTools);
        Assert.Empty(descriptors);
        Assert.Contains("must use an ACP custom method beginning with '_'", message);
    }

    [Theory]
    [InlineData(2)]
    public void TryBuildRuntimeTools_RejectsUnsupportedPrivateContractVersion(int version)
    {
        var caps = CreateRuntimeToolsCapabilities(version);

        Assert.False(AcpRuntimeTools.TryBuildRuntimeTools(
            caps,
            out var dynamicTools,
            out var descriptors,
            out var message));
        Assert.Empty(dynamicTools);
        Assert.Empty(descriptors);
        Assert.Contains("runtimeTools.version must be 1", message);
    }

    [Fact]
    public void ToolKindMapper_MapsStandardKindsAndFallsBackToOther()
    {
        Assert.Equal(ToolKind.Search, AcpToolKindMapper.GetKind("unity_scene_query", "search"));
        Assert.Equal(ToolKind.Other, AcpToolKindMapper.GetKind("unity_scene_query", "unity"));
        Assert.Equal(ToolKind.Other, AcpToolKindMapper.GetKind("unity_scene_query"));
    }

    [Fact]
    public void ToolKindMapper_ExtractFilePaths_EmitsOnlyAbsolutePaths()
    {
        var absolutePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Program.cs"));

        Assert.Null(AcpToolKindMapper.ExtractFilePaths("ReadFile", JsonSerializer.SerializeToElement(new { path = "src/Program.cs" })));
        Assert.Null(AcpToolKindMapper.ExtractFilePaths("ReadFile", JsonSerializer.SerializeToElement(new { path = "~/notes.txt" })));

        var abs = AcpToolKindMapper.ExtractFilePaths("EditFile", JsonSerializer.SerializeToElement(new { path = absolutePath }));
        Assert.NotNull(abs);
        Assert.Equal([absolutePath], abs);
    }

    private static ClientCapabilities CreateRuntimeToolsCapabilities(
        int version,
        params AcpRuntimeToolDescriptor[] tools)
    {
        var dotcraftMeta = JsonSerializer.SerializeToElement(new
        {
            runtimeTools = new AcpRuntimeToolsCapability
            {
                Version = version,
                Tools = [.. tools]
            }
        });

        return new ClientCapabilities
        {
            Meta = new Dictionary<string, JsonElement>
            {
                ["dotcraft"] = dotcraftMeta
            }
        };
    }
}
