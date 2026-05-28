using System.Diagnostics;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Tools;

public class FileToolsConcurrencyTests
{
    [Fact]
    public async Task EditFile_ParallelNonOverlappingEdits_AllSucceed()
    {
        var workspace = CreateWorkspace();
        var filePath = Path.Combine(workspace, "notes.txt");
        await File.WriteAllTextAsync(filePath, string.Join('\n', Enumerable.Range(0, 16).Select(i => $"[[anchor-{i}]]")));

        var toolsA = new FileTools(workspace, requireApprovalOutsideWorkspace: false);
        var toolsB = new FileTools(workspace, requireApprovalOutsideWorkspace: false);
        var tasks = Enumerable.Range(0, 16)
            .Select(i => (i % 2 == 0 ? toolsA : toolsB).EditFile("notes.txt", $"[[anchor-{i}]]", $"replacement-{i}"))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, result => Assert.False(
            result.StartsWith("Error", StringComparison.Ordinal),
            result));
        var finalContent = await File.ReadAllTextAsync(filePath);
        for (var i = 0; i < 16; i++)
            Assert.Contains($"replacement-{i}", finalContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditFile_ParallelOverlappingEdits_FailsCleanly()
    {
        var workspace = CreateWorkspace();
        await File.WriteAllTextAsync(Path.Combine(workspace, "notes.txt"), "shared");
        var toolsA = new FileTools(workspace, requireApprovalOutsideWorkspace: false);
        var toolsB = new FileTools(workspace, requireApprovalOutsideWorkspace: false);

        var results = await Task.WhenAll(
            toolsA.EditFile("notes.txt", "shared", "first"),
            toolsB.EditFile("notes.txt", "shared", "second"));

        Assert.Single(results, result => result.StartsWith("Successfully", StringComparison.Ordinal));
        var error = Assert.Single(results, result => result.StartsWith("Error", StringComparison.Ordinal));
        Assert.Contains("oldText not found", error, StringComparison.Ordinal);
        Assert.DoesNotContain("being used by another process", string.Join('\n', results), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteFile_ParallelWrites_LastWriterWinsNoError()
    {
        var workspace = CreateWorkspace();
        var tools = new FileTools(workspace, requireApprovalOutsideWorkspace: false);
        var inputs = Enumerable.Range(0, 16).Select(i => $"content-{i}").ToArray();

        var results = await Task.WhenAll(inputs.Select(content => tools.WriteFile("notes.txt", content)));

        Assert.All(results, result => Assert.StartsWith("Successfully wrote", result, StringComparison.Ordinal));
        var finalContent = await File.ReadAllTextAsync(Path.Combine(workspace, "notes.txt"));
        Assert.Contains(finalContent, inputs);
    }

    [Fact]
    public async Task ReadFile_DoesNotBlockOnConcurrentReads()
    {
        var workspace = CreateWorkspace();
        var content = string.Join('\n', Enumerable.Range(0, 100_000).Select(i => $"line-{i:D6}"));
        await File.WriteAllTextAsync(Path.Combine(workspace, "large.txt"), content);
        var tools = new FileTools(workspace, requireApprovalOutsideWorkspace: false);

        var singleRead = Stopwatch.StartNew();
        await tools.ReadFile("large.txt");
        singleRead.Stop();

        var parallelRead = Stopwatch.StartNew();
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => tools.ReadFile("large.txt")));
        parallelRead.Stop();

        Assert.All(results, result =>
        {
            var text = Assert.Single(result.OfType<TextContent>());
            Assert.Contains("line-000000", text.Text, StringComparison.Ordinal);
        });
        Assert.True(
            parallelRead.ElapsedMilliseconds < Math.Max(singleRead.ElapsedMilliseconds * 12, 2_000),
            $"Parallel reads took {parallelRead.ElapsedMilliseconds}ms after a single read took {singleRead.ElapsedMilliseconds}ms.");
    }

    private static string CreateWorkspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "dotcraft-filetools-concurrency-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        return workspace;
    }
}
