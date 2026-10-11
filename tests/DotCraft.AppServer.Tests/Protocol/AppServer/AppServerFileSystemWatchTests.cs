using System.Text.Json;
using DotCraft.AppServer;
using Xunit;
using Rpc = DotCraft.Protocol.AppServer.AppServerRpc;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerFileSystemWatchTests : IDisposable
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(700);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"appserver_fswatch_{Guid.NewGuid():N}");
    private readonly AppServerTestHarness _harness = new();

    public AppServerFileSystemWatchTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        _harness.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task DirectoryWatch_CoalescesDirectChildChangesIntoOneNotification()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        await _harness.InitializeAsync();

        var watched = await SendAsync(Rpc.FsWatch.Name, new { watchId = "w1", path = _root });
        File.WriteAllText(Path.Combine(_root, "a.txt"), "a");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "b");
        File.WriteAllText(Path.Combine(_root, "sub", "nested.txt"), "n");

        var changed = await ReadChangedAsync();
        Assert.Equal(Path.GetFullPath(_root), watched.GetProperty("path").GetString());
        Assert.Equal("w1", changed.GetProperty("watchId").GetString());
        var paths = changed.GetProperty("changedPaths").EnumerateArray().Select(path => path.GetString()).ToArray();
        Assert.Contains(Path.Combine(_root, "a.txt"), paths);
        Assert.Contains(Path.Combine(_root, "b.txt"), paths);
        Assert.DoesNotContain(Path.Combine(_root, "sub", "nested.txt"), paths);
        await AssertQuietAsync();
    }

    [Fact]
    public async Task FileWatch_ReportsOnlyThatFile()
    {
        var file = Path.Combine(_root, "watched.txt");
        File.WriteAllText(file, "v1");
        await _harness.InitializeAsync();

        await SendAsync(Rpc.FsWatch.Name, new { watchId = "file", path = file });
        File.WriteAllText(Path.Combine(_root, "other.txt"), "x");
        await AssertQuietAsync();
        File.WriteAllText(file, "v2");

        var changed = await ReadChangedAsync();
        Assert.Equal([file], changed.GetProperty("changedPaths").EnumerateArray().Select(path => path.GetString()));
    }

    [Fact]
    public async Task MissingPath_IsWatchedThroughItsParentUntilCreated()
    {
        var later = Path.Combine(_root, "later.txt");
        await _harness.InitializeAsync();

        await SendAsync(Rpc.FsWatch.Name, new { watchId = "later", path = later });
        await _harness.ExecuteRequestAsync(_harness.BuildRequest(Rpc.FsWatch.Name, new { watchId = "orphan", path = Path.Combine(_root, "missing", "x.txt") }));
        using var orphan = await _harness.Transport.ReadNextSentAsync();
        File.WriteAllText(later, "created");

        var changed = await ReadChangedAsync();
        Assert.Equal("FileNotFound", orphan.RootElement.GetProperty("error").GetProperty("data").GetProperty("code").GetString());
        Assert.Equal([later], changed.GetProperty("changedPaths").EnumerateArray().Select(path => path.GetString()));
    }

    [Fact]
    public async Task Unwatch_StopsNotificationsAndToleratesUnknownIds()
    {
        await _harness.InitializeAsync();

        await SendAsync(Rpc.FsWatch.Name, new { watchId = "w", path = _root });
        await SendAsync(Rpc.FsUnwatch.Name, new { watchId = "w" });
        await SendAsync(Rpc.FsUnwatch.Name, new { watchId = "never-watched" });
        File.WriteAllText(Path.Combine(_root, "after.txt"), "x");

        await AssertQuietAsync();
    }

    [Fact]
    public async Task DuplicateActiveWatchId_IsInvalidParams()
    {
        var other = Path.Combine(_root, "other");
        Directory.CreateDirectory(other);
        await _harness.InitializeAsync();

        await SendAsync(Rpc.FsWatch.Name, new { watchId = "w", path = _root });
        await _harness.ExecuteRequestAsync(_harness.BuildRequest(Rpc.FsWatch.Name, new { watchId = "w", path = other }));
        using var duplicate = await _harness.Transport.ReadNextSentAsync();
        await SendAsync(Rpc.FsUnwatch.Name, new { watchId = "w" });
        await SendAsync(Rpc.FsWatch.Name, new { watchId = "w", path = other });

        Assert.Equal(AppServerErrors.InvalidParamsCode, duplicate.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task ClosingTheConnection_EndsItsWatches()
    {
        await _harness.InitializeAsync();

        await SendAsync(Rpc.FsWatch.Name, new { watchId = "w", path = _root });
        _harness.Connection.MarkClosed();
        await Task.Delay(100);
        File.WriteAllText(Path.Combine(_root, "after-close.txt"), "x");

        await AssertQuietAsync();
    }

    private async Task<JsonElement> SendAsync(string method, object parameters)
    {
        await _harness.ExecuteRequestAsync(_harness.BuildRequest(method, parameters));
        using var response = await _harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        return response.RootElement.GetProperty("result").Clone();
    }

    private async Task<JsonElement> ReadChangedAsync()
    {
        using var notification = await _harness.Transport.ReadNextSentAsync(TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsNotification(notification, Rpc.FsChanged.Name);
        return notification.RootElement.GetProperty("params").Clone();
    }

    private async Task AssertQuietAsync()
    {
        await Task.Delay(Quiet);
        var unexpected = _harness.Transport.TryReadSent();
        Assert.True(unexpected is null, unexpected?.RootElement.GetRawText());
    }
}
