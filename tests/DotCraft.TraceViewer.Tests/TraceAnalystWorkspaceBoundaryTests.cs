using System.Runtime.CompilerServices;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Tracing;
using DotCraft.TraceViewer.Analysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DotCraft.TraceViewer.Tests;

/// <summary>
/// The analyst thread runs with auto-approval and must therefore treat the Evidence
/// Bundle as a hard boundary: reads outside the bundle are rejected without prompting,
/// never silently auto-approved.
/// </summary>
public sealed class TraceAnalystWorkspaceBoundaryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "dotcraft-trace-analyst-boundary-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Analyst_read_outside_the_evidence_bundle_is_hard_rejected()
    {
        var workspace = Path.Combine(_root, "workspace");
        var dataPath = Path.Combine(workspace, ".agents");
        Directory.CreateDirectory(dataPath);
        File.WriteAllText(Path.Combine(dataPath, "config.json"), """
            {
              "ProviderId": "scripted",
              "ProviderPreferences": { "scripted": { "Model": "scripted-model" } },
              "Providers": {
                "scripted": {
                  "DisplayName": "Scripted provider",
                  "Protocol": "openai-chat-completions",
                  "ApiKey": "test",
                  "EndPoint": "https://example.invalid"
                }
              }
            }
            """);
        var outsideFile = Path.Combine(_root, "outside.txt");
        File.WriteAllText(outsideFile, "TOP-PRIVATE-CONTENT");
        var provider = new ScriptedBoundaryProvider();
        provider.Client.EnqueueToolCall("ReadFile", new Dictionary<string, object?>
        {
            ["path"] = outsideFile
        });
        provider.Client.EnqueueToolCall("SubmitTraceReview", ValidSubmission());
        await using var analyst = new TraceAnalystService(Path.Combine(_root, "analysis"), services =>
        {
            services.RemoveAll<IModelProvider>();
            services.AddSingleton<IModelProvider>(provider);
        });
        var snapshot = CreateSnapshot(workspace);

        await analyst.AnalyzeAsync(snapshot, dataPath, progress: null, CancellationToken.None);

        Assert.Equal(["ReadFile", "SubmitTraceReview"], provider.Client.ToolCalls);
        Assert.Contains(
            "outside workspace",
            provider.Client.ToolResults[0],
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "TOP-PRIVATE-CONTENT",
            provider.Client.ToolResults[0],
            StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static TraceSnapshot CreateSnapshot(string workspace)
    {
        TraceEvent[] events =
        [
            new TraceEvent { Id = "event-1", SessionKey = "thread-1", Type = TraceEventType.Request, Timestamp = DateTimeOffset.UnixEpoch },
            new TraceEvent { Id = "event-2", SessionKey = "thread-1", Type = TraceEventType.TurnCompleted, Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(2) }
        ];
        return new TraceSnapshot(
            workspace,
            "thread-1",
            $"{events.Length}:{events[^1].Id}:revision",
            events[^1].Timestamp,
            events);
    }

    private static Dictionary<string, object?> ValidSubmission() => new()
    {
        ["summary"] = "Boundary review",
        ["findings"] = new object[]
        {
            new Dictionary<string, object?>
            {
                ["id"] = "finding-1",
                ["severity"] = "Minor",
                ["dimension"] = "Latency",
                ["title"] = "Recorded latency",
                ["body"] = "The trace contains a completed turn.",
                ["impact"] = "The recorded turn consumed time.",
                ["recommendation"] = "Inspect the cited turn timing.",
                ["basis"] = "Confirmed",
                ["evidence"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["eventId"] = "event-2",
                        ["label"] = "Completed turn"
                    }
                }
            }
        }
    };

    private sealed class ScriptedBoundaryProvider : IModelProvider
    {
        public ScriptedBoundaryChatClient Client { get; } = new();
        public IReadOnlyCollection<string> Protocols { get; } = [ModelProviderProtocols.OpenAIChatCompletions];
        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => Client;
    }

    private sealed class ScriptedBoundaryChatClient : IChatClient
    {
        private readonly Queue<FunctionCallContent> _calls = new();

        public List<string> ToolCalls { get; } = [];
        public List<string> ToolResults { get; } = [];

        public void EnqueueToolCall(string name, Dictionary<string, object?> arguments) =>
            _calls.Enqueue(new FunctionCallContent($"call-{_calls.Count + 1}", name, arguments));

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ToolResults.Clear();
            ToolResults.AddRange(messages
                .SelectMany(message => message.Contents)
                .OfType<FunctionResultContent>()
                .Select(result => FormatToolResult(result.Result)));

            if (_calls.TryDequeue(out var call))
            {
                ToolCalls.Add(call.Name);
                yield return new ChatResponseUpdate(ChatRole.Assistant, [call]);
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }

        private static string FormatToolResult(object? result) => result switch
        {
            IEnumerable<AIContent> content => string.Join("\n", content.OfType<TextContent>().Select(item => item.Text)),
            _ => result?.ToString() ?? string.Empty
        };
    }
}
