using Microsoft.EntityFrameworkCore;
using Oratorio.Server.Data;
using Oratorio.Server.Services;

namespace Oratorio.Server.Tests;

public sealed class TaskShortIdAllocatorTests
{
    [Theory]
    [InlineData("default", "DEF")]
    [InlineData("oratorio", "ORA")]
    [InlineData("x", "X")]
    [InlineData("", "WS")]
    [InlineData("---", "WS")]
    [InlineData("acme-bot-1", "ACM")]
    public void DerivePrefix_UsesWorkspaceIdCharacters(string workspaceId, string expected)
    {
        Assert.Equal(expected, TaskShortIdAllocator.DerivePrefix(workspaceId));
    }

    [Fact]
    public async Task ReserveAsync_AssignsMonotonicShortIds_PerWorkspace()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var allocator = new TaskShortIdAllocator(fixture.Db, new FixedClock(DateTimeOffset.Parse("2026-05-09T00:00:00Z")));
        var first = new OratorioItem { WorkspaceId = "oratorio" };
        var second = new OratorioItem { WorkspaceId = "oratorio" };

        await allocator.ReserveAsync(first, CancellationToken.None);
        await allocator.ReserveAsync(second, CancellationToken.None);

        Assert.Equal("ORA-1", first.ShortId);
        Assert.Equal(1, first.ShortIdInteger);
        Assert.Equal("ORA-2", second.ShortId);
        Assert.Equal(2, second.ShortIdInteger);
    }

    private sealed class SqliteFixture : IAsyncDisposable
    {
        private readonly string _path;

        private SqliteFixture(string path, OratorioDbContext db)
        {
            _path = path;
            Db = db;
        }

        public OratorioDbContext Db { get; }

        public static async Task<SqliteFixture> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), "oratorio-tests", $"{Guid.NewGuid():n}.db");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var options = new DbContextOptionsBuilder<OratorioDbContext>()
                .UseSqlite($"Data Source={path}")
                .Options;
            var db = new OratorioDbContext(options);
            await db.Database.EnsureCreatedAsync();
            return new SqliteFixture(path, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            TryDelete(_path);
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // SQLite may release temp file handles slightly after disposal on Windows.
            }
        }
    }
}
