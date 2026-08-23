using System.Collections.Concurrent;
using DotCraft.Contributions;
using Xunit;

namespace DotCraft.Tests.Contributions;

public sealed class ContributionRegistryConcurrencyTests
{
    [Fact]
    public async Task Registry_ToleratesConcurrentAddsDisposalsAndReads()
    {
        const int writerCount = 8;
        const int perWriter = 100;
        const int readsPerReader = 200;
        var logLines = new List<string>();
        var registry = new ContributionRegistry(new CollectingLogger<ContributionRegistry>(logLines));
        var failures = new ConcurrentQueue<Exception>();

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            try
            {
                for (var read = 0; read < readsPerReader; read++)
                {
                    // The materialized view must never be observed mid-mutation.
                    foreach (var contribution in registry.Resolve<ILabelContract>("thread-1"))
                        Assert.False(string.IsNullOrEmpty(contribution.Label));
                }
            }
            catch (Exception exception)
            {
                failures.Enqueue(exception);
            }
        })).ToArray();

        var writers = Enumerable.Range(0, writerCount).Select(writer => Task.Run(() =>
        {
            try
            {
                for (var index = 0; index < perWriter; index++)
                {
                    var keep = registry.Add<ILabelContract>(
                        new LabelContribution($"w{writer}-keep{index}"),
                        new ContributionOptions(Order: index % 5));
                    var transient = registry.Add<ILabelContract>(
                        new LabelContribution($"w{writer}-transient{index}"),
                        ContributionOptions.ForThread("thread-1"));
                    transient.Dispose();
                    if (index % 3 == 0)
                        keep.Dispose();
                }
            }
            catch (Exception exception)
            {
                failures.Enqueue(exception);
            }
        })).ToArray();

        await Task.WhenAll([.. writers, .. readers]);

        Assert.Empty(failures);

        var expectedKept = writerCount * perWriter - writerCount * ((perWriter + 2) / 3);
        Assert.Equal(expectedKept, registry.Resolve<ILabelContract>().Count);
        Assert.Equal(expectedKept, registry.Resolve<ILabelContract>("thread-1").Count);
        AssertOrdered(registry.Resolve<ILabelContract>());
        Assert.Empty(logLines);
    }

    [Fact]
    public async Task ReleaseThread_IsSafeWhileThreadScopedContributionsAreBeingAdded()
    {
        var registry = new ContributionRegistry();
        var failures = new ConcurrentQueue<Exception>();

        var adder = Task.Run(() =>
        {
            try
            {
                for (var index = 0; index < 500; index++)
                {
                    registry.Add<ILabelContract>(
                        new LabelContribution($"t{index}"),
                        ContributionOptions.ForThread("thread-1"));
                }
            }
            catch (Exception exception)
            {
                failures.Enqueue(exception);
            }
        });

        var releaser = Task.Run(() =>
        {
            try
            {
                for (var index = 0; index < 50; index++)
                    registry.ReleaseThread("thread-1");
            }
            catch (Exception exception)
            {
                failures.Enqueue(exception);
            }
        });

        await Task.WhenAll(adder, releaser);
        registry.ReleaseThread("thread-1");

        Assert.Empty(failures);
        Assert.Empty(registry.Resolve<ILabelContract>("thread-1"));
    }

    private static void AssertOrdered(IReadOnlyList<ILabelContract> contributions)
    {
        // Labels encode their order key as the trailing index modulo five.
        var previous = int.MinValue;
        foreach (var contribution in contributions)
        {
            var order = int.Parse(contribution.Label[(contribution.Label.LastIndexOf("keep", StringComparison.Ordinal) + 4)..])
                % 5;
            Assert.True(order >= previous, $"Contribution '{contribution.Label}' broke the ascending order.");
            previous = order;
        }
    }
}
