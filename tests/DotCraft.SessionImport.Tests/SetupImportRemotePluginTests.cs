using DotCraft.Plugins.Marketplaces;
using DotCraft.Protocol.AppServer;

namespace DotCraft.SessionImport.Tests;

public sealed class SetupImportRemotePluginTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemotePluginsFetchOnlyDuringImportAndNeverSwitchSourceAfterFailure(bool fail)
    {
        var workspace = _temp.CreateDirectory("repo");
        var data = _temp.CreateDirectory("repo", ".craft");
        var user = _temp.CreateDirectory("home", ".craft");
        var source = _temp.CreateDirectory("home", ".claude");
        Write(source, "settings.json", "{\"enabledPlugins\":{\"review@market\":true}}");
        Write(source, "plugins/known_marketplaces.json", "{\"market\":{\"source\":{\"source\":\"github\",\"repo\":\"example/plugins\",\"ref\":\"stable\"}}}");
        var fetcher = new FixtureFetcher(fail);
        var setup = new SetupImportService(new(workspace, data, user, Path.GetDirectoryName(user)!, new Dictionary<string, string> { ["claude-code"] = source }), fetcher);
        var service = new SessionImportService(new(workspace, data, new()),
            new(Path.Combine(user, "config.json"), Path.Combine(data, "config.json")),
            [new FakeImportSource("claude-code")], setup: setup);
        service.SetSessionService(new FakeSessionService());
        var candidate = Assert.Single(Assert.Single(await service.DetectAsync(["claude-code"])).Items);
        Assert.Equal(0, fetcher.Calls);
        var completion = new TaskCompletionSource<ImportCompletedNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Completed += batch => completion.TrySetResult(batch);
        service.Run(["claude-code"], new ImportSelection { User = ["plugins"] },
            [new ImportItemReference { Source = candidate.Source, SourceId = candidate.SourceId, Fingerprint = candidate.Fingerprint }]);
        var outcome = Assert.Single((await completion.Task.WaitAsync(TimeSpan.FromSeconds(10))).Outcomes);
        Assert.Equal(fail ? "failed" : "imported", outcome.Status);
        Assert.Equal(1, fetcher.Calls);
        Assert.Equal("stable", fetcher.Reference);
        var target = Path.Combine(user, "plugins", "claude-code.review.market");
        Assert.Equal(!fail, Directory.Exists(target));
        Assert.False(Directory.Exists(Path.Combine(user, "skills", "review")));
        if (!fail)
        {
            Assert.Contains("DotCraft", File.ReadAllText(Path.Combine(target, "skills", "review", "SKILL.md")));
            Assert.True(File.Exists(Path.Combine(target, ".craft-plugin", "plugin.json")));
        }
    }

    private sealed class FixtureFetcher(bool fail) : IMarketplaceGitFetcher
    {
        public int Calls { get; private set; }
        public string? Reference { get; private set; }
        public Task<string?> FetchAsync(MarketplaceSource source, string destination, CancellationToken ct)
        {
            Calls++;
            Reference = source.Ref;
            if (fail) throw new IOException("Fixture fetch failure");
            Write(destination, ".claude-plugin/marketplace.json", "{\"name\":\"market\",\"plugins\":[{\"name\":\"review\",\"source\":\"./plugins/review\"}]}");
            Write(destination, "plugins/review/.claude-plugin/plugin.json", "{\"name\":\"review\",\"skills\":\"./skills\"}");
            Write(destination, "plugins/review/skills/review/SKILL.md", "---\nname: review\ndescription: Review code\n---\nReview with Claude.");
            return Task.FromResult<string?>("fixture-revision");
        }
    }

    private static void Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
