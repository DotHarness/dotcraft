using System.Text.Json.Nodes;
using DotCraft.RemoteTools;
using DotCraft.Tools;
using ModelContextProtocol;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class RemoteTransferProtocolTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MalformedResponseKeepsConnectionAndDistinguishesUnknownCommit(bool commit, bool missingResult)
    {
        using var home = new TemporaryDirectory();
        using var local = new TemporaryDirectory();
        using var remote = new TemporaryDirectory();
        var storage = new RemoteToolHostStorage(home.Path, new MemoryCredentialStore());
        RemoteToolHostTestHost.Setup(storage, new Dictionary<string, string> { ["repo"] = remote.Path });
        await File.WriteAllTextAsync(Path.Combine(remote.Path, "remote.txt"), "remote bytes");
        await File.WriteAllTextAsync(Path.Combine(local.Path, "local.txt"), "local bytes");
        var target = commit ? RemoteFileTransferProtocol.Commit : RemoteFileTransferProtocol.Open;
        var injections = 0;
        var commits = 0;
        await using var server = new RemoteToolHostTestServer(storage, rewriteResponse: (method, response) =>
        {
            if (method == RemoteFileTransferProtocol.Commit) commits++;
            if (method != target || injections != 0) return response;
            injections++;
            if (missingResult) response!.AsObject().Remove("result");
            else if (commit) response!["result"] = "invalid commit acknowledgement";
            else response!["result"]!["manifest"] = "invalid manifest";
            return response;
        });
        await using var client = server.CreateClient();
        await client.ConnectAsync("thread", server.PeerId, "repo");
        var workspace = new RemoteLocalWorkspace(local.Path, home.Path, new ApproveService());
        var request = commit ? new RemoteFileTransferRequest("upload", "local.txt", "received.txt")
            : new RemoteFileTransferRequest("download", "received.txt", "remote.txt");

        var failed = await client.TransferAsync("thread", request, workspace);

        Assert.False(failed.Success);
        Assert.Equal(commit ? RemoteToolErrorCodes.RemoteOutcomeUnknown : RemoteToolErrorCodes.ProtocolMismatch, failed.ErrorCode);
        Assert.Equal(1, injections);
        if (commit)
        {
            Assert.Equal(1, commits);
            Assert.Equal("local bytes", await File.ReadAllTextAsync(Path.Combine(remote.Path, "received.txt")));
        }
        else Assert.Contains("manifest", failed.Error);
        var subsequent = await client.TransferAsync("thread", new("download", "followup.txt", "remote.txt"), workspace);
        Assert.True(subsequent.Success, subsequent.Error);
        Assert.Equal("remote bytes", await File.ReadAllTextAsync(Path.Combine(local.Path, "followup.txt")));
    }

    [Fact]
    public async Task ServerRpcErrorPreservesReasonWithoutReportingOffline()
    {
        using var home = new TemporaryDirectory();
        using var local = new TemporaryDirectory();
        using var remote = new TemporaryDirectory();
        var storage = new RemoteToolHostStorage(home.Path, new MemoryCredentialStore());
        RemoteToolHostTestHost.Setup(storage, new Dictionary<string, string> { ["repo"] = remote.Path });
        await File.WriteAllTextAsync(Path.Combine(remote.Path, "remote.txt"), "bytes");
        await using var server = new RemoteToolHostTestServer(storage, rewriteResponse: (method, response) =>
            method == RemoteFileTransferProtocol.Open
                ? throw new McpProtocolException("transfer rejected by server", McpErrorCode.InternalError)
                : response);
        await using var client = server.CreateClient();
        await client.ConnectAsync("thread", server.PeerId, "repo");

        var result = await client.TransferAsync("thread", new("download", "copy.txt", "remote.txt"),
            new(local.Path, home.Path, new ApproveService()));

        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.ExecutionFailed, result.ErrorCode);
        Assert.Contains("transfer rejected by server", result.Error);
    }
}
