using System.Collections.Concurrent;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json.Nodes;
using DotCraft.RemoteTools;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class RemoteToolRegressionTests : IAsyncLifetime
{
    private readonly TemporaryDirectory _home = new();
    private readonly TemporaryDirectory _remote = new();
    private readonly TemporaryDirectory _local = new();
    private readonly ConcurrentQueue<RemoteToolHostDiagnostic> _diagnostics = new();
    private RemoteToolHostTestServer _server = null!;
    private RemoteToolHostClient _client = null!;
    private IReadOnlyList<ToolRegistration> _registrations = [];
    private RemoteToolRoute _route = null!;
    private RemoteLocalWorkspace Local => new(_local.Path, _home.Path, new ApproveService());

    public async Task InitializeAsync()
    {
        var storage = new RemoteToolHostStorage(_home.Path, new MemoryCredentialStore());
        RemoteToolHostTestHost.Setup(storage, new Dictionary<string, string> { ["repo"] = _remote.Path });
        _server = new(storage, diagnostic: _diagnostics.Enqueue);
        _client = _server.CreateClient();
        _registrations = await RemoteToolHostTestHost.AgentRegistrationsAsync(_local.Path, _home.Path);
        _client.UpdateRemoteToolSnapshot("agent-thread", new EffectiveToolSnapshotBuilder().Build(_registrations, 1), "agent");
        _route = (await _client.ConnectAsync("agent-thread", _server.PeerId, "repo")).Route;
    }

    public async Task DisposeAsync()
    {
        await _client.DisposeAsync();
        await _server.DisposeAsync();
        _home.Dispose();
        _remote.Dispose();
        _local.Dispose();
    }

    [Fact]
    public async Task RemoteFileWriteFailureKeepsStatusStateAndDiagnostics()
    {
        var failure = await Invoke("WriteFile", new() { ["path"] = ".", ["content"] = "cannot replace a directory" });
        Assert.False(failure.Success);
        Assert.NotNull(failure.Error);
        Assert.Equal("unknown", failure.StructuredContent!.Value.GetProperty("writeState").GetString());
        var diagnostic = Assert.Single(_diagnostics, d => d.Exception is not null);
        Assert.NotNull(diagnostic.Exception!.StackTrace);
        Assert.Contains("WriteFile", diagnostic.Message);
        Assert.Contains("call", diagnostic.Message);
        var rejected = await Invoke("EditFile", new() { ["path"] = "missing.txt", ["oldText"] = "x", ["newText"] = "y" });
        Assert.False(rejected.Success);
        Assert.Equal("notApplied", rejected.StructuredContent!.Value.GetProperty("writeState").GetString());
    }

    [Fact]
    public async Task RemoteTerminalReturnsChineseExitStatusAndRepeatedFinalPolls()
    {
        var started = await Invoke("Exec", new()
        {
            ["command"] = "Start-Sleep -Milliseconds 300; [Console]::Out.Write('远端标准输出'); [Console]::Error.Write('远端错误输出'); exit 7",
            ["shell"] = "pwsh", ["runInBackground"] = true, ["yieldTimeMs"] = 1
        });
        var session = started.Content!.Split('\n').Single(line => line.StartsWith("Session ID:", StringComparison.Ordinal))["Session ID:".Length..].Trim();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        for (var i = 0; i < 2; i++)
        {
            ToolExecutionResult end;
            do
            {
                end = await Invoke("WriteStdin", new() { ["sessionId"] = session, ["input"] = "", ["yieldTimeMs"] = 5000 }, deadline.Token);
                Assert.True(end.Success, end.Error?.Message);
            } while (end.Content!.Contains("Status: running", StringComparison.Ordinal));
            Assert.Null(end.Error);
            Assert.Contains("远端标准输出", end.Content);
            Assert.Contains("远端错误输出", end.Content);
            Assert.Contains("Exit code: 7", end.Content);
        }
        var nonempty = await Invoke("WriteStdin", new() { ["sessionId"] = session, ["input"] = "input" });
        Assert.False(nonempty.Success);
    }

    [Fact]
    public async Task MissingAndOccupiedDownloadsExposeServerCause()
    {
        var missing = await _client.TransferAsync("agent-thread", new("download", "local.txt", "missing.txt"), Local);
        Assert.False(missing.Success);
        Assert.Equal(RemoteToolErrorCodes.FileNotFound, missing.ErrorCode);
        Assert.Contains("files/open", missing.Error);
        Assert.Contains(_diagnostics, diagnostic => diagnostic.Exception is FileNotFoundException);
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_remote.Path, "active.log");
        await File.WriteAllTextAsync(path, "log content");
        await using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        var occupied = await _client.TransferAsync("agent-thread", new("download", "local.txt", "active.log"), Local);
        Assert.False(occupied.Success);
        Assert.Equal(RemoteToolErrorCodes.FileInUse, occupied.ErrorCode);
        Assert.Contains(_diagnostics, diagnostic => diagnostic.Exception is IOException io && (io.HResult & 0xFFFF) is 32 or 33);
    }

    [Fact]
    public async Task ChangedSourcePreservesCompletedFilesAndByteProgress()
    {
        var source = Path.Combine(_remote.Path, "source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "a.txt"), "first");
        var growing = Path.Combine(source, "b.log");
        await File.WriteAllTextAsync(growing, "original");
        var changed = false;
        var result = await _client.TransferAsync("agent-thread", new("download", "copy", "source"), Local,
            reportProgress: progress =>
            {
                if (progress.CompletedFiles == 1 && !changed)
                {
                    changed = true;
                    File.AppendAllText(growing, "appended by the log writer");
                }
            });
        Assert.True(changed);
        Assert.False(result.Success);
        Assert.Equal(RemoteToolErrorCodes.TransferSourceChanged, result.ErrorCode);
        Assert.Equal(1, result.CompletedFiles);
        Assert.Equal(5, result.CompletedBytes);
        Assert.Equal(5, result.TransferredBytes);
        Assert.Equal("first", await File.ReadAllTextAsync(Path.Combine(_local.Path, "copy", "a.txt")));
        Assert.False(File.Exists(Path.Combine(_local.Path, "copy", "b.log")));
    }

    [Fact]
    public async Task PermissionDeniedDownloadPreservesFilesystemCause()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_remote.Path, "denied.txt");
        await File.WriteAllTextAsync(path, "private");
        var file = new FileInfo(path);
        var original = file.GetAccessControl();
        var denied = file.GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        denied.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ReadData, AccessControlType.Deny));
        try
        {
            file.SetAccessControl(denied);
            var result = await _client.TransferAsync("agent-thread", new("download", "copy.txt", "denied.txt"), Local);
            Assert.False(result.Success);
            Assert.Equal(RemoteToolErrorCodes.FileAccessDenied, result.ErrorCode);
            Assert.Contains(_diagnostics, item => item.Exception is UnauthorizedAccessException);
        }
        finally { file.SetAccessControl(original); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedTransferKeepsCompletedFilesAndConnectionOrCancellationCode(bool cancel)
    {
        var source = Path.Combine(_remote.Path, "source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "a.txt"), "first");
        await File.WriteAllTextAsync(Path.Combine(source, "b.txt"), "second");
        using var cancellation = new CancellationTokenSource();
        var interrupted = false;
        var result = await _client.TransferAsync("agent-thread", new("download", "copy", "source"), Local,
            cancellation.Token, progress =>
            {
                if (progress.CompletedFiles != 1 || interrupted) return;
                interrupted = true;
                if (cancel) cancellation.Cancel();
                else _server.DropConnections();
            });
        Assert.True(interrupted);
        Assert.False(result.Success);
        if (cancel) Assert.Equal(ToolErrorCodes.Cancelled, result.ErrorCode);
        else Assert.Contains(result.ErrorCode, new[] { RemoteToolErrorCodes.HostOffline, RemoteToolErrorCodes.LeaseLost });
        Assert.Equal(1, result.CompletedFiles);
        Assert.Equal(5, result.CompletedBytes);
        Assert.Equal("first", await File.ReadAllTextAsync(Path.Combine(_local.Path, "copy", "a.txt")));
        Assert.False(File.Exists(Path.Combine(_local.Path, "copy", "b.txt")));
    }

    private async Task<ToolExecutionResult> Invoke(string name, JsonObject arguments, CancellationToken ct = default)
    {
        var tool = _registrations.Single(item => item.Definition.Name.Name == name);
        return await _client.InvokeAsync(_route, tool.Definition, RemoteToolContractHasher.Compute(tool.Definition),
            new("agent-thread", "turn", "call-" + Guid.NewGuid().ToString("N"), ToolInvocationAudience.Model,
                tool.Definition.Name, tool.Definition.Id, tool.Binding.Id, 1, DateTimeOffset.UtcNow, WorkspacePath: _local.Path), arguments, ct);
    }
}
