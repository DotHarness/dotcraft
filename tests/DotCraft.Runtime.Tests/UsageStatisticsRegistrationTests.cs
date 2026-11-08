using DotCraft.Configuration;
using DotCraft.Persistence;
using DotCraft.Runtime;
using DotCraft.Sessions;
using DotCraft.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotCraft.Tests.Runtime;

public sealed class UsageStatisticsRegistrationTests
{
    [Fact]
    public void UserStatisticsRemainQueryableWhenWorkspaceCollectionIsDisabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "usage-runtime", Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "workspace");
        var user = Path.Combine(root, "user");
        try
        {
            using (var writer = Services(workspace, user, tracing: true))
            {
                var facts = writer.GetRequiredService<UsageStatisticsStore>();
                facts.ObserveThread(new SessionThread { Id = "thread", WorkspacePath = workspace, OriginChannel = "desktop" });
                facts.Record(new TokenUsageRecord
                {
                    ThreadId = "thread", TurnId = "turn_001", RootThreadId = "thread",
                    SourceId = "desktop", SubjectKind = TokenUsageSubjectKinds.Thread, SubjectId = "thread",
                    InputTokens = 42
                });
            }
            using (var reader = Services(workspace, user, tracing: false))
            {
                Assert.Null(reader.GetService<TraceStore>());
                var analytics = reader.GetRequiredService<UsageAnalyticsService>();
                Assert.Equal(42, analytics.GetSummary(UsageDateRange.Create(null, null, 0)).TotalTokens);
                Assert.Equal(1, analytics.GetProfileInsights().TotalThreads);
            }
            using (var embedded = Services(workspace, null, tracing: true))
            {
                Assert.Null(embedded.GetService<UsageAnalyticsService>());
                Assert.Null(embedded.GetService<UsageStatisticsStore>());
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ServiceProvider Services(string workspace, string? user, bool tracing)
    {
        var config = new AppConfig();
        config.Tracing.Enabled = tracing;
        var services = new ServiceCollection();
        services.AddDotCraftRuntime(new DotCraftRuntimeOptions
        {
            Config = config, WorkspacePath = workspace, DataPath = Path.Combine(workspace, ".craft"), UserDataPath = user
        });
        return services.BuildServiceProvider();
    }
}
