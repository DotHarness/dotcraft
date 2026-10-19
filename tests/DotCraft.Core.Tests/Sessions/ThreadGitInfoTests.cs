using DotCraft.Sessions;
using DotCraft.Utilities;
using Xunit;

namespace DotCraft.Tests.Sessions;

public sealed class ThreadGitInfoTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"thread_git_info_{Guid.NewGuid():N}");

    public ThreadGitInfoTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                foreach (var file in Directory.EnumerateFiles(_tempDir, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task Read_ReturnsBranchAndCredentialFreeOrigin()
    {
        var repo = await CreateRepositoryAsync("feat/thread-git-info");
        await GitAsync(repo, "remote", "add", "origin", "https://user:secret-token@github.com/o/r.git");

        Assert.Equal(
            new ThreadGitInfo { Branch = "feat/thread-git-info", OriginUrl = "https://github.com/o/r.git" },
            await ThreadGitInfoProbe.ReadAsync(repo, CancellationToken.None));
    }

    [Fact]
    public async Task Read_RecordsNoBranchOnDetachedHead()
    {
        var repo = await CreateRepositoryAsync("main");
        await GitAsync(repo, "checkout", "--detach");

        Assert.Null(await ThreadGitInfoProbe.ReadAsync(repo, CancellationToken.None));
    }

    [Fact]
    public async Task Read_ReturnsNothingOutsideGit()
    {
        var plain = Directory.CreateDirectory(Path.Combine(_tempDir, "plain")).FullName;

        Assert.Null(await ThreadGitInfoProbe.ReadAsync(plain, CancellationToken.None));
    }

    [Theory]
    [InlineData("https://user:token@github.com/o/r.git", "https://github.com/o/r.git")]
    [InlineData("ssh://git@github.com/o/r.git", "ssh://github.com/o/r.git")]
    [InlineData("git@github.com:o/r.git", "github.com:o/r.git")]
    [InlineData("C:\\repos\\origin", "C:\\repos\\origin")]
    public void SanitizeRemoteUrl_RemovesUserInformation(string raw, string expected)
    {
        Assert.Equal(expected, ThreadGitInfoProbe.SanitizeRemoteUrl(raw));
    }

    [Fact]
    public async Task GitInfo_SurvivesReplayAndReachesTheIndex()
    {
        var craft = Directory.CreateDirectory(Path.Combine(_tempDir, ".craft")).FullName;
        var thread = new SessionThread
        {
            Id = "thread_git_info",
            WorkspacePath = _tempDir,
            OriginChannel = "test",
            Status = ThreadStatus.Active,
            HistoryMode = HistoryMode.Server,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            GitInfo = new ThreadGitInfo { Branch = "main", OriginUrl = "https://github.com/o/r.git" }
        };
        await new ThreadStore(craft).SaveThreadAsync(thread);

        thread.GitInfo = new ThreadGitInfo { Branch = "feat/next", OriginUrl = "https://github.com/o/r.git" };
        await new ThreadStore(craft).SaveThreadBaselineAsync(thread);

        var store = new ThreadStore(craft);
        var loaded = await store.LoadThreadAsync(thread.Id);
        var summary = Assert.Single(await store.LoadIndexAsync(), entry => entry.Id == thread.Id);
        Assert.Equal(thread.GitInfo, loaded?.GitInfo);
        Assert.Equal(thread.GitInfo, summary.GitInfo);
    }

    private async Task<string> CreateRepositoryAsync(string branch)
    {
        var repo = Directory.CreateDirectory(Path.Combine(_tempDir, "repo")).FullName;
        await GitAsync(repo, "init");
        await GitAsync(repo, "checkout", "-b", branch);
        await GitAsync(repo, "-c", "user.email=test@example.com", "-c", "user.name=Test", "commit", "--allow-empty", "-m", "init");
        return repo;
    }

    private static async Task GitAsync(string cwd, params string[] args)
    {
        var result = await GitProcessRunner.RunAsync(cwd, args, TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.True(result.ExitCode == 0, result.StdErr);
    }
}
