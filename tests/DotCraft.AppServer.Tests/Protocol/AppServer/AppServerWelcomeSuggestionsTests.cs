using DotCraft.Memory;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerWelcomeSuggestionsTests : IDisposable
{
    private readonly FakeWelcomeSuggestionService _welcomeSuggestionService = new();
    private readonly AppServerTestHarness _h;

    public AppServerWelcomeSuggestionsTests()
    {
        _h = new AppServerTestHarness(welcomeSuggestionService: _welcomeSuggestionService);
    }

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Initialize_AdvertisesWelcomeSuggestionsCapability()
    {
        var initDoc = await _h.InitializeAsync();

        var extensions = initDoc.RootElement
            .GetProperty("result")
            .GetProperty("capabilities")
            .GetProperty("extensions");

        Assert.True(extensions.GetProperty("welcomeSuggestions").GetBoolean());
    }

    [Fact]
    public async Task WelcomeSuggestions_RoutesToServiceAndReturnsTypedPayload()
    {
        await _h.InitializeAsync();

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.WelcomeSuggestions, new
        {
            identity = new
            {
                channelName = "dotcraft-desktop",
                userId = "local",
                workspacePath = _h.Identity.WorkspacePath,
                channelContext = $"workspace:{_h.Identity.WorkspacePath}"
            },
            maxItems = 4
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        var result = doc.RootElement.GetProperty("result");
        Assert.Equal("dynamic", result.GetProperty("source").GetString());
        Assert.Equal(4, result.GetProperty("items").GetArrayLength());
        Assert.NotNull(_welcomeSuggestionService.LastParams);
        Assert.Equal("dotcraft-desktop", _welcomeSuggestionService.LastParams!.Identity.ChannelName);
    }

    [Fact]
    public async Task WelcomeSuggestions_BeforeInitialize_ReturnsNotInitialized()
    {
        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.WelcomeSuggestions, new
        {
            identity = new
            {
                channelName = "dotcraft-desktop",
                workspacePath = _h.Identity.WorkspacePath
            }
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.NotInitializedCode);
    }

    [Fact]
    public async Task ClearWorkspaceCache_RemovesPersistedCache()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"welcome_suggestions_cache_{Guid.NewGuid():N}");
        try
        {
            var workspacePath = Path.Combine(tempRoot, "workspace");
            var craftPath = Path.Combine(workspacePath, ".craft");
            var cacheDir = Path.Combine(craftPath, "cache");
            Directory.CreateDirectory(cacheDir);
            var persistedPath = Path.Combine(cacheDir, "welcome-suggestions.json");
            await File.WriteAllTextAsync(persistedPath, "{}");

            var threadStore = new ThreadStore(Path.Combine(tempRoot, "threads"));
            var sessionService = new TestableSessionService(threadStore);
            await using var service = new WelcomeSuggestionService(
                sessionService,
                new SessionPersistenceService(threadStore),
                new MemoryStore(craftPath),
                workspacePath,
                new AppConfig(),
                craftPath);

            service.ClearWorkspaceCache(workspacePath);

            Assert.False(File.Exists(persistedPath));
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    private sealed class FakeWelcomeSuggestionService : IWelcomeSuggester
    {
        public WelcomeSuggestionRequest? LastParams { get; private set; }

        public void ScheduleRefresh(string workspacePath, string? triggerThreadId = null)
        {
        }

        public void ClearWorkspaceCache(string workspacePath)
        {
        }

        public Task<WelcomeSuggestionSnapshot> SuggestAsync(
            WelcomeSuggestionRequest parameters,
            CancellationToken cancellationToken = default)
        {
            LastParams = parameters;
            return Task.FromResult(new WelcomeSuggestionSnapshot
            {
                Source = "dynamic",
                Fingerprint = "test-fingerprint",
                GeneratedAt = DateTimeOffset.UtcNow,
                Items =
                [
                    new WelcomeSuggestion { Title = "One", Prompt = "Prompt one", Reason = "Reason one" },
                    new WelcomeSuggestion { Title = "Two", Prompt = "Prompt two", Reason = "Reason two" },
                    new WelcomeSuggestion { Title = "Three", Prompt = "Prompt three", Reason = "Reason three" },
                    new WelcomeSuggestion { Title = "Four", Prompt = "Prompt four", Reason = "Reason four" }
                ]
            });
        }
    }
}
