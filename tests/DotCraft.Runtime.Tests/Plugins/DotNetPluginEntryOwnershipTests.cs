using DotCraft.Runtime;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.DotNetPluginTestBundle;

namespace DotCraft.Tests.Runtime.Plugins;

/// <summary>Guards the generation's exclusive ownership of the plugin entry instance.</summary>
public sealed class DotNetPluginEntryOwnershipTests : IDisposable
{
    private readonly PluginGenerationHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Teardown_DisposesEntryRegisteredAsAContributionExactlyOnce()
    {
        WritePlugin(
            _harness.PluginRoot("entry-contribution"),
            "entry-contribution",
            "EntryContribution.Plugin",
            """
            using System;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Contributions;
            using DotCraft.Plugins;
            namespace EntryContribution;
            public sealed class Plugin : IDotCraftPlugin, ISystemPromptSection, IDisposable
            {
                private string _log = "";
                public string Name => "entry-contribution";
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    Directory.CreateDirectory(context.DataRoot);
                    _log = Path.Combine(context.DataRoot, "dispose.log");
                    context.Contributions.Add<ISystemPromptSection>(this);
                    return ValueTask.CompletedTask;
                }
                public string? GetContent(SystemPromptSectionContext context) => "active";
                public void Dispose() => File.AppendAllText(_log, "disposed\n");
            }
            """);

        var attempt = await _harness.ActivateAsync("entry-contribution");
        var generation = Assert.IsType<PluginGeneration>(attempt.Generation);

        var remnant = await generation.BeginCleanup();

        Assert.Empty(remnant.CleanupErrors);
        Assert.Equal(
            ["disposed"],
            PluginLogFile.ReadLines(_harness.DataFile("entry-contribution", "dispose.log")));
    }

    [Fact]
    public async Task ActivationRollback_DisposesEntryRegisteredAsAContributionExactlyOnce()
    {
        WritePlugin(
            _harness.PluginRoot("failed-entry-contribution"),
            "failed-entry-contribution",
            "FailedEntryContribution.Plugin",
            """
            using System;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Contributions;
            using DotCraft.Plugins;
            namespace FailedEntryContribution;
            public sealed class Plugin : IDotCraftPlugin, ISystemPromptSection, IDisposable
            {
                private string _log = "";
                public string Name => "failed-entry-contribution";
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    Directory.CreateDirectory(context.DataRoot);
                    _log = Path.Combine(context.DataRoot, "dispose.log");
                    context.Contributions.Add<ISystemPromptSection>(this);
                    throw new InvalidOperationException("activation failed");
                }
                public string? GetContent(SystemPromptSectionContext context) => "never";
                public void Dispose() => File.AppendAllText(_log, "disposed\n");
            }
            """);

        var attempt = await _harness.ActivateAsync("failed-entry-contribution");

        Assert.Null(attempt.Generation);
        Assert.NotNull(attempt.Remnant);
        Assert.Empty(attempt.Remnant!.CleanupErrors);
        Assert.Equal(
            ["disposed"],
            PluginLogFile.ReadLines(_harness.DataFile("failed-entry-contribution", "dispose.log")));
    }
}
