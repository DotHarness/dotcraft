using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.RemoteTools;
using DotCraft.Tools;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class RemoteExecutionProtocolTests
{
    [Fact]
    public async Task TransferIdsAndLeaseAdmissionBelongToConnection_AndCloseRemovesOnlyItsPartialFiles()
    {
        await using var fixture = await RemoteExecutionFixture.CreateAsync();
        await using var firstConnection = await fixture.ConnectionAsync();
        await using var secondConnection = await fixture.ConnectionAsync();
        await using var first = await McpClient.CreateAsync(firstConnection.Transport);
        await using var second = await McpClient.CreateAsync(secondConnection.Transport);
        var lease = await Acquire(first);
        var borrowed = await Send<FileTransferOpen, FileTransferOpened>(second, RemoteFileTransferProtocol.Open,
            new(lease.LeaseId, "repo", "no.txt", true, Manifest: Manifest()));
        Assert.Equal(RemoteToolErrorCodes.LeaseLost, borrowed.Error?.Code);
        Assert.Equal(lease.LeaseId, (await Acquire(second)).LeaseId);
        var a = await StartTransfer(first, lease.LeaseId, "a.txt");
        var b = await StartTransfer(second, lease.LeaseId, "b.txt");
        var forbidden = await Send<FileTransferPart, JsonObject>(first, RemoteFileTransferProtocol.Write,
            new(b, Base64: Convert.ToBase64String([1, 2])));
        Assert.False(forbidden.Success);
        Assert.True((await Send<FileTransferPart, JsonObject>(first, RemoteFileTransferProtocol.Write, new(a, Base64: "AQ=="))).Success);
        Assert.True((await Send<FileTransferPart, JsonObject>(second, RemoteFileTransferProtocol.Write, new(b, Base64: "AQI="))).Success);
        Assert.Equal(2, Directory.GetFiles(fixture.Workspace.Path, ".craft-transfer-*.tmp").Length);

        await first.DisposeAsync();
        await firstConnection.DisposeAsync();
        await fixture.WaitAsync(() => Directory.GetFiles(fixture.Workspace.Path, ".craft-transfer-*.tmp").Length == 1, TimeSpan.FromSeconds(10));
        Assert.True((await Send<FileTransferPart, JsonObject>(second, RemoteFileTransferProtocol.Commit, new(b))).Success);
        Assert.Equal(new byte[] { 1, 2 }, await File.ReadAllBytesAsync(Path.Combine(fixture.Workspace.Path, "b.txt")));
        Assert.False(File.Exists(Path.Combine(fixture.Workspace.Path, "a.txt")));
        Assert.True(fixture.Server.Leases.HasActiveLease);
        await second.DisposeAsync();
        await secondConnection.DisposeAsync();
        await fixture.WaitAsync(() => !fixture.Server.Leases.HasActiveLease, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task InvocationWithoutThreadIdentityCannotExecute()
    {
        await using var fixture = await RemoteExecutionFixture.CreateAsync();
        await using var client = await fixture.Server.ConnectRawAsync();
        var lease = await Acquire(client);
        var tool = fixture.Tool("WriteFile").Definition;
        var invocation = JsonSerializer.SerializeToNode(new RemoteInvocationMeta(
            lease.LeaseId, "repo", "invocation", tool.Id.ToString(), RemoteToolContractHasher.Compute(tool),
            "thread", "turn", 1000, 10), RemoteToolHostProtocol.JsonOptions)!.AsObject();
        invocation.Remove("threadId");

        var result = await client.CallToolAsync(new CallToolRequestParams
        {
            Name = tool.Name.ToString(),
            Arguments = new Dictionary<string, JsonElement>
            {
                ["path"] = JsonSerializer.SerializeToElement("result.txt"),
                ["content"] = JsonSerializer.SerializeToElement("value")
            },
            Meta = new JsonObject { ["dotcraft"] = invocation }
        });

        Assert.True(result.IsError);
        Assert.Equal(RemoteToolErrorCodes.ProtocolMismatch, result.Meta?["dotcraft"]?["code"]?.GetValue<string>());
        Assert.False(File.Exists(Path.Combine(fixture.Workspace.Path, "result.txt")));
    }

    [Fact]
    public async Task PairingAndFileTransferSupportDoNotSubstituteForSessionIsolationCapability()
    {
        await using var fixture = await RemoteExecutionFixture.CreateAsync();
        await using var raw = await fixture.Server.ConnectRawAsync();
        var listed = await Send<WorkspaceListRequest, WorkspaceListResponse>(raw, RemoteToolHostProtocol.WorkspacesList,
            new(RemoteToolHostProtocol.ProfileVersion, "owner"));
        Assert.True(listed.Success);
        var info = listed.Result! with { Capabilities = ["files-v1", RemotePluginProtocol.Capability] };
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var accepting = listener.AcceptTcpClientAsync();
        using var clientSocket = new TcpClient();
        await clientSocket.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
        using var serverSocket = await accepting;
        var serverStream = serverSocket.GetStream();
        await using var transport = new StreamServerTransport(serverStream, serverStream, "capability-probe", loggerFactory: null);
        var acquired = false;
        await using var server = McpServer.Create(transport, new McpServerOptions
        {
#pragma warning disable MCPEXP002
            RequestHandlers =
            [
                new() { Method = RemoteToolHostProtocol.WorkspacesList, Handler = (_, _) =>
                    ValueTask.FromResult<JsonNode?>(JsonSerializer.SerializeToNode(new ExtensionResponse<WorkspaceListResponse>(true, info, null), RemoteToolHostProtocol.JsonOptions)) },
                new() { Method = RemoteToolHostProtocol.WorkspacesAcquire, Handler = (_, _) =>
                    { acquired = true; return ValueTask.FromResult<JsonNode?>(null); } }
            ]
#pragma warning restore MCPEXP002
        }, loggerFactory: null, serviceProvider: null);
        var serving = server.RunAsync();
        var stream = clientSocket.GetStream();
        var connection = new RemoteToolHostConnection(new StreamClientTransport(stream, stream, loggerFactory: null), "test://capability", stream: stream);
        var error = await Assert.ThrowsAsync<RemoteToolHostException>(async () =>
            await fixture.Client.OpenSessionAsync("unsupported", fixture.Server.PeerId, "repo", connection));
        await serving.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(RemoteToolErrorCodes.ProtocolMismatch, error.Code);
        Assert.False(acquired);
    }

    private static async Task<WorkspaceAcquireResponse> Acquire(McpClient client)
    {
        var response = await Send<WorkspaceAcquireRequest, WorkspaceAcquireResponse>(client, RemoteToolHostProtocol.WorkspacesAcquire,
            new(RemoteToolHostProtocol.ProfileVersion, "shared-owner", "repo"));
        Assert.True(response.Success, response.Error?.Message);
        return response.Result!;
    }
    private static async Task<string> StartTransfer(McpClient client, string leaseId, string path)
    {
        var response = await Send<FileTransferOpen, FileTransferOpened>(client, RemoteFileTransferProtocol.Open,
            new(leaseId, "repo", path, true, Manifest: Manifest()));
        Assert.True(response.Success, response.Error?.Message);
        return response.Result!.TransferId;
    }
    private static TransferFileManifest Manifest() => new(false, [new("", false, 2, Convert.ToHexStringLower(SHA256.HashData([1, 2])))]);
    private static async Task<ExtensionResponse<T>> Send<TRequest, T>(McpClient client, string method, TRequest request) =>
        await client.SendRequestAsync<TRequest, ExtensionResponse<T>>(method, request, RemoteToolHostProtocol.JsonOptions, default, default);
}
