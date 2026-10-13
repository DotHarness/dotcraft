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
    public void TryAcquire_RecordsTheAcquireTimeAndBlocksWhileTheHolderIsAlive()
    {
        var now = DateTimeOffset.UtcNow;

        var held = ConsolidationLock.TryAcquire(_store, now);

        Assert.NotNull(held);
        Assert.Null(held.PreviousConsolidatedAt);
        AssertTime(now, ConsolidationLock.LastConsolidatedAt(_store));
        Assert.Null(ConsolidationLock.TryAcquire(_store, now.AddMinutes(1)));
    }

    [Fact]
    public void TryAcquire_TakesOverADeadOrTimedOutHolder()
    {
        var now = DateTimeOffset.UtcNow;
        DreamsTestWorkspace.SetLastConsolidated(_store, now.AddMinutes(-5), holder: int.MaxValue.ToString());
        Assert.NotNull(ConsolidationLock.TryAcquire(_store, now));

        DreamsTestWorkspace.SetLastConsolidated(_store, now.AddHours(-2), holder: Environment.ProcessId.ToString());
        var held = ConsolidationLock.TryAcquire(_store, now);

        Assert.NotNull(held);
        AssertTime(now.AddHours(-2), held.PreviousConsolidatedAt);
    }

    [Fact]
    public void Commit_KeepsTheAcquireTimeAndReleasesTheHolder()
    {
        var acquiredAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        DreamsTestWorkspace.SetLastConsolidated(_store, acquiredAt.AddDays(-2));

        ConsolidationLock.TryAcquire(_store, acquiredAt)!.Commit();

        AssertTime(acquiredAt, ConsolidationLock.LastConsolidatedAt(_store));
        Assert.NotNull(ConsolidationLock.TryAcquire(_store, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Rollback_RestoresThePreviousConsolidationTime()
    {
        var previous = DateTimeOffset.UtcNow.AddDays(-2);
        DreamsTestWorkspace.SetLastConsolidated(_store, previous);

        ConsolidationLock.TryAcquire(_store, DateTimeOffset.UtcNow)!.Rollback();

        AssertTime(previous, ConsolidationLock.LastConsolidatedAt(_store));
        Assert.NotNull(ConsolidationLock.TryAcquire(_store, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Rollback_WithoutAPreviousConsolidation_RemovesTheLock()
    {
        ConsolidationLock.TryAcquire(_store, DateTimeOffset.UtcNow)!.Rollback();

        Assert.Null(ConsolidationLock.LastConsolidatedAt(_store));
    }

    private static void AssertTime(DateTimeOffset expected, DateTimeOffset? actual)
    {
        Assert.NotNull(actual);
        Assert.True((expected - actual.Value).Duration() < TimeSpan.FromSeconds(1), $"Expected {expected:O}, got {actual:O}.");
    }
}
