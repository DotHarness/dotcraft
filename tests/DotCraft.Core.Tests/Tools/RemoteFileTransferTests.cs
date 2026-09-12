using System.Security.Cryptography;
using DotCraft.RemoteTools;
using DotCraft.Security;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class RemoteFileTransferTests
{
    [Fact]
    public async Task Local_supporting_files_round_trip_preserves_bytes_directories_and_overwrite_policy()
    {
        using var home = new TemporaryDirectory();
        using var remote = new TemporaryDirectory();
        using var local = new TemporaryDirectory();
        var storage = new RemoteToolHostStorage(home.Path, new MemoryCredentialStore());
        RemoteToolHostTestHost.Setup(storage, new Dictionary<string, string> { ["repo"] = remote.Path });
        await using var server = new RemoteToolHostTestServer(storage);
        await using var client = server.CreateClient();
        var workspace = new RemoteLocalWorkspace(local.Path, home.Path, new ApproveService());
        var source = Path.Combine(home.Path, "skills", "checker");
        Directory.CreateDirectory(Path.Combine(source, "empty"));
        var data = RandomNumberGenerator.GetBytes(3 * 1024 * 1024 + 13);
        await File.WriteAllBytesAsync(Path.Combine(source, "tool.bin"), data);
        await File.WriteAllTextAsync(Path.Combine(source, "zero.txt"), "");
        var connected = await client.ConnectAsync("thread", server.PeerId, "repo");
        var repeated = await client.ConnectAsync("thread", server.PeerId, "repo");
        Assert.True(repeated.AlreadyConnected);
        Assert.Equal(connected.Route, repeated.Route);
        List<RemoteFileTransferProgress> progress = [];
        var upload = await client.TransferAsync(
            "thread",
            new("upload", source, "tools"),
            workspace,
            reportProgress: progress.Add);
        Assert.True(upload.Success, upload.Error);
        Assert.Equal(2, upload.CompletedFiles);
        Assert.Equal(data.Length, upload.CompletedBytes);
        Assert.Equal(2, upload.TotalFiles);
        Assert.Equal(data.Length, upload.TotalBytes);
        Assert.Equal(data.Length, upload.TransferredBytes);
        Assert.Equal("preparing", progress[0].Stage);
        var transferring = progress.Where(value => value.Stage == "transferring").ToList();
        Assert.NotEmpty(transferring);
        Assert.All(transferring, value =>
        {
            Assert.Equal(2, value.TotalFiles);
            Assert.Equal(data.Length, value.TotalBytes);
        });
        Assert.Equal(transferring.Select(value => value.TransferredBytes).Order(),
            transferring.Select(value => value.TransferredBytes));
        Assert.Equal(2, transferring[^1].CompletedFiles);
        Assert.Equal(data.Length, transferring[^1].CompletedBytes);
        Assert.True(Directory.Exists(Path.Combine(remote.Path, "tools", "empty")));
        var conflict = await client.TransferAsync("thread", new("upload", source, "tools"), workspace);
        Assert.False(conflict.Success);
        var download = await client.TransferAsync("thread", new("download", "received", "tools"), workspace);
        Assert.True(download.Success, download.Error);
        Assert.Equal(data, await File.ReadAllBytesAsync(Path.Combine(local.Path, "received", "tool.bin")));
        await File.WriteAllTextAsync(Path.Combine(remote.Path, "tools", "extra.txt"), "keep");
        Assert.True((await client.TransferAsync("thread", new("upload", source, "tools", true), workspace)).Success);
        Assert.True(File.Exists(Path.Combine(remote.Path, "tools", "extra.txt")));
        Assert.True(client.TryGetRoute("thread", out var route));
        Assert.Equal(connected.Route, route);
        await client.DisconnectAsync("thread");
        Assert.Equal(data, await File.ReadAllBytesAsync(Path.Combine(remote.Path, "tools", "tool.bin")));
        Assert.Equal(data, await File.ReadAllBytesAsync(Path.Combine(source, "tool.bin")));
    }

    [Fact]
    public async Task Transfer_enforces_both_sides_blacklists_and_host_tool_policy()
    {
        using var home = new TemporaryDirectory();
        using var remote = new TemporaryDirectory();
        using var local = new TemporaryDirectory();
        var storage = new RemoteToolHostStorage(home.Path, new MemoryCredentialStore());
        RemoteToolHostTestHost.Setup(storage, new Dictionary<string, string> { ["repo"] = remote.Path });
        await using var server = new RemoteToolHostTestServer(storage);
        await using var client = server.CreateClient();
        await client.ConnectAsync("thread", server.PeerId, "repo");
        await File.WriteAllTextAsync(Path.Combine(local.Path, "secret.txt"), "secret");
        var workspace = new RemoteLocalWorkspace(local.Path, null, new ApproveService(),
            new PathBlacklist([Path.Combine(local.Path, "secret.txt")]));
        Assert.False((await client.TransferAsync("thread", new("upload", "secret.txt", "result.txt"), workspace)).Success);
        workspace = workspace with { Blacklist = null };
        Directory.CreateDirectory(Path.Combine(remote.Path, ".craft"));
        await File.WriteAllTextAsync(Path.Combine(remote.Path, ".craft", "config.json"),
            System.Text.Json.JsonSerializer.Serialize(new { Security = new { BlacklistedPaths = new[] { Path.Combine(remote.Path, "blocked.txt") } } }));
        Assert.False((await client.TransferAsync("thread", new("upload", "secret.txt", "blocked.txt"), workspace)).Success);
        Assert.False(File.Exists(Path.Combine(remote.Path, "blocked.txt")));
        storage.SaveHostState(storage.LoadHostState()! with { ToolPolicies = new() { ["WriteFile"] = "deny" } });
        Assert.False((await client.TransferAsync("thread", new("upload", "secret.txt", "result.txt"), workspace)).Success);
        Assert.False(File.Exists(Path.Combine(remote.Path, "result.txt")));
    }

    [Fact]
    public async Task Receiver_rejects_traversal_and_incomplete_or_corrupt_file_without_replacing_destination()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "file.bin");
        await File.WriteAllTextAsync(destination, "original");
        var data = new byte[] { 1, 2, 3 };
        var manifest = new TransferFileManifest(false, [new("", false, 3, Convert.ToHexString(SHA256.HashData(data)))]);
        await using (var session = new FileTransferSession(destination, manifest, true, true))
        {
            await session.PrepareAsync(100, (_, _, _) => Task.CompletedTask, default);
            await session.WriteAsync(0, 0, [1, 2], default);
            await Assert.ThrowsAsync<IOException>(() => session.CommitAsync(0, action => action(), default));
            await session.WriteAsync(0, 2, [4], default);
            await Assert.ThrowsAsync<IOException>(() => session.CommitAsync(0, action => action(), default));
        }
        Assert.Equal("original", await File.ReadAllTextAsync(destination));
        Assert.Empty(Directory.GetFiles(directory.Path, ".craft-transfer-*.tmp"));
        Assert.Throws<IOException>(() => TransferFileTree.ResolveEntry(directory.Path, "../outside"));
        Assert.Throws<IOException>(() => TransferFileTree.ResolveEntry(directory.Path, "C:/outside"));
    }
}
