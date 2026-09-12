using System.Net.WebSockets;
using System.Text.Json;
using DotCraft.RemoteTools;
using DotCraft.Security;
using DotCraft.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

var input = JsonSerializer.Deserialize<ProbeInput>((await Console.In.ReadLineAsync())!)!;
var services = new ServiceCollection();
services.AddDotCraftRemoteExecution();
await using var provider = services.BuildServiceProvider();
Ensure(provider.GetService<IRemoteToolHostClientFactory>() is null, "Product routing was registered.");
var client = provider.GetRequiredService<RemoteExecutionClient>();
await using var first = await client.OpenSessionAsync("first", input.HostId, input.WorkspaceId, await Connect(input.FirstUri));
await using var second = await client.OpenSessionAsync("second", input.HostId, input.WorkspaceId, await Connect(input.SecondUri));
Ensure(first.Route.LeaseId == second.Route.LeaseId && first.Route != second.Route, "Sessions did not share one lease independently.");
Directory.CreateDirectory(input.LocalRoot);
var bytes = new byte[2 * 1024 * 1024 + 17];
Random.Shared.NextBytes(bytes);
await File.WriteAllBytesAsync(Path.Combine(input.LocalRoot, "input.bin"), bytes);
var local = new RemoteLocalWorkspace(input.LocalRoot, input.LocalRoot, new Approve());
var upload = await first.TransferAsync("thread", new("upload", "input.bin", "package-probe.bin"), local);
Ensure(upload.Success, upload.Error ?? "Upload failed.");
await first.DisposeAsync();
var download = await second.TransferAsync("thread", new("download", "output.bin", "package-probe.bin"), local);
Ensure(download.Success, download.Error ?? "Download failed.");
var downloaded = await File.ReadAllBytesAsync(Path.Combine(input.LocalRoot, "output.bin"));
Ensure(bytes.SequenceEqual(downloaded), "Transfer changed the payload.");
await second.DisposeAsync();
Ensure(!File.Exists(Path.Combine(AppContext.BaseDirectory, "dotcraft.dll")), "Consumer requires the DotCraft application.");
Console.WriteLine("remote-execution-package-ok");

async Task<RemoteToolHostConnection> Connect(string address)
{
    var socket = new ClientWebSocket();
    socket.Options.SetRequestHeader("Authorization", "Bearer " + input.Credential);
    try
    {
        await socket.ConnectAsync(new Uri(address), CancellationToken.None);
        var stream = WebSocketStream.Create(socket, WebSocketMessageType.Text, ownsWebSocket: false);
        return new(new StreamClientTransport(stream, stream, loggerFactory: null), address, socket, stream);
    }
    catch { socket.Dispose(); throw; }
}

static void Ensure(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

internal sealed record ProbeInput(string FirstUri, string SecondUri, string Credential, string HostId, string WorkspaceId, string LocalRoot);
internal sealed class Approve : IApprovalService
{
    public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null) => Task.FromResult(true);
    public Task<bool> RequestShellApprovalAsync(string command, string? workingDir, ApprovalContext? context = null) => Task.FromResult(true);
    public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null) => Task.FromResult(true);
}
