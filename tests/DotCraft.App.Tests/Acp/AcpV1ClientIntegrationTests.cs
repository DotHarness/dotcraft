using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text.Json;
using DotCraft.Acp;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Modules;
using DotCraft.Sessions;
using DotCraft.Tests.Sessions.Protocol.AppServer;
using DotCraft.Tracing;
using DotHarness.Acp.V1;
using Xunit;
using AcpClientCapabilities = DotHarness.Acp.V1.ClientCapabilities;
using AcpStdioTransport = DotHarness.Acp.Transport.StdioTransport;
using Contract = DotCraft.Protocol.AppServer;
using ModelPreference = DotCraft.Configuration.ModelPreference;
using WireStdioTransport = DotCraft.AppServer.StdioTransport;

namespace DotCraft.Tests.Acp;

public sealed class AcpV1ClientIntegrationTests
{
    [Fact]
    public async Task AcpV1Client_FullLifecycle_FsTerminalModeListDelete()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AcpV1E2E_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        var store = new ThreadStore(tempDir);
        var service = new TestableSessionService(store);

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var clientToAgent = new Pipe();
        var agentToClient = new Pipe();

        var serverTransport = WireStdioTransport.Create(
            clientToServer.Reader.AsStream(),
            serverToClient.Writer.AsStream());
        serverTransport.Start();

        var connection = new AppServerConnection();
        var wireAcp = new WireAcpExtensionProxy();
        var appServerHandler = new AppServerRequestHandler(
            service,
            connection,
            serverTransport,
            new ModuleRegistryChannelListContributor(new ModuleRegistry()),
            new AppServerConnectionServices
            {
                ServerVersion = "0.0.1-test",
                HostWorkspacePath = tempDir,
                WorkspaceCraftPath = Path.Combine(tempDir, ".agents"),
                WireAcpExtensionProxy = wireAcp,
                AppConfigMonitor = CreateConfigMonitor()
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var serverLoop = Task.Run(() => WireClientIntegrationTestsRunServerLoop.RunAsync(serverTransport, connection, appServerHandler, cts.Token));

        await using var wire = new AppServerWireClient(
            serverToClient.Reader.AsStream(),
            clientToServer.Writer.AsStream());
        wire.Start();

        var bridge = new AcpBridgeHandler(wire, tempDir);
        var agentTransport = new AcpStdioTransport(clientToAgent.Reader.AsStream(), agentToClient.Writer.AsStream());
        var bridgeTask = Task.Run(() => bridge.RunAsync(agentTransport, cts.Token));

        var fakeClientHandler = new FakeAcpClientHandler();
        var clientTransport = new AcpStdioTransport(agentToClient.Reader.AsStream(), clientToAgent.Writer.AsStream());
        await using var acpClient = new AcpV1Client(clientTransport, fakeClientHandler);

        try
        {
            var init = await acpClient.Agent.InitializeAsync(new InitializeRequest
            {
                ProtocolVersion = new(1),
                ClientCapabilities = new AcpClientCapabilities
                {
                    Fs = new FileSystemCapabilities { ReadTextFile = true, WriteTextFile = true },
                    Terminal = true
                },
                ClientInfo = new Implementation { Name = "e2e-acp-client", Version = "1.0.0" }
            }, cts.Token);

            Assert.Equal((ushort)1, init.ProtocolVersion.Value);
            Assert.True(init.AgentCapabilities.HasValue);
            Assert.True(init.AgentCapabilities.Value.LoadSession.HasValue && init.AgentCapabilities.Value.LoadSession.Value);
            Assert.True(init.AgentCapabilities.Value.SessionCapabilities.HasValue);
            Assert.NotNull(init.AgentCapabilities.Value.SessionCapabilities.Value.List.Value);
            Assert.NotNull(init.AgentCapabilities.Value.SessionCapabilities.Value.Delete.Value);

            var created = await acpClient.Agent.SessionNewAsync(new NewSessionRequest
            {
                Cwd = tempDir,
                McpServers = []
            }, cts.Token);
            var sessionId = created.SessionId.Value;
            Assert.StartsWith("thread_", sessionId);
            Assert.True(created.Modes.HasValue);
            Assert.Equal("agent", created.Modes.Value!.CurrentModeId.Value);

            await acpClient.Agent.SessionSetModeAsync(new SetSessionModeRequest
            {
                SessionId = new(sessionId),
                ModeId = new("plan")
            }, cts.Token);

            var listed = await acpClient.Agent.SessionListAsync(new ListSessionsRequest { Cwd = tempDir }, cts.Token);
            Assert.Contains(listed.Sessions, s => s.SessionId.Value == sessionId);

            var prevSessionKey = TracingChatClient.CurrentSessionKey;
            TracingChatClient.CurrentSessionKey = sessionId;
            try
            {
                var readContent = await wireAcp.ReadTextFileAsync("/workspace/Program.cs", line: 2, limit: 10, cts.Token);
                Assert.Equal($"read:{sessionId}:/workspace/Program.cs:2:10", readContent);

                var wrote = await wireAcp.WriteTextFileAsync("/workspace/Out.cs", "hello", cts.Token);
                Assert.True(wrote);
                Assert.Equal($"{sessionId}:/workspace/Out.cs:hello", fakeClientHandler.LastWrite);

                var termId = await wireAcp.CreateTerminalAsync(
                    "dotnet",
                    args: ["--version"],
                    cwd: tempDir,
                    env: new Dictionary<string, string> { ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1" },
                    outputByteLimit: 4096,
                    ct: cts.Token);
                Assert.Equal("term-1", termId);

                var output = await wireAcp.GetTerminalOutputAsync("term-1", cts.Token);
                Assert.NotNull(output);
                Assert.Equal("10.0.100", output!.Output);
                Assert.False(output.Truncated);
                Assert.Equal(0, output.ExitCode);

                var exit = await wireAcp.WaitForTerminalExitAsync("term-1", cts.Token);
                Assert.NotNull(exit);
                Assert.Equal(0, exit!.ExitCode);

                await wireAcp.KillTerminalAsync("term-1", cts.Token);
                await wireAcp.ReleaseTerminalAsync("term-1", cts.Token);
                Assert.True(fakeClientHandler.TerminalKilled);
                Assert.True(fakeClientHandler.TerminalReleased);
            }
            finally
            {
                TracingChatClient.CurrentSessionKey = prevSessionKey;
            }

            await acpClient.Agent.SessionDeleteAsync(new DeleteSessionRequest { SessionId = new(sessionId) }, cts.Token);
            var afterDelete = await acpClient.Agent.SessionListAsync(new ListSessionsRequest { Cwd = tempDir }, cts.Token);
            Assert.DoesNotContain(afterDelete.Sessions, s => s.SessionId.Value == sessionId);
        }
        finally
        {
            await cts.CancelAsync();
            await acpClient.DisposeAsync();
            clientToAgent.Writer.Complete();
            agentToClient.Writer.Complete();
            try { await bridgeTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
            await wire.DisposeAsync();
            clientToServer.Writer.Complete();
            serverToClient.Writer.Complete();
            try { await serverLoop.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private sealed class FakeAcpClientHandler : IAcpClientHandler
    {
        public ConcurrentQueue<SessionNotification> Updates { get; } = new();
        public string? LastWrite { get; private set; }
        public bool TerminalKilled { get; private set; }
        public bool TerminalReleased { get; private set; }

        public ValueTask SessionUpdateAsync(SessionNotification parameters, CancellationToken ct)
        {
            Updates.Enqueue(parameters);
            return ValueTask.CompletedTask;
        }

        public ValueTask<ReadTextFileResponse> FsReadTextFileAsync(ReadTextFileRequest parameters, CancellationToken ct)
        {
            var line = parameters.Line is { HasValue: true, Value: { } l } ? l.ToString() : "";
            var limit = parameters.Limit is { HasValue: true, Value: { } lim } ? lim.ToString() : "";
            return ValueTask.FromResult(new ReadTextFileResponse
            {
                Content = $"read:{parameters.SessionId.Value}:{parameters.Path}:{line}:{limit}"
            });
        }

        public ValueTask<WriteTextFileResponse> FsWriteTextFileAsync(WriteTextFileRequest parameters, CancellationToken ct)
        {
            LastWrite = $"{parameters.SessionId.Value}:{parameters.Path}:{parameters.Content}";
            return ValueTask.FromResult(new WriteTextFileResponse());
        }

        public ValueTask<CreateTerminalResponse> TerminalCreateAsync(CreateTerminalRequest parameters, CancellationToken ct)
        {
            Assert.Equal("dotnet", parameters.Command);
            Assert.True(parameters.Args.HasValue);
            Assert.Equal(["--version"], parameters.Args.Value);
            Assert.True(parameters.OutputByteLimit.HasValue);
            Assert.Equal((ulong)4096, parameters.OutputByteLimit.Value);
            return ValueTask.FromResult(new CreateTerminalResponse { TerminalId = new("term-1") });
        }

        public ValueTask<TerminalOutputResponse> TerminalOutputAsync(TerminalOutputRequest parameters, CancellationToken ct) =>
            ValueTask.FromResult(new TerminalOutputResponse
            {
                Output = "10.0.100",
                Truncated = false,
                ExitStatus = new TerminalExitStatus { ExitCode = 0u }
            });

        public ValueTask<WaitForTerminalExitResponse> TerminalWaitForExitAsync(WaitForTerminalExitRequest parameters, CancellationToken ct) =>
            ValueTask.FromResult(new WaitForTerminalExitResponse { ExitCode = 0u });

        public ValueTask<KillTerminalResponse> TerminalKillAsync(KillTerminalRequest parameters, CancellationToken ct)
        {
            TerminalKilled = true;
            return ValueTask.FromResult(new KillTerminalResponse());
        }

        public ValueTask<ReleaseTerminalResponse> TerminalReleaseAsync(ReleaseTerminalRequest parameters, CancellationToken ct)
        {
            TerminalReleased = true;
            return ValueTask.FromResult(new ReleaseTerminalResponse());
        }
    }

    private static AppConfigMonitor CreateConfigMonitor() =>
        new(new AppConfig
        {
            ProviderId = "openai",
            ProviderPreferences = new() { ["openai"] = new ModelPreference { Model = "gpt-4o-mini" } },
            Providers =
            {
                ["openai"] = new AppConfig.ModelProviderConfig
                {
                    DisplayName = "OpenAI",
                    Protocol = ModelProviderProtocols.OpenAI,
                    ApiKey = "sk-test-not-used-for-network",
                    EndPoint = "https://127.0.0.1:9/v1"
                }
            }
        });
}
