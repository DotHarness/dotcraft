using System.Diagnostics;

namespace DotCraft.Scripting.Tests;

public sealed class ScriptEngineFactoryTests
{
    [Fact]
    public void Create_CatastrophicRegexStopsAtEngineTimeout()
    {
        var engine = ScriptEngineFactory.Create(
            new ScriptEngineLimits(64L * 1024 * 1024, 1_000_000, 128, TimeSpan.FromSeconds(1)),
            CancellationToken.None);
        var stopwatch = Stopwatch.StartNew();

        Assert.ThrowsAny<Exception>(() => engine.Evaluate("/^(a+)+$/.test('a'.repeat(40) + 'b')"));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Regex ran for {stopwatch.Elapsed}.");
    }
}
