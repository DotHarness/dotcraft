using System.Text.Json;
using DotCraft.AppServer;
using Xunit;
using Rpc = DotCraft.Protocol.AppServer.AppServerRpc;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerFileSystemTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"appserver_fs_{Guid.NewGuid():N}");
    private readonly AppServerTestHarness _harness = new();

    public AppServerFileSystemTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        _harness.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task CreateWriteRead_RoundTripsBytesUnderNewDirectories()
    {
        using var init = await _harness.InitializeAsync();
        Assert.True(init.RootElement.GetProperty("result").GetProperty("capabilities").GetProperty("fileSystem").GetBoolean());
        var directory = Path.Combine(_root, ".craft", "attachments", "a1");
        var file = Path.Combine(directory, "photo.bin");
        byte[] bytes = [0, 1, 2, 250, 255];

        Assert.Empty((await SendAsync(Rpc.FsCreateDirectory.Name, new { path = directory })).EnumerateObject());
        await SendAsync(Rpc.FsCreateDirectory.Name, new { path = directory, recursive = false });
        Assert.Empty((await SendAsync(Rpc.FsWriteFile.Name, new { path = file, dataBase64 = Convert.ToBase64String(bytes) })).EnumerateObject());
        var read = await SendAsync(Rpc.FsReadFile.Name, new { path = file });

        Assert.Equal(bytes, Convert.FromBase64String(read.GetProperty("dataBase64").GetString()!));
    }

    [Fact]
    public async Task RelativePathAndMalformedBase64_AreInvalidParams()
    {
        await _harness.InitializeAsync();

        var relative = await SendForErrorAsync(Rpc.FsReadFile.Name, new { path = Path.Combine("dir", "file.txt") });
        var badData = await SendForErrorAsync(Rpc.FsWriteFile.Name, new { path = Path.Combine(_root, "x.bin"), dataBase64 = "not base64!" });

        Assert.Equal(AppServerErrors.InvalidParamsCode, relative.GetProperty("code").GetInt32());
        Assert.Equal(AppServerErrors.InvalidParamsCode, badData.GetProperty("code").GetInt32());
        Assert.False(File.Exists(Path.Combine(_root, "x.bin")));
    }

    [Fact]
    public async Task BlacklistedPath_IsBlockedForEveryMethod()
    {
        var blocked = Path.Combine(_root, "secrets");
        Directory.CreateDirectory(blocked);
        File.WriteAllText(Path.Combine(blocked, "key.txt"), "secret");
        _harness.Monitor.Current.Security.BlacklistedPaths.Add(blocked);
        await _harness.InitializeAsync();

        var read = await SendForErrorAsync(Rpc.FsReadFile.Name, new { path = Path.Combine(blocked, "key.txt") });
        var write = await SendForErrorAsync(Rpc.FsWriteFile.Name, new { path = Path.Combine(blocked, "new.txt"), dataBase64 = "AA==" });
        var create = await SendForErrorAsync(Rpc.FsCreateDirectory.Name, new { path = Path.Combine(blocked, "sub") });

        Assert.All([read, write, create], error => Assert.Equal("PathBlocked", DataCode(error)));
        Assert.False(File.Exists(Path.Combine(blocked, "new.txt")));
        Assert.False(Directory.Exists(Path.Combine(blocked, "sub")));
    }

    [Fact]
    public async Task ReadFile_ReportsMissingDirectoryAndOversizedTargets()
    {
        var large = Path.Combine(_root, "large.bin");
        using (var stream = File.Create(large))
            stream.SetLength(8L * 1024 * 1024 + 1);
        await _harness.InitializeAsync();

        Assert.Equal("FileNotFound", DataCode(await SendForErrorAsync(Rpc.FsReadFile.Name, new { path = Path.Combine(_root, "missing.txt") })));
        Assert.Equal("NotAFile", DataCode(await SendForErrorAsync(Rpc.FsReadFile.Name, new { path = _root })));
        Assert.Equal("FileTooLarge", DataCode(await SendForErrorAsync(Rpc.FsReadFile.Name, new { path = large })));
    }

    [Fact]
    public async Task WriteAndCreate_RejectMissingParentAndExistingFile()
    {
        var file = Path.Combine(_root, "existing.txt");
        File.WriteAllText(file, "x");
        await _harness.InitializeAsync();

        var write = await SendForErrorAsync(Rpc.FsWriteFile.Name, new { path = Path.Combine(_root, "missing", "a.txt"), dataBase64 = "AA==" });
        var create = await SendForErrorAsync(Rpc.FsCreateDirectory.Name, new { path = file });

        Assert.Equal("DirectoryNotFound", DataCode(write));
        Assert.Equal("NotADirectory", DataCode(create));
        Assert.False(Directory.Exists(Path.Combine(_root, "missing")));
    }

    private async Task<JsonElement> SendAsync(string method, object parameters)
    {
        using var response = await ExecuteAsync(method, parameters);
        AppServerTestHarness.AssertIsSuccessResponse(response);
        return response.RootElement.GetProperty("result").Clone();
    }

    private async Task<JsonElement> SendForErrorAsync(string method, object parameters)
    {
        using var response = await ExecuteAsync(method, parameters);
        Assert.True(response.RootElement.TryGetProperty("error", out var error), response.RootElement.GetRawText());
        return error.Clone();
    }

    private async Task<JsonDocument> ExecuteAsync(string method, object parameters)
    {
        await _harness.ExecuteRequestAsync(_harness.BuildRequest(method, parameters));
        return _harness.Transport.TryReadSent()!;
    }

    private static string? DataCode(JsonElement error) =>
        error.GetProperty("data").GetProperty("code").GetString();
}
