using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Core.Tests.Tools;

public sealed class ToolResultBudgetTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "dotcraft-budget-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Apply_SpillsTheLargestResultsUntilTheRoundFits()
    {
        var small = new string('s', 10_000);
        IList<AIContent> contents =
        [
            new FunctionResultContent("call-a", new string('a', 120_000)),
            new FunctionResultContent("call-b", new string('b', 150_000)),
            new FunctionResultContent("call-c", small)
        ];

        ToolResultBudget.Apply(contents, _workspace, Path.Combine(_workspace, ".craft"), "thread-1", 40);

        var results = contents.Cast<FunctionResultContent>().ToDictionary(static r => r.CallId, static r => (string)r.Result!);
        Assert.Contains("full output at:", results["call-b"], StringComparison.Ordinal);
        Assert.Equal(new string('a', 120_000), results["call-a"]);
        Assert.Same(small, results["call-c"]);
        Assert.True(results.Values.Sum(static text => text.Length) <= ToolResultBudget.MaxCharsPerRound);
        var spilled = Assert.Single(Directory.GetFiles(Path.Combine(_workspace, ".craft", "tool-results", "thread-1")));
        Assert.Equal(new string('b', 150_000), File.ReadAllText(spilled));
    }

    [Fact]
    public void Apply_CountsTextInRichResultsAndKeepsResultsWithImagesWhole()
    {
        var image = new DataContent(new byte[] { 1, 2, 3 }, "image/png");
        IList<AIContent> contents =
        [
            .. Enumerable.Range(0, 6).Select(static index =>
                new FunctionResultContent($"call-{index}", new List<AIContent> { new TextContent(new string('t', 40_000)) })),
            new FunctionResultContent("call-image", new List<AIContent> { new TextContent(new string('i', 40_000)), image })
        ];

        ToolResultBudget.Apply(contents, _workspace, Path.Combine(_workspace, ".craft"), "thread-1", 40);

        var results = contents.Cast<FunctionResultContent>().ToList();
        Assert.Contains(results, static result => result.Result is string preview && preview.Contains("full output at:", StringComparison.Ordinal));
        Assert.Contains(image, Assert.IsType<List<AIContent>>(results.Single(static result => result.CallId == "call-image").Result));
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }
}
