using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.AppBinding;
using DotCraft.AppServer;
using DotCraft.Channels;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.ExternalChannel;
using DotCraft.Processes;
using DotCraft.Modules;
using DotCraft.Security;
using DotCraft.Security.ShellCommands;
using DotCraft.Sessions;
using DotCraft.Tools;
using DotCraft.Workspaces;
using Contract = DotCraft.Protocol.AppServer;
using Microsoft.Extensions.AI;
using DotCraft.Sessions.Wire;
using ContextUsageSnapshot = DotCraft.Sessions.Wire.ContextUsageSnapshot;
using QueuedTurnInput = DotCraft.Sessions.QueuedTurnInput;
using SenderContext = DotCraft.Sessions.SenderContext;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;
using SessionThread = DotCraft.Sessions.SessionThread;
using ThreadConfiguration = DotCraft.Sessions.ThreadConfiguration;
using ThreadSource = DotCraft.Sessions.ThreadSource;
using ThreadSpawnEdge = DotCraft.Sessions.ThreadSpawnEdge;
using ThreadSummary = DotCraft.Sessions.ThreadSummary;
using Xunit;

namespace DotCraft.Tests.AppServer;

public sealed class ExternalChannelDeliveryTests : IDisposable
{
    private static readonly ModelProviderRegistry EmptyModelProviders = new([]);
    private static readonly ChatClientRegistry EmptyChatClients = new(EmptyModelProviders);
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "ExternalChannelDeliveryTests_" + Guid.NewGuid().ToString("N")[..8]);
    private DotCraftPaths WorkspacePaths =>
        DotCraftPaths.CreateForExecutionHost(_tempDir, Path.Combine(_tempDir, ".craft"));

    public ExternalChannelDeliveryTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch
        {
        }
    }

    [Fact]
    public async Task ExternalChannelHost_UsesResolvedWorkspaceTempPathForMedia()
    {
        var paths = DotCraftPaths.CreateForExecutionHost(_tempDir, Path.Combine(_tempDir, ".agents"));
        var host = new ExternalChannelHost(
            new ExternalChannelEntry
            {
                Name = "telegram",
                Enabled = true,
                Transport = ExternalChannelTransport.Websocket
            },
            new FakeSessionService(),
            "0.0.1-test",
            new ModuleRegistry(),
            paths,
            EmptyChatClients,
            EmptyModelProviders);
        var transport = new StubTransport(new ChannelDeliveryResult { Delivered = true });
        AttachFakeAdapter(host, transport, CreateAdapterConnection(
            fileConstraints: new ChannelMediaConstraintSnapshot { SupportsHostPath = true }));

        var result = await host.DeliverAsync("group:1", new ChannelDeliveryMessage
        {
            Kind = "file",
            FileName = "report.txt",
            Source = new ChannelDeliveryMediaSource
            {
                Kind = "dataBase64",
                DataBase64 = Convert.ToBase64String("hello"u8.ToArray())
            }
        });

        Assert.True(result.Delivered);
        var sent = JsonSerializer.SerializeToElement(transport.LastParams, SessionWireJsonOptions.Default);
        var temporaryFile = sent.GetProperty("message").GetProperty("source").GetProperty("hostPath").GetString();
        Assert.NotNull(temporaryFile);
        Assert.Equal(paths.WorkspaceTempPath, Path.GetDirectoryName(temporaryFile));
        Assert.False(File.Exists(temporaryFile));
        Assert.False(Directory.Exists(Path.Combine(_tempDir, ".craft", "external-channel-media")));
    }

    [Fact]
    public async Task ChannelMediaResolver_RejectsMalformedSources()
    {
        var resolver = CreateResolver(new InMemoryChannelMediaArtifactStore(), _tempDir);

        var text = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(new ChannelDeliveryMessage
        {
            Kind = "text",
            Text = "hello",
            Source = new ChannelDeliveryMediaSource { Kind = "hostPath", HostPath = "x.txt" }
        }));
        var ambiguous = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(new ChannelDeliveryMessage
        {
            Kind = "file",
            Source = new ChannelDeliveryMediaSource { Kind = "hostPath", HostPath = "a.txt", Url = "https://example.com/a.txt" }
        }));

        Assert.Contains("Text delivery", text.Message);
        Assert.Contains("exactly one", ambiguous.Message);
    }

    [Fact]
    public async Task ChannelMediaResolver_RegistersHostPathArtifact_AndArtifactIdCanResolve()
    {
        var path = Path.Combine(_tempDir, "report.txt");
        await File.WriteAllTextAsync(path, "report");

        var store = new InMemoryChannelMediaArtifactStore();
        var resolver = CreateResolver(store, _tempDir);

        var first = await resolver.ResolveAsync(new ChannelDeliveryMessage
        {
            Kind = "file",
            Source = new ChannelDeliveryMediaSource
            {
                Kind = "hostPath",
                HostPath = path
            }
        });

        var second = await resolver.ResolveAsync(new ChannelDeliveryMessage
        {
            Kind = "file",
            Source = new ChannelDeliveryMediaSource
            {
                Kind = "artifactId",
                ArtifactId = first.Artifact.Id
            }
        });

        Assert.Equal(first.Artifact.Id, second.Artifact.Id);
        Assert.Equal(path, second.Artifact.ResolvedPath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChannelMediaResolver_HostPathOutsideWorkspace_RequiresApproval(bool approve)
    {
        var outsideDir = Path.Combine(Path.GetTempPath(), "ExternalChannelOutside_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(outsideDir);
        try
        {
            var outsidePath = Path.Combine(outsideDir, "secret.txt");
            await File.WriteAllTextAsync(outsidePath, "secret");
            var approvalService = new RecordingApprovalService(approve);
            var resolver = CreateResolver(new InMemoryChannelMediaArtifactStore(), _tempDir, approvalService: approvalService);
            var message = new ChannelDeliveryMessage
            {
                Kind = "file",
                Source = new ChannelDeliveryMediaSource { Kind = "hostPath", HostPath = outsidePath }
            };

            if (approve)
            {
                Assert.Equal(Path.GetFullPath(outsidePath), (await resolver.ResolveAsync(message)).Artifact.ResolvedPath);
            }
            else
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(message));
                Assert.Contains("rejected by user", ex.Message);
            }
            Assert.Equal("read-for-delivery", approvalService.LastOperation);
            Assert.Equal(Path.GetFullPath(outsidePath), approvalService.LastPath);
        }
        finally
        {
            try { Directory.Delete(outsideDir, recursive: true); }
            catch { }
        }
    }

    [Fact]
    public async Task ChannelMediaResolver_RejectsBlacklistedHostPath()
    {
        var blockedPath = Path.Combine(_tempDir, "blocked.txt");
        await File.WriteAllTextAsync(blockedPath, "blocked");

        var store = new InMemoryChannelMediaArtifactStore();
        var resolver = CreateResolver(store, _tempDir, blacklist: new PathBlacklist([blockedPath]));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(new ChannelDeliveryMessage
        {
            Kind = "file",
            Source = new ChannelDeliveryMediaSource
            {
                Kind = "hostPath",
                HostPath = blockedPath
            }
        }));

        Assert.Contains("blacklist", ex.Message);
    }

    [Fact]
    public async Task ChannelMediaResolver_CleansUpTemporaryBase64Artifact_WhenRegisterFails()
    {
        var mediaRoot = Path.Combine(_tempDir, "register-failure-media");
        var store = new ThrowingRegisterArtifactStore();
        var resolver = CreateResolver(store, mediaRoot);

        await Assert.ThrowsAsync<IOException>(() => resolver.ResolveAsync(new ChannelDeliveryMessage
        {
            Kind = "file",
            FileName = "report.txt",
            Source = new ChannelDeliveryMediaSource
            {
                Kind = "dataBase64",
                DataBase64 = Convert.ToBase64String("hello"u8.ToArray())
            }
        }));

        var tmpDir = Path.Combine(mediaRoot, "tmp");
        Assert.False(Directory.Exists(tmpDir) && Directory.EnumerateFiles(tmpDir).Any());
    }

    [Theory]
    [InlineData("file", false, null, "UnsupportedMediaSource")]
    [InlineData("file", true, 1024L, "MediaResolutionFailed")]
    [InlineData("audio", true, null, "UnsupportedDeliveryKind")]
    public async Task ExternalChannelMessageDispatcher_RejectsUndeliverableUrlMedia_BeforeDispatch(
        string kind,
        bool supportsUrl,
        long? maxBytes,
        string errorCode)
    {
        var store = new InMemoryChannelMediaArtifactStore();
        var dispatcher = new ExternalChannelMessageDispatcher(CreateResolver(store, _tempDir), store);
        var transport = new StubTransport();
        var connection = CreateAdapterConnection(fileConstraints: kind == "file"
            ? new ChannelMediaConstraintSnapshot { SupportsUrl = supportsUrl, SupportsBase64 = true, MaxBytes = maxBytes }
            : null);

        var result = await dispatcher.DeliverAsync(
            transport,
            connection,
            "telegram",
            "group:1",
            new ChannelDeliveryMessage
            {
                Kind = kind,
                Source = new ChannelDeliveryMediaSource { Kind = "url", Url = "https://example.com/media.bin" }
            },
            metadata: null,
            deliveryId: "d1");

        Assert.False(result.Delivered);
        Assert.Equal(errorCode, result.ErrorCode);
        Assert.Null(transport.LastMethod);
    }

    [Fact]
    public async Task ExternalChannelMessageDispatcher_TextDelivery_SendsWithDeliveryId()
    {
        var transport = new StubTransport(new ChannelDeliveryResult { Delivered = true });
        var store = new InMemoryChannelMediaArtifactStore();
        var resolver = CreateResolver(store, _tempDir);
        var dispatcher = new ExternalChannelMessageDispatcher(resolver, store);
        var connection = CreateAdapterConnection(fileConstraints: new ChannelMediaConstraintSnapshot
        {
            SupportsBase64 = true
        });

        var result = await dispatcher.DeliverAsync(
            transport,
            connection,
            "telegram",
            "group:1",
            new ChannelDeliveryMessage
            {
                Kind = "text",
                Text = "hello"
            },
            metadata: null,
            deliveryId: "d1");

        Assert.True(result.Delivered);
        Assert.Equal(DotCraft.Protocol.AppServer.AppServerMethodNames.ExtChannelSend, transport.LastMethod);
        var sent = JsonSerializer.SerializeToElement(transport.LastParams, SessionWireJsonOptions.Default);
        Assert.Equal("d1", sent.GetProperty("deliveryId").GetString());
    }

    [Fact]
    public async Task ExternalChannelHost_Disconnect_UnbindsRuntimeAdditionalContext()
    {
        var runtimeContextProvider = new WireRuntimeAdditionalContextProvider();
        var service = new FakeSessionService();
        var host = new ExternalChannelHost(
            new ExternalChannelEntry
            {
                Name = "test-channel",
                Enabled = true,
                Transport = ExternalChannelTransport.Websocket,
            },
            service,
            "0.0.1-test",
            new ModuleRegistry(),
            WorkspacePaths,
            EmptyChatClients,
            EmptyModelProviders,
            wireRuntimeAdditionalContextProvider: runtimeContextProvider);
        var transport = new StubTransport();
        var connection = new AppServerConnection();
        const string threadId = "thread_context_cleanup";
        runtimeContextProvider.BindThread(
            threadId,
            transport,
            connection,
            new Dictionary<string, RuntimeAdditionalContextValue>
            {
                ["test.runtime"] = new()
                {
                    Kind = RuntimeAdditionalContextKinds.Application,
                    Value = "connection-owned runtime context"
                }
            });

        var factory = Assert.IsType<ExternalChannelRequestHandlerFactory>(typeof(ExternalChannelHost)
            .GetField("_requestHandlerFactory", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(host));
        var handler = factory.Create(connection, transport);
        var runLoop = typeof(ExternalChannelHost)
            .GetMethod("RunMessageLoopAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await Assert.IsAssignableFrom<Task>(
            runLoop.Invoke(host, [transport, connection, handler, CancellationToken.None]));

        Assert.True(connection.IsClosed);
        Assert.Null(runtimeContextProvider.GetSystemPromptSection(
            new ThreadSystemPromptContext(threadId, _tempDir, "test-channel")));
    }

    [Fact]
    public async Task ExternalChannelHost_WhenDisconnected_FailsDeliveryAndReportsUnknownConfigAvailability()
    {
        var host = CreateHost("telegram");

        Assert.Null(await host.GetConfigOptionAvailabilityAsync("feishu.cli.userScopes", CancellationToken.None));
        var result = await host.DeliverAsync(
            "group:1",
            new ChannelDeliveryMessage
            {
                Kind = "text",
                Text = "hello"
            });

        Assert.False(result.Delivered);
        Assert.Equal("AdapterDeliveryFailed", result.ErrorCode);
    }

    [Fact]
    public async Task ExternalChannelHost_SpawnAdapterProcess_UsesManagedFactory()
    {
        ProcessStartInfo? capturedStartInfo = null;
        ManagedChildProcess? spawnedProcess = null;
        var host = new ExternalChannelHost(
            new ExternalChannelEntry
            {
                Name = "telegram",
                Enabled = true,
                Transport = ExternalChannelTransport.Subprocess,
                Command = "python",
                Args = ["-m", "sample_adapter"],
                WorkingDirectory = _tempDir,
                Env = new Dictionary<string, string> { ["DOTCRAFT_TEST"] = "1" }
            },
            new FakeSessionService(),
            "0.0.1-test",
            new ModuleRegistry(),
            WorkspacePaths,
            EmptyChatClients,
            EmptyModelProviders,
            deliveryDependenciesFactory: null,
            managedChildProcessFactory: startInfo =>
            {
                capturedStartInfo = startInfo;
                spawnedProcess = ManagedChildProcess.Start(CreateLongRunningStartInfo());
                return spawnedProcess;
            });

        try
        {
            var method = typeof(ExternalChannelHost)
                .GetMethod("SpawnAdapterProcess", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var managed = Assert.IsType<ManagedChildProcess>(method.Invoke(host, null));

            Assert.Same(spawnedProcess, managed);
            Assert.NotNull(capturedStartInfo);
            Assert.Equal("python", capturedStartInfo!.FileName);
            Assert.Equal(_tempDir, capturedStartInfo.WorkingDirectory);
            Assert.Contains("-m", capturedStartInfo.ArgumentList);
            Assert.Equal("1", capturedStartInfo.Environment["DOTCRAFT_TEST"]);
        }
        finally
        {
            if (spawnedProcess is not null)
                await spawnedProcess.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExternalChannelHost_SpawnManagedWebsocketProcess_InjectsRuntimeEndpoint()
    {
        ProcessStartInfo? capturedStartInfo = null;
        ManagedChildProcess? spawnedProcess = null;
        var appConfig = new AppConfig();
        appConfig.SetSection("AppServer", new AppServerConfig
        {
            Mode = AppServerMode.StdioAndWebSocket,
            WebSocket = new WebSocketServerConfig
            {
                Host = "0.0.0.0",
                Port = 9133,
                Token = "fixture-channel-token"
            }
        });

        var host = new ExternalChannelHost(
            new ExternalChannelEntry
            {
                Name = "telegram",
                Enabled = true,
                Transport = ExternalChannelTransport.ManagedWebsocket,
                Command = "python",
                Env = new Dictionary<string, string>
                {
                    ["DOTCRAFT_CHANNEL_TRANSPORT"] = "stdio",
                    ["DOTCRAFT_CHANNEL_WS_URL"] = "ws://stale/ws",
                    ["DOTCRAFT_CHANNEL_WS_TOKEN"] = "stale-token"
                }
            },
            new FakeSessionService(),
            "0.0.1-test",
            new ModuleRegistry(),
            WorkspacePaths,
            EmptyChatClients,
            EmptyModelProviders,
            deliveryDependenciesFactory: null,
            managedChildProcessFactory: startInfo =>
            {
                capturedStartInfo = startInfo;
                spawnedProcess = ManagedChildProcess.Start(CreateLongRunningStartInfo());
                return spawnedProcess;
            },
            appConfigMonitor: new AppConfigMonitor(appConfig));

        try
        {
            var method = typeof(ExternalChannelHost)
                .GetMethod("SpawnAdapterProcess", BindingFlags.Instance | BindingFlags.NonPublic)!;
            _ = Assert.IsType<ManagedChildProcess>(method.Invoke(host, null));

            Assert.NotNull(capturedStartInfo);
            Assert.Equal("websocket", capturedStartInfo!.Environment["DOTCRAFT_CHANNEL_TRANSPORT"]);
            Assert.Equal("ws://127.0.0.1:9133/ws", capturedStartInfo.Environment["DOTCRAFT_CHANNEL_WS_URL"]);
            Assert.Equal("fixture-channel-token", capturedStartInfo.Environment["DOTCRAFT_CHANNEL_WS_TOKEN"]);
            Assert.True(capturedStartInfo.RedirectStandardOutput);
            Assert.True(capturedStartInfo.RedirectStandardError);
        }
        finally
        {
            if (spawnedProcess is not null)
                await spawnedProcess.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExternalChannelHost_StopAsync_DisposesManagedAdapterProcess()
    {
        var host = CreateHost("telegram");
        await using var managedProcess = ManagedChildProcess.Start(CreateLongRunningStartInfo());
        var processId = managedProcess.Process.Id;

        typeof(ExternalChannelHost)
            .GetField("_adapterProcess", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(host, managedProcess);

        await host.StopAsync();

        Assert.ThrowsAny<ArgumentException>(() => Process.GetProcessById(processId));
        Assert.Null(typeof(ExternalChannelHost)
            .GetField("_adapterProcess", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(host));
    }

    [Fact]
    public async Task ExternalChannelHost_RuntimeState_TransitionsFromStartingToRunningToStopped()
    {
        var host = CreateHost("telegram");
        Assert.Equal(ChannelRuntimeStates.Starting, host.RuntimeState);
        Assert.Null(host.FailureCode);

        AttachFakeAdapter(host, new StubTransport(), CreateToolAdapterConnection("telegram", []));
        Assert.Equal(ChannelRuntimeStates.Running, host.RuntimeState);
        Assert.True(host.IsAdapterConnected);

        await host.StopAsync();
        Assert.Equal(ChannelRuntimeStates.Stopped, host.RuntimeState);
        Assert.Null(host.FailureCode);
    }

    [Fact]
    public async Task ExternalChannelHost_FiveConsecutiveStartFailures_BecomePermanentFailure()
    {
        var attempts = 0;
        var host = new ExternalChannelHost(
            new ExternalChannelEntry
            {
                Name = "feishu",
                Enabled = true,
                Transport = ExternalChannelTransport.Subprocess,
                Command = "unused"
            },
            new FakeSessionService(),
            "0.0.1-test",
            new ModuleRegistry(),
            WorkspacePaths,
            EmptyChatClients,
            EmptyModelProviders,
            deliveryDependenciesFactory: null,
            managedChildProcessFactory: _ =>
            {
                Interlocked.Increment(ref attempts);
                return ManagedChildProcess.Start(CreateImmediateExitStartInfo(exitCode: 17));
            },
            initialBackoff: TimeSpan.Zero,
            maxBackoff: TimeSpan.Zero,
            maxConsecutiveFailures: 5);

        await host.StartAsync(CancellationToken.None);

        Assert.Equal(5, attempts);
        Assert.Equal(ChannelRuntimeStates.Failed, host.RuntimeState);
        Assert.Equal(ChannelFailureCodes.ExternalChannelStartFailed, host.FailureCode);

        await host.StopAsync();
        Assert.Equal(ChannelRuntimeStates.Stopped, host.RuntimeState);
        Assert.Null(host.FailureCode);
    }

    [Fact]
    public async Task ExternalChannelHost_RunSubprocessCycleAsync_ReportsExitBeforeDisposingProcess()
    {
        var host = new ExternalChannelHost(
            new ExternalChannelEntry
            {
                Name = "telegram",
                Enabled = true,
                Transport = ExternalChannelTransport.Subprocess,
                Command = "python"
            },
            new FakeSessionService(),
            "0.0.1-test",
            new ModuleRegistry(),
            WorkspacePaths,
            EmptyChatClients,
            EmptyModelProviders,
            deliveryDependenciesFactory: null,
            managedChildProcessFactory: _ => ManagedChildProcess.Start(CreateImmediateExitStartInfo()));

        var method = typeof(ExternalChannelHost)
            .GetMethod("RunSubprocessCycleAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = Assert.IsAssignableFrom<Task>(
            method.Invoke(host, [CancellationToken.None]));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Contains("exit code 0", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExternalChannelToolSource_DispatchesWithQualifiedIdentityAndOriginalCallId()
    {
        var registry = new ExternalChannelRegistry();
        var host = CreateHost("telegram");
        var transport = new StubTransport(new ChannelToolInvocationResult
        {
            Success = true,
            ContentItems = [new ChannelToolInvocationContentItem { Type = "text", Text = "Document sent." }]
        });
        AttachFakeAdapter(host, transport, CreateToolAdapterConnection(
            "telegram",
            [
                new ChannelToolSpec
                {
                    Name = "TelegramExportChatHistory",
                    Description = "Export the current Telegram chat history to a file.",
                    RequiresChatContext = true,
                    InputSchema = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["fileName"] = new JsonObject { ["type"] = "string" }
                        },
                        ["required"] = new JsonArray("fileName")
                    }
                }
            ]));
        registry.Register("telegram", host);
        var thread = new SessionThread
        {
            Id = "thread_m1",
            WorkspacePath = _tempDir,
            OriginChannel = "telegram",
            ChannelContext = "chat_123",
            UserId = "user_42",
            Status = ThreadStatus.Active
        };
        var provider = new ExternalChannelToolProvider(registry);
        var source = Assert.Single(provider.CreateToolSourcesForThread(thread));
        var planning = new ToolPlanningContext(
            thread.Id,
            "turn_m1",
            _tempDir,
            Path.Combine(_tempDir, ".craft"),
            "default",
            null,
            [],
            1);
        var snapshot = await new EffectiveToolSnapshotBuilder().BuildAsync([source], planning);
        var definition = Assert.Single(snapshot.ModelVisibleDefinitions);
        Assert.Equal(new ToolName("external_channel", "TelegramExportChatHistory"), definition.Name);
        Assert.Equal("external-channel:telegram", definition.Id.SourceId);

        var providerName = snapshot.ProviderFlatNames[definition.Name];
        var result = await new ToolDispatcher().DispatchProviderFlatCallAsync(
            snapshot,
            providerName,
            new JsonObject { ["fileName"] = "report.pdf" },
            new ToolInvocationRequest(
                thread.Id,
                "turn_m1",
                "provider-call-42",
                ToolInvocationAudience.Model));

        Assert.True(result.Success);
        Assert.Equal("Document sent.", result.Content);
        var toolParams = Assert.IsType<Contract.ExtChannelToolCallParams>(transport.LastParams);
        Assert.Equal("provider-call-42", toolParams.CallId.Value);
        var toolContext = Assert.IsType<Contract.ExtChannelToolCallContext>(toolParams.Context.Value);
        Assert.Equal("chat_123", toolContext.ChannelContext.Value);
        Assert.Equal("user_42", toolContext.SenderId.Value);
    }

    [Fact]
    public async Task ExternalChannelToolSource_ReconnectDoesNotRetargetFrozenSnapshot()
    {
        var sessionService = new FakeSessionService();
        var registry = new ExternalChannelRegistry();
        var host = CreateHost("feishu", sessionService);
        registry.Register("feishu", host);
        var descriptor = new ChannelToolSpec
        {
            Name = "ExternalReadResource",
            Description = "Read an external resource.",
            InputSchema = new JsonObject { ["type"] = "object" }
        };
        var firstTransport = new StubTransport(new ChannelToolInvocationResult
        {
            Success = true,
            ContentItems = [new ChannelToolInvocationContentItem { Type = "text", Text = "first" }]
        });
        AttachFakeAdapter(host, firstTransport, CreateToolAdapterConnection("feishu", [descriptor]));

        var thread = new SessionThread
        {
            Id = "thread_reconnect",
            WorkspacePath = _tempDir,
            OriginChannel = "feishu",
            ChannelContext = "chat_1",
            Status = ThreadStatus.Active
        };
        var provider = new ExternalChannelToolProvider(registry);
        var firstSource = Assert.Single(provider.CreateToolSourcesForThread(thread));
        var firstSnapshot = await new EffectiveToolSnapshotBuilder().BuildAsync(
            [firstSource],
            new ToolPlanningContext(thread.Id, "turn_1", _tempDir, Path.Combine(_tempDir, ".craft"), "default", null, [], 1));
        var toolName = Assert.Single(firstSnapshot.ModelVisibleDefinitions).Name;

        var secondTransport = new StubTransport(new ChannelToolInvocationResult
        {
            Success = true,
            ContentItems = [new ChannelToolInvocationContentItem { Type = "text", Text = "second" }]
        });
        AttachFakeAdapter(host, secondTransport, CreateToolAdapterConnection("feishu", [descriptor]));

        var staleResult = await new ToolDispatcher().DispatchAsync(
            firstSnapshot,
            toolName,
            [],
            new ToolInvocationRequest(
                thread.Id,
                "turn_1",
                "call_stale",
                ToolInvocationAudience.Model));
        Assert.False(staleResult.Success);
        Assert.Null(firstTransport.LastMethod);
        Assert.Null(secondTransport.LastMethod);

        var secondSource = Assert.Single(provider.CreateToolSourcesForThread(thread));
        var secondSnapshot = await new EffectiveToolSnapshotBuilder().BuildAsync(
            [secondSource],
            new ToolPlanningContext(thread.Id, "turn_2", _tempDir, Path.Combine(_tempDir, ".craft"), "default", null, [], 2));
        var currentResult = await new ToolDispatcher().DispatchAsync(
            secondSnapshot,
            toolName,
            [],
            new ToolInvocationRequest(
                thread.Id,
                "turn_2",
                "call_current",
                ToolInvocationAudience.Model));

        Assert.True(currentResult.Success);
        Assert.Equal("second", currentResult.Content);
        Assert.Equal(Contract.AppServerRpc.ExtChannelToolCall.Name, secondTransport.LastMethod);
        Assert.Equal(2, sessionService.AgentInvalidationCount);
    }

    [Fact]
    public async Task ExternalChannelToolSource_DisconnectKeepsTheDeclaredToolsAndFailsDispatch()
    {
        var registry = new ExternalChannelRegistry();
        var host = CreateHost("feishu");
        registry.Register("feishu", host);
        var transport = new StubTransport(new ChannelToolInvocationResult { Success = true });
        var connection = CreateToolAdapterConnection("feishu", [ReadResourceTool()]);
        AttachFakeAdapter(host, transport, connection);
        var thread = new SessionThread
        {
            Id = "thread_disconnect",
            WorkspacePath = _tempDir,
            OriginChannel = "feishu",
            ChannelContext = "chat_1",
            Status = ThreadStatus.Active
        };
        var provider = new ExternalChannelToolProvider(registry);
        var connected = await SnapshotAsync(Assert.Single(provider.CreateToolSourcesForThread(thread)), thread.Id);

        DetachFakeAdapter(host, connection);
        var disconnected = await SnapshotAsync(Assert.Single(provider.CreateToolSourcesForThread(thread)), thread.Id);
        var definition = Assert.Single(disconnected.ModelVisibleDefinitions);
        var result = await new ToolDispatcher().DispatchAsync(
            disconnected,
            definition.Name,
            [],
            new ToolInvocationRequest(thread.Id, "turn_1", "call_offline", ToolInvocationAudience.Model));

        Assert.Equal(Assert.Single(connected.ModelVisibleDefinitions).Name, definition.Name);
        Assert.False(result.Success);
        Assert.Null(transport.LastMethod);
    }

    [Fact]
    public async Task ManagedChannelToolSource_KeepsToolsWhileTheBindingIsOfflineAndDropsThemOnRevoke()
    {
        var dataPath = Path.Combine(_tempDir, ".craft");
        var bindings = new AppBindingService();
        var request = bindings.CreateChannelRequest(dataPath, "desktop-thread", "feishu", "local");
        var binding = bindings.AcceptChannel(dataPath, "feishu", new ChannelBindingAcceptCommand
        {
            Code = request.Code,
            Target = new ChannelTarget
            {
                ChannelName = "feishu",
                ConversationKind = "group",
                ConversationId = "oc_1",
                DeliveryTarget = "group:oc_1",
                DisplayName = "Release team"
            }
        });
        var registry = new ExternalChannelRegistry();
        var host = CreateHost("feishu");
        registry.Register("feishu", host);
        var transport = new StubTransport(new ChannelToolInvocationResult { Success = true });
        AttachFakeAdapter(host, transport, CreateToolAdapterConnection("feishu", [ReadResourceTool()]));
        var source = new ManagedChannelToolSource(bindings, registry, new ChannelToolRegistrationService());
        var active = await SnapshotAsync(source, "desktop-thread");

        bindings.MarkUnavailable(dataPath, binding.BindingId, "adapterDisconnected");
        var offline = await SnapshotAsync(source, "desktop-thread");
        var definition = Assert.Single(offline.ModelVisibleDefinitions);
        var result = await new ToolDispatcher().DispatchAsync(
            offline,
            definition.Name,
            [],
            new ToolInvocationRequest("desktop-thread", "turn_1", "call_offline", ToolInvocationAudience.Model));
        bindings.RevokeBinding(dataPath, "desktop-thread", binding.BindingId, "local");
        var revoked = await SnapshotAsync(source, "desktop-thread");

        var shown = Assert.Single(active.ModelVisibleDefinitions);
        Assert.Equal(shown.Name, definition.Name);
        Assert.Equal(shown.InputSchema.GetRawText(), definition.InputSchema.GetRawText());
        Assert.False(result.Success);
        Assert.Null(transport.LastMethod);
        Assert.Empty(revoked.ModelVisibleDefinitions);
    }

    [Fact]
    public void ExternalChannelToolSource_WhenPluginDisabled_ReturnsNoSources()
    {
        var registry = new ExternalChannelRegistry();
        var host = CreateHost("telegram");
        AttachFakeAdapter(host, new StubTransport(), CreateToolAdapterConnection(
            "telegram",
            [
                new ChannelToolSpec
                {
                    Name = "TelegramExportChatHistory",
                    Description = "Export the chat history.",
                    InputSchema = new JsonObject { ["type"] = "object" }
                }
            ]));
        registry.Register("telegram", host);
        var config = new AppConfig();
        config.Plugins.DisabledPlugins.Add("external-channel");
        var provider = new ExternalChannelToolProvider(registry, config);

        var sources = provider.CreateToolSourcesForThread(new SessionThread
        {
            Id = "thread_disabled",
            WorkspacePath = _tempDir,
            OriginChannel = "telegram",
            Status = ThreadStatus.Active
        });

        Assert.Empty(sources);
    }

    [Theory]
    [InlineData(ExternalChannelTransport.Subprocess, "python", null, null, true, false)]
    [InlineData(ExternalChannelTransport.Subprocess, null, null, null, false, false)]
    [InlineData(ExternalChannelTransport.Websocket, null, null, AppServerMode.WebSocket, true, true)]
    [InlineData(ExternalChannelTransport.ManagedWebsocket, null, "channel-feishu", AppServerMode.StdioAndWebSocket, true, true)]
    [InlineData(ExternalChannelTransport.ManagedWebsocket, null, "channel-feishu", AppServerMode.Stdio, false, false)]
    public void ExternalChannelManager_RegistersOnlyLaunchableHosts(
        ExternalChannelTransport transport,
        string? command,
        string? builtinModule,
        AppServerMode? appServerMode,
        bool registered,
        bool acceptsWebSocketAttach)
    {
        var registry = new ExternalChannelRegistry();
        var config = new AppConfig();
        if (appServerMode is { } mode)
            config.SetSection("AppServer", new AppServerConfig { Mode = mode });
        config.ExternalChannels =
        [
            new ExternalChannelEntry
            {
                Name = "channel",
                Enabled = true,
                Transport = transport,
                Command = command,
                BuiltinModule = builtinModule
            }
        ];

        var manager = new ExternalChannelManager(
            config,
            new FakeSessionService(),
            [],
            new ModuleRegistry(),
            WorkspacePaths,
            EmptyChatClients,
            EmptyModelProviders,
            registry: registry,
            appConfigMonitor: new AppConfigMonitor(config));

        Assert.Equal(registered ? 1 : 0, manager.Channels.Count);
        Assert.Equal(registered, registry.TryGet("channel", out var host));
        if (registered)
        {
            Assert.Equal(transport, host!.Transport);
            Assert.Equal(acceptsWebSocketAttach, host.AcceptsWebSocketAdapterAttach);
        }
    }

    private static AppServerConnection CreateAdapterConnection(
        ChannelMediaConstraintSnapshot? fileConstraints)
    {
        var connection = new AppServerConnection();
        connection.TryMarkInitialized(
            new ClientConnectionInfo { Name = "adapter", Version = "1.0.0" },
            new ClientConnectionCapabilities
            {
                ChannelAdapter = new ChannelAdapterRuntimeCapability
                {
                    ChannelName = "telegram",
                    DisplayName = "Telegram",
                    DeliveryCapabilities = new ChannelDeliveryCapabilitySnapshot
                    {
                        Media = new ChannelMediaCapabilitySnapshot
                        {
                            File = fileConstraints
                        }
                    }
                }
            });
        connection.MarkClientReady();
        return connection;
    }

    private static AppServerConnection CreateToolAdapterConnection(
        string channelName,
        IReadOnlyList<ChannelToolSpec> tools)
    {
        var connection = new AppServerConnection();
        connection.TryMarkInitialized(
            new ClientConnectionInfo { Name = $"{channelName}-adapter", Version = "1.0.0" },
            new ClientConnectionCapabilities
            {
                ChannelAdapter = new ChannelAdapterRuntimeCapability
                {
                    ChannelName = channelName,
                    ChannelTools = tools.ToList()
                }
            });
        connection.MarkClientReady();
        return connection;
    }

    private static ChannelMediaResolver CreateResolver(
        IChannelMediaArtifactStore store,
        string workspaceRoot,
        IApprovalService? approvalService = null,
        PathBlacklist? blacklist = null)
        => new(
            store,
            Path.Combine(workspaceRoot, "tmp"),
            new FileAccessGuard(
                workspaceRoot,
                requireApprovalOutsideWorkspace: true,
                approvalService,
                blacklist));

    private ExternalChannelHost CreateHost(
        string channelName,
        FakeSessionService? sessionService = null)
        => new(
            new ExternalChannelEntry
            {
                Name = channelName,
                Enabled = true,
                Transport = ExternalChannelTransport.Subprocess,
                Command = "python"
            },
            sessionService ?? new FakeSessionService(),
            "0.0.1-test",
            new ModuleRegistry(),
            WorkspacePaths,
            EmptyChatClients,
            EmptyModelProviders);

    private static ProcessStartInfo CreateLongRunningStartInfo() =>
        Shell(OperatingSystem.IsWindows() ? "pause" : "read line");

    private static ProcessStartInfo CreateImmediateExitStartInfo(int exitCode = 0) => Shell($"exit {exitCode}");

    private static ProcessStartInfo Shell(string command)
    {
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", command } }
            : new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", command } };
        start.UseShellExecute = false;
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.CreateNoWindow = true;
        return start;
    }

    private static ChannelToolSpec ReadResourceTool() => new()
    {
        Name = "ExternalReadResource",
        Description = "Read an external resource.",
        InputSchema = new JsonObject { ["type"] = "object" }
    };

    private ValueTask<EffectiveToolSnapshot> SnapshotAsync(IToolSource source, string threadId) =>
        new EffectiveToolSnapshotBuilder().BuildAsync(
            [source],
            new ToolPlanningContext(threadId, "turn_1", _tempDir, Path.Combine(_tempDir, ".craft"), "default", null, [], 1));

    private static void DetachFakeAdapter(ExternalChannelHost host, AppServerConnection connection) =>
        typeof(ExternalChannelHost)
            .GetMethod("ClearCurrentSession", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, [connection]);

    private static void AttachFakeAdapter(
        ExternalChannelHost host,
        StubTransport transport,
        AppServerConnection connection)
    {
        typeof(ExternalChannelHost)
            .GetField("_transport", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(host, transport);
        typeof(ExternalChannelHost)
            .GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(host, connection);
        typeof(ExternalChannelHost)
            .GetMethod("PublishToolBinding", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, [transport, connection]);
    }

    private sealed class StubTransport(object? result = null, Exception? exception = null) : IAppServerTransport
    {
        public string? LastMethod { get; private set; }

        public object? LastParams { get; private set; }

        public Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default) =>
            Task.FromResult<AppServerIncomingMessage?>(null);

        public Task WriteMessageAsync(object message, CancellationToken ct = default) => Task.CompletedTask;

        public Task<AppServerIncomingMessage> SendClientRequestAsync(string method, object? @params, CancellationToken ct = default, TimeSpan? timeout = null)
        {
            LastMethod = method;
            LastParams = @params;
            if (exception != null)
                return Task.FromException<AppServerIncomingMessage>(exception);
            var payload = result ?? new ChannelDeliveryResult { Delivered = true };
            var json = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, result = payload }, SessionWireJsonOptions.Default);
            var msg = JsonSerializer.Deserialize<AppServerIncomingMessage>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            })!;
            return Task.FromResult(msg);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingApprovalService(bool approve) : IApprovalService
    {
        public string? LastOperation { get; private set; }

        public string? LastPath { get; private set; }

        public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null)
        {
            LastOperation = operation;
            LastPath = path;
            return Task.FromResult(approve);
        }

        public Task<bool> RequestShellApprovalAsync(ShellApprovalRequest request, ApprovalContext? context = null)
            => throw new NotSupportedException();

        public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null)
        {
            LastOperation = operation;
            LastPath = target;
            return Task.FromResult(approve);
        }
    }

    private sealed class ThrowingRegisterArtifactStore : IChannelMediaArtifactStore
    {
        public Task<ChannelMediaArtifact?> GetAsync(string artifactId, CancellationToken cancellationToken = default)
            => Task.FromResult<ChannelMediaArtifact?>(null);

        public Task RegisterAsync(ChannelMediaArtifact artifact, CancellationToken cancellationToken = default)
            => Task.FromException(new IOException("register failed"));

        public Task DeleteAsync(string artifactId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeSessionService : ISessionService, IThreadAgentRefreshService
    {
        public int AgentInvalidationCount { get; private set; }
        public Action<SessionThread>? ThreadCreatedForBroadcast { get; set; }
        public Action<string>? ThreadDeletedForBroadcast { get; set; }
        public Action<SessionThread>? ThreadRenamedForBroadcast { get; set; }
        public Action<string, ThreadStatus, ThreadStatus>? ThreadStatusChangedForBroadcast { get; set; }
        public Action<string, SessionThreadRuntimeSignal, SessionTurn?>? ThreadRuntimeSignalForBroadcast { get; set; }

        public Task<SessionThread> CreateThreadAsync(SessionIdentity identity, ThreadConfiguration? config = null, HistoryMode historyMode = HistoryMode.Server, string? threadId = null, string? displayName = null, CancellationToken ct = default, ThreadSource? source = null, bool ephemeral = false) => throw new NotImplementedException();
        public Task<SessionThread> ResumeThreadAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ArchiveThreadAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UnarchiveThreadAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ThreadSummary>> FindThreadsAsync(SessionIdentity identity, bool includeArchived = false, IReadOnlyList<string>? crossChannelOrigins = null, CancellationToken ct = default, bool includeSubAgents = false, ThreadDiscoveryScope scope = ThreadDiscoveryScope.Identity) => throw new NotImplementedException();
        public Task<int> CountWorkspaceThreadsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpsertThreadSpawnEdgeAsync(ThreadSpawnEdge edge, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SetThreadSpawnEdgeStatusAsync(string parentThreadId, string childThreadId, string status, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ThreadSpawnEdge>> ListSubAgentChildrenAsync(string parentThreadId, bool includeClosed = false, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<SessionEvent> SubscribeThreadAsync(string threadId, bool replayRecent = false, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<SessionEvent> SubmitInputAsync(string threadId, IList<AIContent> content, SenderContext? sender = null, ChatMessage[]? messages = null, CancellationToken ct = default, SessionInputSnapshot? inputSnapshot = null) => throw new NotImplementedException();
        public Task ResolveApprovalAsync(string threadId, string turnId, string requestId, SessionApprovalDecision decision, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ResolveUserInputRequestAsync(string threadId, string turnId, string requestId, RequestUserInputResponse response, CancellationToken ct = default) => throw new NotImplementedException();
        public Task CancelTurnAsync(string threadId, string turnId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task CleanBackgroundTerminalsAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SessionThread> RollbackThreadAsync(string threadId, int numTurns, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<QueuedTurnInput> EnqueueTurnInputAsync(string threadId, IList<AIContent> content, SenderContext? sender = null, CancellationToken ct = default, SessionInputSnapshot? inputSnapshot = null) => throw new NotImplementedException();
        public Task<string> SteerTurnAsync(string threadId, string expectedTurnId, IList<AIContent> content, SenderContext? sender = null, CancellationToken ct = default, SessionInputSnapshot? inputSnapshot = null) => throw new NotImplementedException();
        public Task<IReadOnlyList<QueuedTurnInput>> RemoveQueuedTurnInputAsync(string threadId, string queuedInputId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<QueuedTurnInput>> ReorderQueuedTurnInputsAsync(string threadId, IReadOnlyList<string> orderedQueuedInputIds, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<QueuedTurnInput>> UpdateQueuedTurnInputAsync(string threadId, string queuedInputId, string expectedTurnId, string status, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SetThreadModeAsync(string threadId, string mode, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateThreadConfigurationAsync(string threadId, ThreadConfiguration config, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SessionThread> GetThreadAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> GetInstructionSourcesAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SessionThread> EnsureThreadLoadedAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteThreadPermanentlyAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RenameThreadAsync(string threadId, string displayName, CancellationToken ct = default) => throw new NotImplementedException();
        public ContextUsageSnapshot? TryGetContextUsageSnapshot(string threadId) => null;
        public Task RefreshThreadAgentAsync(string threadId, CancellationToken ct = default) =>
            Task.CompletedTask;
        public void InvalidateThreadAgents() => AgentInvalidationCount++;
    }
}
