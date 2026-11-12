using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Services;
using static DotCraft.Oratorio.Tests.OratorioApiTestSupport;

namespace DotCraft.Oratorio.Tests;

public sealed class DiscussionWorktreeRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discussion_ClaimsPreservedWorkspaceOrRejectsCleanedWorkspace(bool cleaned)
    {
        var root = Path.Combine(Path.GetTempPath(), $"oratorio-discussion-{Guid.NewGuid():n}");
        var checkout = Path.Combine(root, "checkout");
        Directory.CreateDirectory(checkout);
        try
        {
            await GitAsync(checkout, "init", "--initial-branch=main");
            await File.WriteAllTextAsync(Path.Combine(checkout, "work.txt"), "Initial work.\n");
            await GitAsync(checkout, "add", ".");
            await GitAsync(checkout, "commit", "-m", "Initial work");
            var clock = new MutableClock(DateTimeOffset.Parse("2026-11-12T00:00:00Z"));
            var appServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Disconnect);
            await using var app = new TestOratorioApp(services =>
            {
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.RemoveAll<IWorktreeManager>();
                services.RemoveAll<IClock>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(appServer);
                services.AddSingleton<IWorktreeManager, WorktreeManager>();
                services.AddSingleton<IClock>(clock);
            }, new Dictionary<string, string?>
            {
                ["Oratorio:DotCraft:RepositoryWorkspaceRoutes:0:WorkspacePath"] = checkout,
                ["Oratorio:DotCraft:WorktreeRoot"] = Path.Combine(root, "managed"),
                ["Oratorio:DotCraft:WorktreeCleanupEnabled"] = "false"
            });
            var client = app.CreateClient();
            var task = await PostAsync<ItemDetailResponse>(client, "/api/v1/local-tasks",
                new CreateLocalTaskRequest("Continue implementation", null, "example-owner/oratorio", null, null, null));
            var itemId = task.Item.ItemId;
            await PostAsync<ItemDetailResponse>(client, $"/api/v1/items/id/{itemId}/dispatch",
                new DispatchRequest("appServer", "Implement the task.", null, null, "implementation", DeliveryPolicy.ManualDelivery));
            var failed = await WaitForItemByIdAsync(client, itemId, x => x.Item.State == ItemState.Failed);
            var run = Assert.Single(failed.Runs);
            Assert.Equal("appServerDisconnected", run.ErrorCode);
            var worktree = run.WorktreePath!;
            var manager = app.Services.GetRequiredService<IWorktreeManager>();
            var oldCleanup = new WorktreeCleanupRequest(run.RunId, run.BaseWorkspacePath, worktree);
            await File.WriteAllTextAsync(Path.Combine(worktree, "work.txt"), "Staged implementation.\n");
            await GitAsync(worktree, "add", "work.txt");
            await File.AppendAllTextAsync(Path.Combine(worktree, "work.txt"), "Unstaged implementation.\n");
            await File.WriteAllTextAsync(Path.Combine(worktree, "new.txt"), "Untracked implementation.\n");

            if (cleaned)
            {
                await manager.CleanupAsync(oldCleanup, CancellationToken.None);
                using var scope = app.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
                var recorded = await db.Runs.SingleAsync(x => x.RunId == run.RunId);
                recorded.WorktreeStatus = WorktreeStatus.Cleaned;
                await db.SaveChangesAsync();
            }

            appServer.Outcome = FakeAppServerOutcome.Hold;
            await PostAsync<ItemDetailResponse>(client, $"/api/v1/items/id/{itemId}/discussion-turns",
                new DiscussionTurnRequest("Explain the existing changes.", null, null));
            var discussion = await WaitForItemByIdAsync(client, itemId, x => x.DiscussionTurns.Any(turn =>
                cleaned ? turn.Status == DiscussionTurnStatus.Failed : turn.Status == DiscussionTurnStatus.Running && turn.TurnId != null));
            var turn = Assert.Single(discussion.DiscussionTurns);
            if (cleaned)
            {
                Assert.Equal("implementationWorktreeMissing", turn.ErrorCode);
                Assert.Empty(appServer.ThreadResumeRequests);
                Assert.False(Directory.Exists(worktree));
                return;
            }

            clock.Advance(run.WorktreeCleanupAfterAt!.Value - clock.UtcNow + TimeSpan.FromSeconds(1));
            await manager.CleanupAsync(oldCleanup, CancellationToken.None);

            Assert.Equal(run.ThreadId, turn.ThreadId);
            Assert.Equal("Staged implementation.\nUnstaged implementation.\n",
                await File.ReadAllTextAsync(Path.Combine(worktree, "work.txt")));
            Assert.Equal("Untracked implementation.\n", await File.ReadAllTextAsync(Path.Combine(worktree, "new.txt")));
            Assert.Contains("Staged implementation.", await GitAsync(worktree, "diff", "--cached"));
            using var verification = app.Services.CreateScope();
            var discussionRecord = await verification.ServiceProvider.GetRequiredService<OratorioDbContext>()
                .DiscussionTurns.SingleAsync(x => x.DiscussionTurnId == turn.DiscussionTurnId);
            Assert.Equal(WorktreeStatus.Ready, discussionRecord.WorktreeStatus);
            Assert.Equal(worktree, discussionRecord.WorktreePath);
        }
        finally
        {
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string> GitAsync(string path, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.Environment["GIT_AUTHOR_NAME"] = start.Environment["GIT_COMMITTER_NAME"] = "Oratorio Tests";
        start.Environment["GIT_AUTHOR_EMAIL"] = start.Environment["GIT_COMMITTER_EMAIL"] = "oratorio@example.test";
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
        return output;
    }
}
