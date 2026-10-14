using DotCraft.Dreams;
using Xunit;

namespace DotCraft.Tests.Dreams;

public sealed class ConsolidationLockTests : IDisposable
{
    private readonly string _store = Path.Combine(Path.GetTempPath(), "ConsolidationLock_" + Guid.NewGuid().ToString("N")[..8], "memory");

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(_store)!, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public void TryAcquire_RecordsTheAcquireTimeAndBlocksAnotherHolderUntilReleased()
    {
        var now = DateTimeOffset.UtcNow;

        var held = ConsolidationLock.TryAcquire(_store, now);

        Assert.NotNull(held);
        Assert.Null(held.PreviousConsolidatedAt);
        AssertTime(now, ConsolidationLock.LastConsolidatedAt(_store));
        Assert.Null(ConsolidationLock.TryAcquire(_store, now.AddMinutes(1)));

        held.Commit();
        var next = ConsolidationLock.TryAcquire(_store, now.AddMinutes(2));

        Assert.NotNull(next);
        AssertTime(now, next.PreviousConsolidatedAt);
        next.Rollback();
    }

    [Fact]
    public void Commit_KeepsTheAcquireTimeAndReleasesTheLock()
    {
        var acquiredAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        DreamsTestWorkspace.SetLastConsolidated(_store, acquiredAt.AddDays(-2));

        ConsolidationLock.TryAcquire(_store, acquiredAt)!.Commit();

        AssertTime(acquiredAt, ConsolidationLock.LastConsolidatedAt(_store));
        ConsolidationLock.TryAcquire(_store, DateTimeOffset.UtcNow)!.Rollback();
    }

    [Fact]
    public void Rollback_RestoresThePreviousConsolidationTime()
    {
        var previous = DateTimeOffset.UtcNow.AddDays(-2);
        DreamsTestWorkspace.SetLastConsolidated(_store, previous);

        ConsolidationLock.TryAcquire(_store, DateTimeOffset.UtcNow)!.Rollback();

        AssertTime(previous, ConsolidationLock.LastConsolidatedAt(_store));
        ConsolidationLock.TryAcquire(_store, DateTimeOffset.UtcNow)!.Rollback();
    }

    [Fact]
    public void Rollback_WithoutAPreviousConsolidation_KeepsTheLockFileAndForgetsTheTime()
    {
        ConsolidationLock.TryAcquire(_store, DateTimeOffset.UtcNow)!.Rollback();

        Assert.True(File.Exists(Path.Combine(_store, ConsolidationLock.FileName)));
        Assert.Null(ConsolidationLock.LastConsolidatedAt(_store));
        var next = ConsolidationLock.TryAcquire(_store, DateTimeOffset.UtcNow);
        Assert.NotNull(next);
        Assert.Null(next.PreviousConsolidatedAt);
        next.Rollback();
    }

    [Fact]
    public void TryAcquire_ReadsThePreviousConsolidationOnlyOnceItOwnsTheLock()
    {
        var settled = DateTimeOffset.UtcNow.AddDays(-2);
        DreamsTestWorkspace.SetLastConsolidated(_store, settled);
        var first = ConsolidationLock.TryAcquire(_store, DateTimeOffset.UtcNow)!;
        Assert.Null(ConsolidationLock.TryAcquire(_store, DateTimeOffset.UtcNow));

        first.Rollback();
        var second = ConsolidationLock.TryAcquire(_store, DateTimeOffset.UtcNow);

        Assert.NotNull(second);
        AssertTime(settled, second.PreviousConsolidatedAt);
        second.Rollback();
        AssertTime(settled, ConsolidationLock.LastConsolidatedAt(_store));
    }

    private static void AssertTime(DateTimeOffset expected, DateTimeOffset? actual)
    {
        Assert.NotNull(actual);
        Assert.True((expected - actual.Value).Duration() < TimeSpan.FromSeconds(1), $"Expected {expected:O}, got {actual:O}.");
    }
}
