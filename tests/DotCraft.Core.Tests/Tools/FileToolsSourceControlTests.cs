using DotCraft.SourceControl;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class FileToolsSourceControlTests
{
    [Fact]
    public async Task WriteFile_ExistingFile_PreparesPerforceEditBeforeWriting()
    {
        using var workspace = new TempWorkspace();
        var file = Path.Combine(workspace.Path, "notes.txt");
        await File.WriteAllTextAsync(file, "old");
        var coordinator = new RecordingCoordinator();
        var tools = new FileTools(workspace.Path, requireApprovalOutsideWorkspace: false, sourceControlWriteCoordinator: coordinator);

        var result = await tools.WriteFile("notes.txt", "new");

        Assert.StartsWith("Successfully wrote", result, StringComparison.Ordinal);
        Assert.Equal("new", await File.ReadAllTextAsync(file));
        Assert.Equal([(file, true)], coordinator.BeforeCalls);
        Assert.Equal([(file, true)], coordinator.AfterCalls);
    }

    [Fact]
    public async Task WriteFile_NewFile_RunsPerforceAddAfterWriting()
    {
        using var workspace = new TempWorkspace();
        var file = Path.Combine(workspace.Path, "new.txt");
        var coordinator = new RecordingCoordinator();
        var tools = new FileTools(workspace.Path, requireApprovalOutsideWorkspace: false, sourceControlWriteCoordinator: coordinator);

        var result = await tools.WriteFile("new.txt", "hello");

        Assert.StartsWith("Successfully wrote", result, StringComparison.Ordinal);
        Assert.Equal([(file, false)], coordinator.BeforeCalls);
        Assert.Equal([(file, false)], coordinator.AfterCalls);
    }

    [Fact]
    public async Task EditFile_StopsWhenPerforceEditFails()
    {
        using var workspace = new TempWorkspace();
        var file = Path.Combine(workspace.Path, "notes.txt");
        await File.WriteAllTextAsync(file, "old");
        var coordinator = new RecordingCoordinator
        {
            BeforeResult = SourceControlWriteResult.Error("Perforce login is required before writing files.")
        };
        var tools = new FileTools(workspace.Path, requireApprovalOutsideWorkspace: false, sourceControlWriteCoordinator: coordinator);

        var result = await tools.EditFile("notes.txt", "old", "new");

        Assert.Equal("Error: Perforce login is required before writing files.", result);
        Assert.Equal("old", await File.ReadAllTextAsync(file));
        Assert.Empty(coordinator.AfterCalls);
    }

    private sealed class RecordingCoordinator : ISourceControlWriteCoordinator
    {
        public SourceControlWriteResult BeforeResult { get; init; } = SourceControlWriteResult.Ok();
        public SourceControlWriteResult AfterResult { get; init; } = SourceControlWriteResult.Ok();
        public List<(string Path, bool Exists)> BeforeCalls { get; } = [];
        public List<(string Path, bool ExistedBefore)> AfterCalls { get; } = [];

        public Task<SourceControlWriteResult> BeforeWriteAsync(string fullPath, bool fileExists, CancellationToken ct = default)
        {
            BeforeCalls.Add((fullPath, fileExists));
            return Task.FromResult(BeforeResult);
        }

        public Task<SourceControlWriteResult> AfterWriteAsync(string fullPath, bool fileExistedBefore, CancellationToken ct = default)
        {
            AfterCalls.Add((fullPath, fileExistedBefore));
            return Task.FromResult(AfterResult);
        }
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "filetools-sc-tests", Guid.NewGuid().ToString("N"));

        public TempWorkspace() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
