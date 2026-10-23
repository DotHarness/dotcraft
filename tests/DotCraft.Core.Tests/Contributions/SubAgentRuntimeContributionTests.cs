using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Contributions;
using Xunit;

namespace DotCraft.Tests.Contributions;

public sealed class SubAgentRuntimeContributionTests
{
    private const string AcmeRuntime = "acme-remote";
    private const string AcmeProfile = "acme-remote-review";
    private const string ThreadId = "thread-a";

    [Fact]
    public void WithoutContributions_TheCatalogIsTheHostsOwnSet()
    {
        Assert.Same(SubAgentProfileCatalog.BuiltIn, SubAgentProfileCatalog.Resolve(null));
        Assert.Same(SubAgentProfileCatalog.BuiltIn, SubAgentProfileCatalog.Resolve(new ContributionRegistry()));
        Assert.Equal(
            SubAgentProfileRegistry.KnownRuntimeTypes,
            SubAgentProfileCatalog.BuiltIn.KnownRuntimeTypes);
        Assert.Equal(
            SubAgentProfilePromptSectionBuilder.Build(configuredProfiles: null, binaryAvailabilityProbe: _ => true),
            SubAgentProfilePromptSectionBuilder.Build(
                configuredProfiles: null,
                catalog: SubAgentProfileCatalog.Resolve(new ContributionRegistry()),
                binaryAvailabilityProbe: _ => true));
    }

    [Fact]
    public void AContributedRuntime_IsKnownEverywhereOneProfileSetIsReadUntilRevoked()
    {
        var registry = new ContributionRegistry();
        var handle = registry.Add<ISubAgentRuntimeSource>(new StubContribution(AcmeRuntime, [CreateProfile(AcmeProfile)]));
        var catalog = SubAgentProfileCatalog.Resolve(registry);

        Assert.Empty(SubAgentProfileRegistry.ValidateProfiles(
            [CreateProfile(AcmeProfile)],
            catalog.KnownRuntimeTypes));
        Assert.Contains(AcmeRuntime, catalog.KnownRuntimeTypes);
        Assert.True(catalog.CreateRegistry(null).TryGet(AcmeProfile, out _));

        var section = SubAgentProfilePromptSectionBuilder.Build(
            configuredProfiles: null,
            catalog: catalog,
            binaryAvailabilityProbe: _ => true);
        Assert.Contains($"`{AcmeProfile}`", section, StringComparison.Ordinal);

        handle.Dispose();

        Assert.Same(SubAgentProfileCatalog.BuiltIn, SubAgentProfileCatalog.Resolve(registry));
    }

    [Fact]
    public void AContributedRuntime_IsRegisteredAndExecutesTheTaskItsProfileNames()
    {
        var runtime = new StubRuntime(AcmeRuntime);
        var registry = new ContributionRegistry();
        registry.Add<ISubAgentRuntimeSource>(new StubContribution(runtime, [CreateProfile(AcmeProfile)]));
        var coordinator = new SubAgentCoordinator(
            Path.GetTempPath(),
            [new CliOneshotRuntime()],
            catalog: SubAgentProfileCatalog.Resolve(registry));

        var diagnostic = Assert.Single(
            coordinator.GetProfileDiagnostics(),
            entry => entry.Name == AcmeProfile);
        Assert.True(diagnostic.RuntimeRegistered);
        Assert.False(diagnostic.HiddenFromPrompt);
        Assert.Empty(diagnostic.Warnings);
        Assert.Same(runtime, coordinator.PrepareRun(new SubAgentTaskRequest { Task = "review" }, AcmeProfile).Runtime);
    }

    [Fact]
    public void ThrowingAndShadowingContributions_AreSkippedWithoutStoppingTheOnesBehindThem()
    {
        var registry = new ContributionRegistry();
        var impostor = new StubRuntime(CliOneshotRuntime.RuntimeTypeName);
        registry.Add<ISubAgentRuntimeSource>(new ThrowingContribution(), new ContributionOptions(Order: 10));
        registry.Add<ISubAgentRuntimeSource>(new StubContribution(impostor, []), new ContributionOptions(Order: 20));
        registry.Add<ISubAgentRuntimeSource>(
            new StubContribution(AcmeRuntime, [CreateProfile(AcmeProfile)]),
            new ContributionOptions(Order: 30));
        var catalog = SubAgentProfileCatalog.Resolve(registry);

        Assert.Equal(AcmeRuntime, Assert.Single(catalog.ContributedRuntimes).RuntimeType);
        Assert.Equal(SubAgentProfileRegistry.KnownRuntimeTypes.Count + 1, catalog.KnownRuntimeTypes.Count);
    }

    [Fact]
    public void AThreadScopedRuntime_IsInvisibleToOtherThreads()
    {
        var registry = new ContributionRegistry();
        registry.Add<ISubAgentRuntimeSource>(
            new StubContribution(AcmeRuntime, [CreateProfile(AcmeProfile)]),
            new ContributionOptions(ContributionScope.Thread, ThreadId));

        Assert.Contains(AcmeRuntime, SubAgentProfileCatalog.Resolve(registry, ThreadId).KnownRuntimeTypes);
        Assert.DoesNotContain(AcmeRuntime, SubAgentProfileCatalog.Resolve(registry, "thread-b").KnownRuntimeTypes);
        Assert.Same(SubAgentProfileCatalog.BuiltIn, SubAgentProfileCatalog.Resolve(registry));
    }

    [Fact]
    public void AWorkspaceProfile_OverridesAContributedProfileOfTheSameName()
    {
        var registry = new ContributionRegistry();
        registry.Add<ISubAgentRuntimeSource>(new StubContribution(AcmeRuntime, [CreateProfile(AcmeProfile)]));
        var profiles = SubAgentProfileCatalog.Resolve(registry).CreateRegistry(
            [new SubAgentProfile { Name = AcmeProfile, Runtime = AcmeRuntime, WorkingDirectoryMode = "specified" }]);

        Assert.True(profiles.TryGet(AcmeProfile, out var effective));
        Assert.Equal("specified", effective.WorkingDirectoryMode);
        Assert.True(profiles.IsBuiltInProfile(AcmeProfile));
    }

    private static SubAgentProfile CreateProfile(string name) =>
        new() { Name = name, Runtime = AcmeRuntime, WorkingDirectoryMode = "workspace" };

    private sealed class StubContribution : ISubAgentRuntimeSource
    {
        public StubContribution(string runtimeType, IReadOnlyList<SubAgentProfile> profiles)
            : this(new StubRuntime(runtimeType), profiles)
        {
        }

        public StubContribution(ISubAgentRuntime runtime, IReadOnlyList<SubAgentProfile> profiles)
        {
            Runtime = runtime;
            Profiles = profiles;
        }

        public ISubAgentRuntime Runtime { get; }

        public IReadOnlyList<SubAgentProfile> Profiles { get; }
    }

    private sealed class ThrowingContribution : ISubAgentRuntimeSource
    {
        public ISubAgentRuntime Runtime => throw new InvalidOperationException("contribution is broken");
    }

    private sealed class StubRuntime(string runtimeType) : ISubAgentRuntime
    {
        public string RuntimeType => runtimeType;

        public Task<SubAgentSessionHandle> CreateSessionAsync(
            SubAgentProfile profile,
            SubAgentLaunchContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SubAgentSessionHandle(RuntimeType, profile.Name));

        public Task<SubAgentRunResult> RunAsync(
            SubAgentSessionHandle session,
            SubAgentTaskRequest request,
            ISubAgentEventSink sink,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SubAgentRunResult { Text = request.Task });

        public Task CancelAsync(SubAgentSessionHandle session, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DisposeSessionAsync(SubAgentSessionHandle session, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
