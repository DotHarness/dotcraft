using System.Text.Json.Nodes;
using DotCraft.Acp;

namespace DotCraft.Tests.Acp;

/// <summary>
/// Unit tests for <see cref="AcpBridgeHandler"/> capability mapping (ACP client → wire <c>acpExtensions</c>).
/// </summary>
public sealed class AcpBridgeHandlerCapabilityTests
{
    [Fact]
    public void BuildAcpExtensionCapability_MapsDeclaredCapabilities()
    {
        var caps = new ClientCapabilities
        {
            Fs = new FsCapabilities
            {
                ReadTextFile = true,
                WriteTextFile = true
            },
            Terminal = new TerminalCapabilities { Create = true },
            Extensions = ["_unity", "foo"]
        };

        var ext = AcpBridgeHandler.BuildAcpExtensionCapability(caps);

        Assert.NotNull(ext);
        Assert.True(ext!.FsReadTextFile);
        Assert.True(ext.FsWriteTextFile);
        Assert.True(ext.TerminalCreate);
        Assert.Equal(["_unity", "foo"], ext.Extensions);
    }

    [Fact]
    public void TryBuildRuntimeTools_MapsClientRuntimeToolsToDynamicToolSpecs()
    {
        var caps = new ClientCapabilities
        {
            Extensions = ["_unity"],
            Meta = new ClientCapabilitiesMeta
            {
                DotCraft = new DotCraftClientCapabilities
                {
                    RuntimeTools =
                    [
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
                            Kind = AcpToolKind.Unity,
                            DeferLoading = true
                        }
                    ]
                }
            }
        };

        Assert.True(AcpBridgeHandler.TryBuildRuntimeTools(
            caps,
            out var dynamicTools,
            out var descriptors,
            out var message), message);

        var tool = Assert.Single(dynamicTools);
        Assert.Equal("unity", tool.Namespace);
        Assert.Equal("unity_scene_query", tool.Name);
        Assert.Equal("Query Unity scene hierarchy.", tool.Description);
        Assert.Equal("object", tool.InputSchema?["type"]?.GetValue<string>());
        Assert.True(tool.DeferLoading);

        var descriptor = Assert.Single(descriptors);
        Assert.Equal("unity", descriptor.Namespace);
        Assert.Equal("unity_scene_query", descriptor.Name);
        Assert.Equal("_unity/scene_query", descriptor.AcpMethod);
        Assert.Equal(AcpToolKind.Unity, descriptor.Kind);
    }

    [Fact]
    public void TryBuildRuntimeTools_RejectsUnadvertisedExtensionMethod()
    {
        var caps = new ClientCapabilities
        {
            Extensions = [],
            Meta = new ClientCapabilitiesMeta
            {
                DotCraft = new DotCraftClientCapabilities
                {
                    RuntimeTools =
                    [
                        new AcpRuntimeToolDescriptor
                        {
                            Name = "unity_scene_query",
                            Description = "Query Unity scene hierarchy.",
                            InputSchema = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject()
                            },
                            AcpMethod = "_unity/scene_query"
                        }
                    ]
                }
            }
        };

        Assert.False(AcpBridgeHandler.TryBuildRuntimeTools(
            caps,
            out var dynamicTools,
            out var descriptors,
            out var message));

        Assert.Empty(dynamicTools);
        Assert.Empty(descriptors);
        Assert.Contains("requires advertised extension '_unity'", message);
    }

    [Fact]
    public void ToolKindMapper_UsesRuntimeDeclaredKind()
    {
        Assert.Equal(AcpToolKind.Unity, AcpToolKindMapper.GetKind("unity_scene_query", "unity"));
        Assert.Equal(AcpToolKind.Other, AcpToolKindMapper.GetKind("unity_scene_query"));
    }
}
