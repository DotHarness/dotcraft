using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Skills;
using DotCraft.Security;
using DotCraft.Tracing;
using DotCraft.Tools;
using DotCraft.TraceViewer.Analysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DotCraft.TraceViewer.Tests;

public sealed class TraceReviewTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-trace-viewer-review-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Validator_rejects_evidence_outside_snapshot()
    {
        var snapshot = CreateSnapshot();
        var finding = CreateFinding("finding-1", TraceFindingSeverity.Minor, "missing");

        var exception = Assert.Throws<InvalidDataException>(() =>
            TraceReviewValidator.ValidateAndOrder([finding], snapshot));

        Assert.Contains("not in this trace snapshot", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_orders_severity_then_evidence_time()
    {
        var snapshot = CreateSnapshot();
        var suggestion = CreateFinding("suggestion", TraceFindingSeverity.Suggestion, "event-1");
        var laterMajor = CreateFinding("later", TraceFindingSeverity.Major, "event-2");
        var earlierMajor = CreateFinding("earlier", TraceFindingSeverity.Major, "event-1");
        var validated = TraceReviewValidator.ValidateAndOrder(
            [suggestion, laterMajor, earlierMajor], snapshot);

        Assert.Equal(["earlier", "later", "suggestion"], validated.Select(item => item.Id));
    }

    [Fact]
    public void Validator_rejects_reversed_range_when_events_share_a_timestamp()
    {
        var timestamp = DateTimeOffset.UnixEpoch;
        var snapshot = CreateSnapshot(
            new TraceEvent { Id = "event-1", SessionKey = "thread-1", Type = TraceEventType.Request, Timestamp = timestamp },
            new TraceEvent { Id = "event-2", SessionKey = "thread-1", Type = TraceEventType.Response, Timestamp = timestamp });
        var finding = CreateFinding("finding-1", TraceFindingSeverity.Minor, "event-2") with
        {
            Evidence = [new TraceEvidenceReference("event-2", "event-1", "Reversed range")]
        };

        Assert.Throws<InvalidDataException>(() =>
            TraceReviewValidator.ValidateAndOrder([finding], snapshot));
    }

    [Fact]
    public void Validator_accepts_snapshot_order_when_timestamps_are_not_monotonic()
    {
        var snapshot = CreateSnapshot(
            new TraceEvent { Id = "event-1", SessionKey = "thread-1", Type = TraceEventType.Request, Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(2) },
            new TraceEvent { Id = "event-2", SessionKey = "thread-1", Type = TraceEventType.Response, Timestamp = DateTimeOffset.UnixEpoch });
        var finding = CreateFinding("finding-1", TraceFindingSeverity.Minor, "event-1") with
        {
            Evidence = [new TraceEvidenceReference("event-1", "event-2", "Recorded range")]
        };

        var validated = TraceReviewValidator.ValidateAndOrder([finding], snapshot);

        Assert.Single(validated);
    }

    [Fact]
    public void Store_atomically_replaces_latest_review_and_ignores_corruption()
    {
        var store = new TraceReviewStore(_root);
        var first = CreateReview([new TraceEvidenceReference("event-1", null, "Evidence")]);
        var second = first with { Summary = "Updated summary" };
        var snapshot = CreateSnapshot();
        var conversation = new[] { new TraceConversationMessage("You", "Why?", DateTimeOffset.UnixEpoch) };
        store.Save(snapshot.WorkspacePath, new StoredTraceReview(first, snapshot, conversation));
        store.Save(snapshot.WorkspacePath, new StoredTraceReview(second, snapshot, conversation));

        var loaded = store.Load(snapshot.WorkspacePath, first.SessionKey);
        Assert.Equal("Updated summary", loaded?.Review.Summary);
        Assert.Equal(snapshot.Revision, loaded?.Snapshot.Revision);
        Assert.Equal("Why?", Assert.Single(loaded!.Conversation).Content);
        var jsonPath = Assert.Single(Directory.EnumerateFiles(_root, "*.json", SearchOption.AllDirectories));
        Assert.DoesNotContain(snapshot.WorkspacePath, Path.GetFileName(jsonPath), StringComparison.OrdinalIgnoreCase);
        File.WriteAllText(jsonPath, "not json");
        Assert.Null(store.Load(snapshot.WorkspacePath, first.SessionKey));
    }

    [Fact]
    public async Task Submission_tool_source_exposes_only_the_product_contract()
    {
        var source = new TraceReviewSubmissionToolSource(new TraceAnalysisContext());
        var analysisPath = Path.Combine(_root, "analysis");
        var planning = new ToolPlanningContext(
            "analyst-thread", null, analysisPath, Path.Combine(analysisPath, ".agents"), "analyst", null, [], 1);

        var registrations = await source.GetRegistrationsAsync(planning);

        Assert.Equal(["SubmitTraceReview"], registrations.Select(item => item.Definition.Name.ToString()));
    }

    [Fact]
    public async Task Evidence_bundle_is_fully_readable_by_standard_file_tools()
    {
        var content = "中文证据开头" + new string('x', 12_000) + "中文证据结尾";
        var metadata = "{\"detail\":\"" + new string('数', 4_000) + "\"}";
        var snapshot = new TraceSnapshot(
            Path.Combine(_root, "workspace"),
            "thread-1",
            "1:event-1:revision",
            DateTimeOffset.UnixEpoch,
            [new TraceEvent
            {
                Id = "event-1",
                SessionKey = "thread-1",
                Type = TraceEventType.Response,
                Timestamp = DateTimeOffset.UnixEpoch,
                Content = content,
                MetadataJson = metadata
            }]);
        var store = new TraceEvidenceBundleStore(_root);

        var bundle = store.Ensure(snapshot);

        var manifest = File.ReadAllText(Path.Combine(bundle, "manifest.json"));
        var index = File.ReadAllText(Path.Combine(bundle, "events", "index.jsonl"));
        var detail = Path.Combine(bundle, "events", "000001");
        var chunks = Directory.GetFiles(detail, "content-*.txt").Order().ToArray();
        var metadataChunks = Directory.GetFiles(detail, "metadata-*.json").Order().ToArray();
        Assert.Contains("\"sessionKey\": \"thread-1\"", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain(snapshot.WorkspacePath, manifest, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("event-1", index, StringComparison.Ordinal);
        Assert.True(chunks.Length > 1);
        Assert.All(chunks, chunk => Assert.InRange(File.ReadAllText(chunk).Length, 1, 1800));
        Assert.Equal(content, string.Concat(chunks.Select(File.ReadAllText)));
        Assert.Equal(metadata, string.Concat(metadataChunks.Select(File.ReadAllText)));
        var files = new FileTools(bundle, requireApprovalOutsideWorkspace: false, workspaceRoots: [bundle]);
        foreach (var chunk in chunks.Concat(metadataChunks))
        {
            var result = await files.ReadFile(chunk);
            var text = Assert.IsType<TextContent>(Assert.Single(result));
            Assert.Contains(File.ReadAllText(chunk), text.Text, StringComparison.Ordinal);
        }
        Assert.Contains("中文证据开头", File.ReadAllText(chunks[0]), StringComparison.Ordinal);
        Assert.Contains("中文证据结尾", File.ReadAllText(chunks[^1]), StringComparison.Ordinal);
        Assert.DoesNotContain("fieldFiles", index, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Review_submission_returns_recoverable_result_for_invalid_enum_value()
    {
        var context = new TraceAnalysisContext
        {
            Snapshot = CreateSnapshot(),
            AnalystThreadId = "analyst-thread"
        };
        context.ModelId = "fake-model";
        var source = new TraceReviewSubmissionToolSource(context);
        var analysisPath = Path.Combine(_root, "analysis");
        var planning = new ToolPlanningContext(
            "analyst-thread", null, analysisPath, Path.Combine(analysisPath, ".agents"), "analyst", null, [], 1);
        var registrations = await source.GetRegistrationsAsync(planning);
        var registration = Assert.Single(registrations, item =>
            item.Definition.Name.ToString() == "SubmitTraceReview");
        var result = await registration.Binding.Runtime.InvokeAsync(
            new ToolInvocationContext(
                "analyst-thread",
                null,
                "call-1",
                ToolInvocationAudience.Model,
                registration.Definition.Name,
                registration.Definition.Id,
                registration.Binding.Id,
                registration.Binding.Revision,
                DateTimeOffset.UtcNow),
            new JsonObject
            {
                ["summary"] = "Summary",
                ["findings"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = "finding-1",
                        ["severity"] = "High",
                        ["dimension"] = "Latency",
                        ["title"] = "Title",
                        ["body"] = "Body",
                        ["impact"] = "Impact",
                        ["recommendation"] = "Recommendation",
                        ["basis"] = "Confirmed",
                        ["evidence"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["eventId"] = "event-1",
                                ["label"] = "Evidence"
                            }
                        }
                    }
                }
            });

        Assert.True(result.Success);
        Assert.Contains("review_rejected", result.Content, StringComparison.Ordinal);
        Assert.Contains("Major, Minor, or Suggestion", result.Content, StringComparison.Ordinal);
        Assert.Null(context.SubmittedReview);
    }

    [Fact]
    public void Trace_viewer_assembly_deploys_the_trace_review_skill()
    {
        var loader = new SkillsLoader(_root);

        loader.DeployBuiltInSkills(typeof(TraceAnalystService).Assembly);

        var skill = loader.LoadSkill("trace-review");
        Assert.NotNull(skill);
        Assert.Contains("# DotCraft Trace Review", skill, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyst_file_boundary_blocks_user_data_even_when_it_is_a_trusted_read_path()
    {
        var bundle = Path.Combine(_root, "bundle");
        var userData = Path.Combine(_root, "user-data");
        Directory.CreateDirectory(bundle);
        Directory.CreateDirectory(userData);
        var secret = Path.Combine(userData, "config.json");
        File.WriteAllText(secret, "secret");
        var files = new FileTools(
            bundle,
            requireApprovalOutsideWorkspace: false,
            blacklist: new PathBlacklist([userData]),
            trustedReadPaths: [userData],
            workspaceRoots: [bundle]);

        var result = await files.ReadFile(secret);

        var text = Assert.IsType<Microsoft.Extensions.AI.TextContent>(Assert.Single(result));
        Assert.Contains("blacklist", text.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", text.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Evidence_commit_keeps_only_the_selected_revision()
    {
        var first = CreateSnapshot();
        var second = first with { Revision = "3:event-3:revision" };
        var store = new TraceEvidenceBundleStore(_root);
        var firstPath = store.Ensure(first);
        var secondPath = store.Ensure(second);

        store.KeepOnly(second);

        Assert.False(Directory.Exists(firstPath));
        Assert.True(Directory.Exists(secondPath));
    }

    [Fact]
    public async Task Analyst_reads_the_bundle_and_submits_a_review_through_the_agentic_loop()
    {
        var workspace = Path.Combine(_root, "workspace");
        var dataPath = Path.Combine(workspace, ".agents");
        var analysisRoot = Path.Combine(_root, "analysis");
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
        var provider = new ScriptedReviewProvider();
        await using var analyst = new TraceAnalystService(analysisRoot, services =>
        {
            services.RemoveAll<IModelProvider>();
            services.AddSingleton<IModelProvider>(provider);
        });
        var snapshot = CreateSnapshot() with { WorkspacePath = workspace };

        var review = await analyst.AnalyzeAsync(snapshot, dataPath, progress: null, CancellationToken.None);

        Assert.Equal("Evidence-backed review", review.Summary);
        Assert.Equal("scripted-model", review.ModelId);
        Assert.Equal(snapshot.SessionKey, review.SessionKey);
        Assert.False(string.IsNullOrWhiteSpace(review.AnalystThreadId));
        Assert.Equal(["FindFiles", "ReadFile", "SubmitTraceReview"], provider.Client.ToolCalls);
        Assert.Contains("index.jsonl", provider.Client.ToolResults[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("event-1", provider.Client.ToolResults[1], StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private TraceSnapshot CreateSnapshot(params TraceEvent[] events)
    {
        var snapshotEvents = events.Length > 0
            ? events
            :
            [
                new TraceEvent { Id = "event-1", SessionKey = "thread-1", Type = TraceEventType.Request, Timestamp = DateTimeOffset.UnixEpoch },
                new TraceEvent { Id = "event-2", SessionKey = "thread-1", Type = TraceEventType.TurnCompleted, Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(2) }
            ];
        return new TraceSnapshot(
            Path.Combine(_root, "workspace"),
            "thread-1",
            $"{snapshotEvents.Length}:{snapshotEvents[^1].Id}:revision",
            snapshotEvents[^1].Timestamp,
            snapshotEvents);
    }

    private static TraceReview CreateReview(
        IReadOnlyList<TraceEvidenceReference> evidence,
        IReadOnlyList<TraceFinding>? findings = null) => new(
            1, "thread-1", string.Empty, DateTimeOffset.UnixEpoch, "fake-model", "Summary",
            findings ?? [CreateFinding("finding-1", TraceFindingSeverity.Minor, evidence[0].EventId)],
            string.Empty);

    private static TraceFinding CreateFinding(string id, TraceFindingSeverity severity, string eventId) => new(
        id, severity, "Latency", id, "Body", "Impact", "Recommendation", TraceFindingBasis.Confirmed,
        [new TraceEvidenceReference(eventId, null, "Evidence")]);

    private sealed class ScriptedReviewProvider : IModelProvider
    {
        public ScriptedReviewChatClient Client { get; } = new();
        public IReadOnlyCollection<string> Protocols { get; } = [ModelProviderProtocols.OpenAIChatCompletions];
        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => Client;
    }

    private sealed class ScriptedReviewChatClient : IChatClient
    {
        public List<string> ToolCalls { get; } = [];
        public List<string> ToolResults { get; } = [];

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
            var history = messages.ToList();
            var result = history.SelectMany(message => message.Contents).OfType<FunctionResultContent>().LastOrDefault();
            if (result is not null)
                ToolResults.Add(FormatToolResult(result.Result));

            var call = ToolCalls.Count switch
            {
                0 => new FunctionCallContent("call-find", "FindFiles", new Dictionary<string, object?>
                {
                    ["pattern"] = "index.jsonl",
                    ["path"] = "events"
                }),
                1 => new FunctionCallContent("call-read", "ReadFile", new Dictionary<string, object?>
                {
                    ["path"] = "events/index.jsonl"
                }),
                2 => new FunctionCallContent("call-submit", "SubmitTraceReview", new Dictionary<string, object?>
                {
                    ["summary"] = "Evidence-backed review",
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
                }),
                _ => null
            };

            if (call is null)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            }
            else
            {
                ToolCalls.Add(call.Name);
                yield return new ChatResponseUpdate(ChatRole.Assistant, [call]);
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        private static string FormatToolResult(object? result) => result switch
        {
            IEnumerable<AIContent> content => string.Join("\n", content.OfType<TextContent>().Select(item => item.Text)),
            _ => result?.ToString() ?? string.Empty
        };

        public void Dispose()
        {
        }
    }
}
