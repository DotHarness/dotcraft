using System.Runtime.CompilerServices;
using System.Text;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Runtime;
using DotCraft.Sessions;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.DotNetPluginSampleEffects;
using static DotCraft.Tests.Runtime.Plugins.PluginRuntimeHarness;

namespace DotCraft.Tests.Runtime.Plugins;

/// <summary>Runs the shipped sample bundles through the real admission, preflight, trust, and activation
/// path, and asserts the observable consequence of every contribution point they cover.</summary>
/// <remarks>The bundle facts need <c>DOTCRAFT_SAMPLE_BUNDLES</c> pointing at built bundles, which
/// <c>sdk/dotnet/samples/DotNetPluginSample/verify.ps1</c> sets; without it they are skipped. The catalog
/// census is an ordinary fact, so a new contribution point fails an ordinary <c>dotnet test</c>.</remarks>
public sealed class DotNetPluginSampleBundleTests : IDisposable
{
    private readonly PluginRuntimeHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    /// <summary>The drift detector: the sample's coverage table must disposition every contract the kernel exports.</summary>
    [Fact]
    public void TheCoverageTableDispositionsEveryContributionContractTheKernelExports()
    {
        var declared = DotNetPluginSampleCoverage.Points.Select(static point => point.Contract).ToArray();
        Assert.Equal(declared.Length, declared.Distinct().Count());

        var kernel = DotNetPluginSampleCoverage.KernelContracts();
        var undispositioned = Names(kernel.Except(declared));
        Assert.True(
            undispositioned.Length == 0,
            "DotCraft.Core exports contribution contracts the sample's coverage table says nothing about: "
            + $"{string.Join(", ", undispositioned)}. Add a row to DotNetPluginSampleCoverage.Points — an "
            + "asserted effect, or a stated reason there is none — and back it with an assertion.");

        var stale = Names(declared.Except(kernel));
        Assert.True(
            stale.Length == 0,
            $"The sample's coverage table names contracts DotCraft.Core no longer exports: {string.Join(", ", stale)}.");
    }

    [SampleBundlesFact]
    public void SampleBundlesPassTheNonExecutingMetadataPreflight()
    {
        foreach (var pluginId in new[] { ProviderId, ConsumerId })
        {
            InstallBundle(pluginId);

            var parsed = PluginManifestParser.Load(_harness.PluginRoot(pluginId));
            Assert.NotNull(parsed.Manifest);
            Assert.NotNull(parsed.Manifest!.Dotnet);
            Assert.Matches(@"^\d+\.\d+\.\d+$", parsed.Manifest.Dotnet!.MinHostVersion);

            var errors = parsed.Diagnostics
                .Where(static diagnostic => diagnostic.Severity == PluginDiagnosticSeverity.Error)
                .Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")
                .ToArray();
            Assert.True(errors.Length == 0, $"'{pluginId}' failed preflight: {string.Join(" | ", errors)}");
        }
    }

    [SampleBundlesFact]
    public async Task EveryCoveredContributionPointTakesEffectOnTheActivatedBundles()
    {
        await using var manager = await ActivateAsync();
        using var host = new DotNetPluginSampleHost(_harness);
        var ledger = new SampleCoverageLedger();

        AssertPromptEffects(host, ledger);
        AssertChecklistFollowsSettings(host);
        await AssertCompactionEffectsAsync(host, ledger);
        await AssertModelPipelineEffectsAsync(host, ledger);
        await AssertToolEffectsAsync(host, manager, ledger);
        await AssertSessionAndSurfaceEffectsAsync(host, ledger);
        AssertRegistrationOnlyPoints(host, ledger);

        ledger.AssertMatchesDeclaredCoverage();
    }

    [SampleBundlesFact]
    public async Task DisablingTheProviderRestoresTheBuiltInsAndBlocksTheConsumer()
    {
        await using var manager = await ActivateAsync();
        using var host = new DotNetPluginSampleHost(_harness);
        Assert.Contains("## Review checklist", host.BuildPrompt(ThreadId), StringComparison.Ordinal);

        await manager.SetEnabledAsync(ProviderId, enabled: false);

        AssertBuiltInsRestored(host);
        Assert.Empty(await manager.ToolSource.GetRegistrationsAsync(PlanningContext(2, ThreadId)));
        AssertState(Plugin(manager, ConsumerId), PluginDotnetRuntimeState.Blocked);
        Assert.Contains(
            Plugin(manager, ConsumerId).Blockers,
            blocker => blocker.Code == "PluginDependencyUnsatisfied");
    }

    [SampleBundlesFact]
    public async Task PromptAndToolShapesRemainStableAcrossAPluginRestart()
    {
        await using var manager = await ActivateAsync();
        using var host = new DotNetPluginSampleHost(_harness);

        var baselinePrompt = host.BuildPrompt(ThreadId);
        var baselineTools = await ProviderVisibleToolShapesAsync(host, manager, revision: 1);

        Assert.Equal(baselinePrompt, host.BuildPrompt(ThreadId));
        Assert.Equal(baselineTools, await ProviderVisibleToolShapesAsync(host, manager, revision: 2));

        await manager.SetEnabledAsync(ProviderId, enabled: false);

        Assert.NotEqual(baselinePrompt, host.BuildPrompt(ThreadId));
        Assert.Empty(await ProviderVisibleToolShapesAsync(host, manager, revision: 3));

        await manager.SetEnabledAsync(ProviderId, enabled: true);

        AssertState(Plugin(manager, ProviderId), PluginDotnetRuntimeState.Active);
        AssertState(Plugin(manager, ConsumerId), PluginDotnetRuntimeState.Active);
        Assert.Equal(baselinePrompt, host.BuildPrompt(ThreadId));
        Assert.Equal(baselineTools, await ProviderVisibleToolShapesAsync(host, manager, revision: 4));
    }

    [SampleBundlesFact]
    public async Task OfficialGenericHost_RunsARealTurnAndObservesPluginTeardown()
    {
        InstallBundle(ProviderId);
        InstallBundle(ConsumerId);
        var config = CreateHostConfig();
        config.Plugins.Settings[ProviderId] = SettingsBag(checklistLimit: 2);
        GrantInstalledBundle(config, ProviderId);
        GrantInstalledBundle(config, ConsumerId);

        var provider = new CapturingModelProvider();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IModelProvider>(provider);
        builder.Services.AddDotCraftRuntime(new DotCraftRuntimeOptions
        {
            Config = config,
            WorkspacePath = _harness.Workspace,
            DataPath = ".craft",
            UserDataPath = Path.Combine(_harness.Root, "user-data")
        });
        builder.Services.AddSingleton<IConfigSchemaProvider>(new EmptyConfigSchemaProvider());

        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            var runtime = host.Services.GetRequiredService<IPluginDotnetRuntimeCoordinator>();
            AssertState(runtime.Snapshot.Plugins.Single(plugin => plugin.PluginId == ProviderId), PluginDotnetRuntimeState.Active);
            AssertState(runtime.Snapshot.Plugins.Single(plugin => plugin.PluginId == ConsumerId), PluginDotnetRuntimeState.Active);

            var sessions = host.Services.GetRequiredService<ISessionService>();
            var thread = await sessions.CreateThreadAsync(new SessionIdentity
            {
                ChannelName = "sample-smoke",
                UserId = "isolated-test",
                WorkspacePath = _harness.Workspace
            });

            Assert.Equal("official-host-ok", await RunTurnAsync(sessions, thread.Id));
            Assert.Contains("## Review checklist", provider.Client.LastInstructions, StringComparison.Ordinal);
            Assert.Contains("review__summary", provider.Client.LastToolNames);
            Assert.Contains("review__normalize", provider.Client.LastToolNames);
            Assert.DoesNotContain("review__publish", provider.Client.LastToolNames);

            var journal = Path.Combine(_harness.Root, "user-data", "plugins", ProviderId, "activity.log");
            await WaitForJournalAsync(
                journal,
                "plugin activated",
                "thread started",
                "turn started",
                "streaming call on Agent pipeline",
                "turn ended status=Completed failed=False");

            await runtime.SetEnabledAsync(ProviderId, enabled: false);
            var providerState = runtime.Snapshot.Plugins.Single(plugin => plugin.PluginId == ProviderId).State;
            Assert.True(providerState is PluginDotnetRuntimeState.Stopped or PluginDotnetRuntimeState.Reclaiming);
            AssertState(runtime.Snapshot.Plugins.Single(plugin => plugin.PluginId == ConsumerId), PluginDotnetRuntimeState.Blocked);

            Assert.Equal("official-host-ok", await RunTurnAsync(sessions, thread.Id));
            Assert.DoesNotContain("## Review checklist", provider.Client.LastInstructions, StringComparison.Ordinal);
            Assert.DoesNotContain(provider.Client.LastToolNames, static name => name.StartsWith("review__", StringComparison.Ordinal));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private async Task<DotNetPluginRuntimeManager> ActivateAsync()
    {
        InstallBundle(ProviderId);
        InstallBundle(ConsumerId);
        _harness.Config.Plugins.Settings[ProviderId] = SettingsBag(checklistLimit: 2);

        var manager = _harness.CreateManager(activationTimeout: TimeSpan.FromSeconds(30));
        await manager.StartAsync(CancellationToken.None);

        AssertState(Plugin(manager, ProviderId), PluginDotnetRuntimeState.Active);
        AssertState(Plugin(manager, ConsumerId), PluginDotnetRuntimeState.Active);
        Assert.Equal(PluginDotnetTrustStatus.Trusted, Plugin(manager, ProviderId).TrustStatus);
        return manager;
    }

    private void GrantInstalledBundle(AppConfig config, string pluginId)
    {
        var trust = new PluginDotnetTrust(config);
        Assert.True(trust.Grant(pluginId, PluginDotnetFingerprint.Compute(_harness.PluginRoot(pluginId))));
    }

    private AppConfig CreateHostConfig() => new()
    {
        GlobalConfigPath = Path.Combine(_harness.Root, "global", "config.json"),
        ProviderId = "sample-smoke",
        ProviderPreferences = new Dictionary<string, ModelPreference>(StringComparer.OrdinalIgnoreCase)
        {
            ["sample-smoke"] = new ModelPreference { Model = "fake-model" }
        },
        Providers =
        {
            ["sample-smoke"] = new AppConfig.ModelProviderConfig
            {
                DisplayName = "Sample smoke provider",
                Protocol = ModelProviderProtocols.OpenAIChatCompletions,
                ApiKey = "not-used",
                EndPoint = "https://example.invalid/v1"
            }
        }
    };

    private static async Task<string> RunTurnAsync(ISessionService sessions, string threadId)
    {
        var response = new StringBuilder();
        await foreach (var sessionEvent in sessions.SubmitInputAsync(threadId, "Run the sample smoke turn."))
        {
            if (sessionEvent.DeltaPayload?.TextDelta is { } delta)
                response.Append(delta);
            if (sessionEvent.EventType == SessionEventType.TurnFailed)
            {
                throw new InvalidOperationException(
                    sessionEvent.TurnFailedPayload?.Error ?? "The sample smoke turn failed.");
            }
        }
        return response.ToString();
    }

    private static async Task<string[]> ProviderVisibleToolShapesAsync(
        DotNetPluginSampleHost host,
        DotNetPluginRuntimeManager manager,
        long revision)
    {
        var snapshot = await host.BuildSnapshotAsync(manager, PlanningContext(revision, ThreadId));
        return
        [
            .. AgentFactory.ProjectSnapshotTools(snapshot)
                .OfType<AIFunction>()
                .Select(tool =>
                {
                    Assert.True(snapshot.TryResolveProviderFlatName(tool.Name, out var canonicalName));
                    var namespaceDescription = canonicalName.Namespace is { } toolNamespace
                        ? snapshot.NamespaceDescriptions.GetValueOrDefault(toolNamespace)
                        : null;
                    return string.Join(
                        '\n',
                        tool.Name,
                        canonicalName.ToString(),
                        namespaceDescription ?? string.Empty,
                        tool.Description,
                        tool.JsonSchema.GetRawText(),
                        tool.ReturnJsonSchema?.GetRawText() ?? string.Empty);
                })
        ];
    }

    private static async Task WaitForJournalAsync(string path, params string[] expected)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            string text;
            try
            {
                text = File.Exists(path) ? await File.ReadAllTextAsync(path) : string.Empty;
            }
            catch (IOException)
            {
                text = string.Empty;
            }

            if (expected.All(value => text.Contains(value, StringComparison.Ordinal)))
                return;
            await Task.Delay(20);
        }

        Assert.Fail($"The sample journal did not contain: {string.Join(", ", expected)}.");
    }

    private void InstallBundle(string pluginId)
    {
        var source = Path.Combine(SampleBundlesFactAttribute.BundlesRoot, pluginId);
        Assert.True(Directory.Exists(source), $"Sample bundle '{source}' is missing. Build it first.");

        var destination = _harness.PluginRoot(pluginId);
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
    }

    private static string[] Names(IEnumerable<Type> types) =>
        [.. types.Select(static type => type.Name).Order(StringComparer.Ordinal)];

    private sealed class EmptyConfigSchemaProvider : IConfigSchemaProvider
    {
        public IReadOnlyList<ConfigSchemaSection> GetConfigSchema() => [];
    }

    private sealed class CapturingModelProvider : IModelProvider
    {
        public CapturingChatClient Client { get; } = new();

        public IReadOnlyCollection<string> Protocols { get; } = [ModelProviderProtocols.OpenAIChatCompletions];

        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => Client;
    }

    private sealed class CapturingChatClient : IChatClient
    {
        public string LastInstructions { get; private set; } = string.Empty;

        public IReadOnlyList<string> LastToolNames { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Capture(options);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "official-host-ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Capture(options);
            yield return new ChatResponseUpdate(ChatRole.Assistant, "official-host-ok");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }

        private void Capture(ChatOptions? options)
        {
            LastInstructions = options?.Instructions ?? string.Empty;
            LastToolNames = options?.Tools?.Select(static tool => tool.Name).ToArray() ?? [];
        }
    }
}
