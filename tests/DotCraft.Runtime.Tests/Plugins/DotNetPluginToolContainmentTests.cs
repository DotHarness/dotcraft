using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Runtime;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.DotNetPluginTestBundle;
using static DotCraft.Tests.Runtime.Plugins.PluginRuntimeHarness;

namespace DotCraft.Tests.Runtime.Plugins;

/// <summary>Holds the containment property the Host wrapper exists for: a frozen snapshot must
/// reference nothing a plugin allocated, or the generation could never be reclaimed.</summary>
public sealed class DotNetPluginToolContainmentTests : IDisposable
{
    private readonly PluginRuntimeHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task EffectiveSnapshot_HoldsNothingFromThePluginsLoadContext()
    {
        WritePluginBundle(
            _harness.PluginRoot("contained.tools"),
            "contained.tools",
            "ContainedTools.Plugin",
            """
            using System.Text.Json.Nodes;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            using DotCraft.Tests.Bundle;
            using DotCraft.Tools;
            namespace ContainedTools;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    context.Contributions.Add<IToolSource>(new Contained());
                    return ValueTask.CompletedTask;
                }
                private sealed class Contained() : TestTool("contained", "sample", "contained", "Stays behind the proxy.")
                {
                    public override ValueTask<ToolExecutionResult> InvokeAsync(
                        ToolInvocationContext context,
                        JsonObject arguments,
                        CancellationToken cancellationToken = default) =>
                        ValueTask.FromResult(ToolExecutionResult.Succeeded("contained"));
                }
            }
            """);
        await using var manager = _harness.CreateManager();
        await manager.StartAsync(CancellationToken.None);

        var snapshot = await BuildSnapshotAsync(manager.ToolSource, revision: 1);

        var registration = Assert.Single(snapshot.Registrations).Value;
        var host = typeof(DotNetPluginToolSource).Assembly;
        Assert.Equal(host, registration.Binding.Runtime.GetType().Assembly);
        Assert.Equal(host, registration.Binding.Lease.GetType().Assembly);
        AssertHostOwnedGraph(registration.Definition);
        // The proxy's own state is identifiers plus Host services. The live registry it re-resolves
        // through is deliberately shared: that is where revocation happens.
        foreach (var field in registration.Binding.Runtime.GetType()
                     .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            AssertHostOwned(field.GetValue(registration.Binding.Runtime));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ToolResult_PreservesRichContentWithoutRetainingPluginObjects(bool failed)
    {
        const string imageBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jWZkAAAAASUVORK5CYII=";
        WritePluginBundle(
            _harness.PluginRoot("image.tools"),
            "image.tools",
            "ImageTools.Plugin",
            $$"""
            using System;
            using System.Buffers;
            using System.Text.Json.Nodes;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            using DotCraft.Tests.Bundle;
            using DotCraft.Tools;
            using Microsoft.Extensions.AI;
            namespace ImageTools;
            public sealed class Plugin : IDotCraftPlugin, IDisposable
            {
                private readonly ImageTool _tool = new();
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    context.Contributions.Add<IToolSource>(_tool);
                    return ValueTask.CompletedTask;
                }
                public void Dispose() => _tool.Clear();
                private sealed class ImageTool() : TestTool("image", null, "image", "Returns image content.")
                {
                    private readonly ImageMemory _memory = new();
                    private readonly TextContent _text = new("image description");
                    public override ValueTask<ToolExecutionResult> InvokeAsync(
                        ToolInvocationContext context, JsonObject arguments, CancellationToken cancellationToken = default)
                    {
                        AIContent[] items =
                        [
                            _text,
                            new DataContent(_memory.Memory, "image/png") { Name = "capture.png" },
                            new UriContent("https://example.com/capture.png", "image/png")
                        ];
                        foreach (var item in items)
                        {
                            item.RawRepresentation = this;
                            item.AdditionalProperties = new() { ["plugin"] = this };
                        }
                        return ValueTask.FromResult(arguments["failed"]!.GetValue<bool>()
                            ? ToolExecutionResult.Failed(new ToolError("capture_failed", "Capture failed."), "summary", items)
                            : ToolExecutionResult.Succeeded("summary", contentItems: items));
                    }
                    public void Clear()
                    {
                        _memory.GetSpan().Clear();
                        _text.Text = "cleared";
                    }
                }
                private sealed class ImageMemory : MemoryManager<byte>
                {
                    private readonly byte[] _bytes = Convert.FromBase64String("{{imageBase64}}");
                    public override Span<byte> GetSpan() => _bytes;
                    public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
                    public override void Unpin() { }
                    protected override void Dispose(bool disposing) { }
                }
            }
            """);
        await using var manager = _harness.CreateManager();
        await manager.StartAsync(CancellationToken.None);
        var snapshot = await BuildSnapshotAsync(manager.ToolSource, revision: 1);
        var registration = Assert.Single(snapshot.Registrations).Value;

        var result = await new ToolDispatcher().DispatchAsync(
            snapshot, registration.Definition.Name, new JsonObject { ["failed"] = failed }, Request("image-call"));

        Assert.Equal(!failed, result.Success);
        Assert.Equal(failed ? "capture_failed" : null, result.Error?.Code);
        Assert.Equal("summary", result.Content);
        AssertHostOwnedGraph(result);
        await manager.SetEnabledAsync("image.tools", enabled: false);
        await WaitForStateAsync(manager, "image.tools", DotCraft.Plugins.PluginDotnetRuntimeState.Stopped);
        Assert.Collection(
            result.ContentItems!,
            content => Assert.Equal("image description", Assert.IsType<TextContent>(content).Text),
            content =>
            {
                var image = Assert.IsType<DataContent>(content);
                Assert.Equal("image/png", image.MediaType);
                Assert.Equal("capture.png", image.Name);
                Assert.Equal(Convert.FromBase64String(imageBase64), image.Data.ToArray());
            },
            content => Assert.Equal(
                "https://example.com/capture.png", Assert.IsType<UriContent>(content).Uri.AbsoluteUri));
        Assert.All(result.ContentItems!, content =>
        {
            Assert.Null(content.RawRepresentation);
            Assert.Null(content.AdditionalProperties);
        });
    }

    private static void AssertHostOwnedGraph(object root)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<object>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current))
                continue;

            AssertHostOwned(current);
            var type = current.GetType();
            if (type.IsPrimitive || current is string or JsonElement or JsonNode or JsonDocument)
                continue;
            if (current is IEnumerable items)
            {
                foreach (var item in items)
                    if (item is not null)
                        pending.Push(item);
            }

            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.GetValue(current) is { } value && !value.GetType().IsPrimitive)
                    pending.Push(value);
            }
        }
    }

    private static void AssertHostOwned(object? value)
    {
        if (value == null)
            return;
        var type = value.GetType();
        Assert.False(
            AssemblyLoadContext.GetLoadContext(type.Assembly)?.IsCollectible == true,
            $"'{type.FullName}' reached the effective snapshot from a plugin load context.");
    }
}
