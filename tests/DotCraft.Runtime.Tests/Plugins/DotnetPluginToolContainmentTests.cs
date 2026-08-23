using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Runtime;
using DotCraft.Tools;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.DotnetPluginTestBundle;
using static DotCraft.Tests.Runtime.Plugins.PluginRuntimeHarness;

namespace DotCraft.Tests.Runtime.Plugins;

/// <summary>Holds the containment property the Host wrapper exists for: a frozen snapshot must
/// reference nothing a plugin allocated, or the generation could never be reclaimed.</summary>
public sealed class DotnetPluginToolContainmentTests : IDisposable
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
        var host = typeof(DotnetPluginToolSource).Assembly;
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

    /// <summary>Walks a copied definition and fails on anything loaded into a collectible plugin context.</summary>
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
