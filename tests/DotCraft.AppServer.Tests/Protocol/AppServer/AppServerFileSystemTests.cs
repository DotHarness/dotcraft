using System.Diagnostics;
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
        var metadata = await SendForErrorAsync(Rpc.FsGetMetadata.Name, new { path = Path.Combine(blocked, "key.txt") });
        var list = await SendForErrorAsync(Rpc.FsReadDirectory.Name, new { path = blocked });
        var remove = await SendForErrorAsync(Rpc.FsRemove.Name, new { path = Path.Combine(blocked, "key.txt") });
        var watch = await SendForErrorAsync(Rpc.FsWatch.Name, new { watchId = "w", path = blocked });

        Assert.All([read, write, create, metadata, list, remove, watch], error => Assert.Equal("PathBlocked", DataCode(error)));
        Assert.False(File.Exists(Path.Combine(blocked, "new.txt")));
        Assert.False(Directory.Exists(Path.Combine(blocked, "sub")));
        Assert.True(File.Exists(Path.Combine(blocked, "key.txt")));
    }

    [Fact]
    public async Task Copy_ChecksBothPathsAgainstTheBlacklist()
    {
        var blocked = Path.Combine(_root, "secrets");
        Directory.CreateDirectory(blocked);
        File.WriteAllText(Path.Combine(blocked, "key.txt"), "secret");
        var open = Path.Combine(_root, "open.txt");
        File.WriteAllText(open, "public");
        _harness.Monitor.Current.Security.BlacklistedPaths.Add(blocked);
        await _harness.InitializeAsync();

        var fromBlocked = await SendForErrorAsync(Rpc.FsCopy.Name, new { sourcePath = Path.Combine(blocked, "key.txt"), destinationPath = Path.Combine(_root, "leak.txt") });
        var intoBlocked = await SendForErrorAsync(Rpc.FsCopy.Name, new { sourcePath = open, destinationPath = Path.Combine(blocked, "planted.txt") });

        Assert.Equal("PathBlocked", DataCode(fromBlocked));
        Assert.Equal("PathBlocked", DataCode(intoBlocked));
        Assert.False(File.Exists(Path.Combine(_root, "leak.txt")));
        Assert.False(File.Exists(Path.Combine(blocked, "planted.txt")));
    }

    [Fact]
    public async Task BlacklistedTargetBehindALink_IsBlockedForEveryMethod()
    {
        var blocked = Path.Combine(_root, "secrets");
        Directory.CreateDirectory(blocked);
        File.WriteAllText(Path.Combine(blocked, "key.txt"), "secret");
        var link = Path.Combine(_root, "workspace", "link");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        if (!TryCreateDirectoryLink(link, blocked))
            return;
        _harness.Monitor.Current.Security.BlacklistedPaths.Add(blocked);
        await _harness.InitializeAsync();

        var read = await SendForErrorAsync(Rpc.FsReadFile.Name, new { path = Path.Combine(link, "key.txt") });
        var write = await SendForErrorAsync(Rpc.FsWriteFile.Name, new { path = Path.Combine(link, "new.txt"), dataBase64 = "AA==" });
        var create = await SendForErrorAsync(Rpc.FsCreateDirectory.Name, new { path = Path.Combine(link, "sub") });
        var remove = await SendForErrorAsync(Rpc.FsRemove.Name, new { path = link });

        Assert.All([read, write, create, remove], error => Assert.Equal("PathBlocked", DataCode(error)));
        Assert.False(File.Exists(Path.Combine(blocked, "new.txt")));
        Assert.False(Directory.Exists(Path.Combine(blocked, "sub")));
        Assert.True(Directory.Exists(link));
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

    [Fact]
    public async Task GetMetadataAndReadDirectory_DescribeFilesDirectoriesAndLinks()
    {
        var directory = Path.Combine(_root, "dir");
        Directory.CreateDirectory(Path.Combine(directory, "child"));
        var file = Path.Combine(directory, "a.txt");
        File.WriteAllText(file, "a");
        await _harness.InitializeAsync();

        var fileMetadata = await SendAsync(Rpc.FsGetMetadata.Name, new { path = file });
        var directoryMetadata = await SendAsync(Rpc.FsGetMetadata.Name, new { path = directory });
        var listing = await SendAsync(Rpc.FsReadDirectory.Name, new { path = directory });

        Assert.True(fileMetadata.GetProperty("isFile").GetBoolean());
        Assert.False(fileMetadata.GetProperty("isDirectory").GetBoolean());
        Assert.False(fileMetadata.GetProperty("isSymlink").GetBoolean());
        Assert.True(fileMetadata.GetProperty("modifiedAtMs").GetInt64() > 0);
        Assert.True(directoryMetadata.GetProperty("isDirectory").GetBoolean());
        Assert.Equal(
            [("a.txt", false, true), ("child", true, false)],
            listing.GetProperty("entries").EnumerateArray()
                .Select(entry => (entry.GetProperty("fileName").GetString(), entry.GetProperty("isDirectory").GetBoolean(), entry.GetProperty("isFile").GetBoolean()))
                .OrderBy(entry => entry.Item1, StringComparer.Ordinal));
        Assert.Equal("FileNotFound", DataCode(await SendForErrorAsync(Rpc.FsGetMetadata.Name, new { path = Path.Combine(_root, "missing") })));
        Assert.Equal("FileNotFound", DataCode(await SendForErrorAsync(Rpc.FsReadDirectory.Name, new { path = Path.Combine(_root, "missing") })));
        Assert.Equal("NotADirectory", DataCode(await SendForErrorAsync(Rpc.FsReadDirectory.Name, new { path = file })));

        var link = Path.Combine(_root, "link");
        if (!TryCreateDirectoryLink(link, directory))
            return;
        var linkMetadata = await SendAsync(Rpc.FsGetMetadata.Name, new { path = link });
        Assert.True(linkMetadata.GetProperty("isSymlink").GetBoolean());
        Assert.True(linkMetadata.GetProperty("isDirectory").GetBoolean());
    }

    [Fact]
    public async Task Remove_DeletesTreesAndHonorsRecursiveAndForce()
    {
        var tree = Path.Combine(_root, "tree");
        Directory.CreateDirectory(Path.Combine(tree, "nested"));
        File.WriteAllText(Path.Combine(tree, "nested", "x.txt"), "x");
        var file = Path.Combine(_root, "file.txt");
        File.WriteAllText(file, "f");
        var missing = Path.Combine(_root, "missing");
        await _harness.InitializeAsync();

        var notEmpty = await SendForErrorAsync(Rpc.FsRemove.Name, new { path = tree, recursive = false });
        var notForced = await SendForErrorAsync(Rpc.FsRemove.Name, new { path = missing, force = false });
        await SendAsync(Rpc.FsRemove.Name, new { path = missing });
        await SendAsync(Rpc.FsRemove.Name, new { path = file });
        await SendAsync(Rpc.FsRemove.Name, new { path = tree });

        Assert.Equal("DirectoryNotEmpty", DataCode(notEmpty));
        Assert.Equal("FileNotFound", DataCode(notForced));
        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(tree));
    }

    [Fact]
    public async Task Copy_CopiesFilesAndDirectoryTrees()
    {
        var source = Path.Combine(_root, "src");
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        File.WriteAllText(Path.Combine(source, "nested", "x.txt"), "x");
        var file = Path.Combine(_root, "a.txt");
        File.WriteAllText(file, "new");
        var existing = Path.Combine(_root, "b.txt");
        File.WriteAllText(existing, "old");
        var copy = Path.Combine(_root, "copy");
        await _harness.InitializeAsync();

        var withoutRecursive = await SendForErrorAsync(Rpc.FsCopy.Name, new { sourcePath = source, destinationPath = copy });
        var intoItself = await SendForErrorAsync(Rpc.FsCopy.Name, new { sourcePath = source, destinationPath = Path.Combine(source, "inner"), recursive = true });
        var missingSource = await SendForErrorAsync(Rpc.FsCopy.Name, new { sourcePath = Path.Combine(_root, "missing"), destinationPath = copy });
        var missingParent = await SendForErrorAsync(Rpc.FsCopy.Name, new { sourcePath = file, destinationPath = Path.Combine(_root, "no", "a.txt") });
        await SendAsync(Rpc.FsCopy.Name, new { sourcePath = file, destinationPath = existing });
        await SendAsync(Rpc.FsCopy.Name, new { sourcePath = source, destinationPath = copy, recursive = true });

        Assert.Equal(AppServerErrors.InvalidParamsCode, withoutRecursive.GetProperty("code").GetInt32());
        Assert.Equal(AppServerErrors.InvalidParamsCode, intoItself.GetProperty("code").GetInt32());
        Assert.Equal("FileNotFound", DataCode(missingSource));
        Assert.Equal("DirectoryNotFound", DataCode(missingParent));
        Assert.Equal("new", File.ReadAllText(existing));
        Assert.Equal("x", File.ReadAllText(Path.Combine(copy, "nested", "x.txt")));
        Assert.False(Directory.Exists(Path.Combine(source, "inner")));
    }

    [Fact]
    public async Task Remove_DeletesALinkWithoutTouchingItsTarget()
    {
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(Path.Combine(target, "nested"));
        File.WriteAllText(Path.Combine(target, "nested", "keep.txt"), "keep");
        var link = Path.Combine(_root, "link");
        if (!TryCreateDirectoryLink(link, target))
            return;
        await _harness.InitializeAsync();

        await SendAsync(Rpc.FsRemove.Name, new { path = link });

        Assert.False(Directory.Exists(link));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(target, "nested", "keep.txt")));
    }

    [Fact]
    public async Task Copy_DoesNotFollowDirectoryLinksInsideTheTree()
    {
        var source = Path.Combine(_root, "src");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "a.txt"), "a");
        if (!TryCreateDirectoryLink(Path.Combine(source, "loop"), source))
            return;
        var copy = Path.Combine(_root, "copy");
        await _harness.InitializeAsync();

        await SendAsync(Rpc.FsCopy.Name, new { sourcePath = source, destinationPath = copy, recursive = true });

        Assert.Equal("a", File.ReadAllText(Path.Combine(copy, "a.txt")));
    }

    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            if (!OperatingSystem.IsWindows())
                return false;
        }

        using var junction = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        junction.WaitForExit();
        return junction.ExitCode == 0 && Directory.Exists(link);
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
