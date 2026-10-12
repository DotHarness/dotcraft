using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using Xunit;

#pragma warning disable OPENAI001, SCME0001

namespace DotCraft.Tests.Providers.OpenAI;

public sealed class ResponsesInstructionPrefixTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("replace")]
    [InlineData("root")]
    [InlineData("remove")]
    [InlineData("null")]
    public void EffectiveInstructions_PreserveMergingPatchesAndReusableCallerOptions(string patch)
    {
        var native = new CreateResponseOptions { Instructions = "native", Temperature = 0.7f };
        switch (patch)
        {
            case "replace": native.Patch.Set("$.instructions"u8, "patched"); break;
            case "root": native.Patch.Set("$"u8, BinaryData.FromString(
                """{"instructions":"patched","temperature":0.7,"client_metadata":{"caller":"preserved"}}""")); break;
            case "remove": native.Patch.Remove("$.instructions"u8); break;
            case "null": native.Patch.SetNull("$.instructions"u8); break;
        }
        native.Patch.Set("$.client_metadata"u8, BinaryData.FromString("""{"caller":"preserved"}"""));
        var before = ModelReaderWriter.Write(native).ToString();
        var options = new ChatOptions
        {
            Instructions = " runtime ",
            RawRepresentationFactory = _ => native
        };
        ChatMessage[] messages = [new(ChatRole.System, " system "), new(ChatRole.User, "hello")];

        var first = Request(messages, options);
        var second = Request(messages, options);
        Assert.Equal(ModelReaderWriter.Write(first.Options).ToString(), ModelReaderWriter.Write(second.Options).ToString());
        Assert.Equal(before, ModelReaderWriter.Write(native).ToString());
        using var body = Body(first);
        Assert.False(body.RootElement.TryGetProperty("instructions", out _));
        Assert.Equal(0.7, body.RootElement.GetProperty("temperature").GetDouble(), precision: 5);
        Assert.Equal("preserved", body.RootElement.GetProperty("client_metadata").GetProperty("caller").GetString());
        var prefixes = body.RootElement.GetProperty("input").EnumerateArray()
            .Where(item => item.GetProperty("role").GetString() == "developer").ToArray();
        if (patch is "remove" or "null")
        {
            Assert.Empty(prefixes);
            Assert.Null(first.Shape.InstructionsHash);
        }
        else
        {
            var expected = patch is "replace" or "root" ? "patched" : "native" + Environment.NewLine + "runtime\n\nsystem";
            Assert.Equal(expected, Assert.Single(prefixes).GetProperty("content")[0].GetProperty("text").GetString());
            Assert.NotNull(first.Shape.InstructionsHash);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \r\n ")]
    public void EmptyInstructions_DoNotCreatePrefix(string? instructions)
    {
        using var body = Body(Request([new(ChatRole.User, "hello")], new ChatOptions { Instructions = instructions }));
        Assert.False(body.RootElement.TryGetProperty("instructions", out _));
        Assert.Equal("user", Assert.Single(body.RootElement.GetProperty("input").EnumerateArray()).GetProperty("role").GetString());
    }

    [Fact]
    public void StableIdentity_UsesExecutionThreadAndTextAcrossRequestKindsAndWindows()
    {
        var first = PrefixFor(Identity("thread-root"));
        Assert.Equal(first, PrefixFor(Identity("thread-root", "turn_2", "window_2", ProviderRequestKind.Compaction)));
        Assert.Equal(first, PrefixFor(Identity("thread-root", "turn_3", "window_3"), model: "another-model"));
        Assert.NotEqual(first, PrefixFor(Identity("thread-root"), instructions: "changed"));
        Assert.NotEqual(first, PrefixFor(Identity("thread-child") with { RootThreadId = "thread-root", ParentThreadId = "thread-root" }));
        Assert.NotEqual(first, PrefixFor(Identity("thread-fork") with { ForkedFromThreadId = "thread-root" }));

        using var outsideOne = Body(Request([new(ChatRole.User, "one")], new ChatOptions { Instructions = "base" }));
        using var outsideTwo = Body(Request([new(ChatRole.User, "two")], new ChatOptions { Instructions = "base" }));
        Assert.Equal(PrefixId(outsideOne), PrefixId(outsideTwo));
        Assert.StartsWith("msg_", PrefixId(outsideOne));
    }

    [Fact]
    public void CanonicalInput_IsCopiedWithoutChangingHistoryIdsOrDeveloperMessages()
    {
        var canonical = JsonNode.Parse("""
            [{"type":"message","id":"msg_context","role":"developer","content":[{"type":"input_text","text":"base"}]},
             {"type":"message","id":"msg_user","role":"user","content":[{"type":"input_text","text":"hello"}]}]
            """)!.AsArray();
        var original = canonical.ToJsonString();
        var identity = OpenAIResponsesItemIdentityDiagnostics.FromInput(canonical);
        var request = Request([], new ChatOptions { Instructions = "base" }, canonical, identity);
        using var body = Body(request);
        var input = body.RootElement.GetProperty("input").EnumerateArray().ToArray();
        Assert.Equal(original, canonical.ToJsonString());
        Assert.Equal(2, identity.EligibleCount);
        Assert.Equal(3, request.Shape.InputItemIdEligibleCount);
        Assert.Equal(1, request.Shape.InputItemIdGeneratedCount);
        Assert.Equal(canonical[0]!.ToJsonString(), input[1].GetRawText());
        Assert.Equal(canonical[1]!.ToJsonString(), input[2].GetRawText());
        Assert.Equal("msg_context", input[1].GetProperty("id").GetString());
        Assert.Equal("msg_user", input[2].GetProperty("id").GetString());
    }

    [Fact]
    public void Lite_UsesSharedPrefixAndDiagnosticsDescribeFinalInput()
    {
        using var client = new NoopChatClient();
        ChatMessage[] messages = [new(ChatRole.User, "hello")];
        var options = new ChatOptions { Instructions = "base", Tools = [AIFunctionFactory.Create(() => "ok", name: "lookup")] };
        var standard = Request(messages, options);
        var lite = OpenAIResponsesLiteRequestMapper.CreateResponseRequest(
            "gpt-test", messages, options, null, null, client, "installation-test", supportsFreeformTools: true);
        using var standardBody = Body(standard);
        using var liteBody = JsonDocument.Parse(lite.WireBody);
        var input = liteBody.RootElement.GetProperty("input");
        Assert.Equal("additional_tools", input[0].GetProperty("type").GetString());
        Assert.Equal(standardBody.RootElement.GetProperty("input")[0].GetRawText(), input[1].GetRawText());
        Assert.False(liteBody.RootElement.TryGetProperty("instructions", out _));
        Assert.Equal(1, lite.Shape.SchemaVersion);
        Assert.Equal(input.GetArrayLength(), lite.Shape.InputItemCount);
        Assert.Equal(Encoding.UTF8.GetByteCount(input.GetRawText()), lite.Shape.InputBytes);
        Assert.Equal(2, lite.Shape.InputItemIdEligibleCount);
        Assert.Equal(2, lite.Shape.InputItemIdPresentCount);
        Assert.Equal(standard.Shape.InstructionsHash, lite.Shape.InstructionsHash);
        Assert.NotEqual(standard.Shape.InputHash, lite.Shape.InputHash);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Prefix_SurvivesRolloutReplayAndCompactionWithoutBecomingHistory(bool lite)
    {
        var records = new List<ThreadRolloutRecord>();
        var identity = Identity("thread-history");
        using var scope = OpenAIResponsesCodexRuntimeScope.Set(new(identity));
        var context = Context(identity, ProviderHistorySnapshot.Empty("window_1"), [], records);
        ChatMessage[] messages = [new(ChatRole.User, "hello")];
        var options = new ChatOptions { Instructions = "base" };
        var canonical = await context.PrepareInputAsync(messages, options, CancellationToken.None);
        var first = Request(messages, options, canonical.Input, canonical.ItemIdentity);
        using var firstBody = Body(first);
        var prefix = firstBody.RootElement.GetProperty("input")[0].GetRawText();

        var snapshot = ProviderHistoryReplayer.Replay(
            "thread-history", "window_1", new HashSet<string>(["turn_1"], StringComparer.Ordinal), records);
        Assert.Single(snapshot.Entries);
        Assert.Equal("user", snapshot.Entries[0].Item.GetProperty("role").GetString());
        var resumed = Context(identity with { TurnId = "turn_2" }, snapshot, messages, records);
        var restored = await resumed.PrepareInputAsync(messages, options, CancellationToken.None);
        using var resumedBody = Body(Request(messages, options, restored.Input, restored.ItemIdentity));
        Assert.Equal(prefix, resumedBody.RootElement.GetProperty("input")[0].GetRawText());

        var compactInput = await resumed.CaptureInputAsync(ProviderCompactionPhase.PreTurn, messages, options, CancellationToken.None);
        var compact = ChatGptResponsesCompactRequestBuilder.Build(
            "gpt-test", compactInput, messages, options, lite, installationId: "installation-test");
        Assert.Equal(prefix, compact.Input[lite ? 1 : 0].GetRawText());
        Assert.Equal("compaction_trigger", compact.Input[^1].GetProperty("type").GetString());
        var compactedHistory = ResponsesCompactionHistory.Build(compactInput.Items,
            JsonSerializer.SerializeToElement(new { type = "compaction", encrypted_content = "opaque" }));
        var replacement = new ProviderNativeCompactionReplacement("openai-responses",
            compactedHistory.Select((item, index) => new ProviderHistoryItem($"compact:{index}", item)).ToArray(),
            CoveredMessageCount: 1, CoveredThroughTurnId: "turn_1", EstimatedTokensAfter: 10);
        await resumed.ReplaceAsync(replacement, CancellationToken.None);
        var after = await resumed.PrepareInputAsync(messages, options, CancellationToken.None);
        Assert.DoesNotContain(after.Input.OfType<JsonObject>(), item => item["role"]?.GetValue<string>() == "developer");
        using var afterBody = Body(Request(messages, options, after.Input, after.ItemIdentity));
        Assert.Equal(prefix, afterBody.RootElement.GetProperty("input")[0].GetRawText());
        Assert.Single(afterBody.RootElement.GetProperty("input").EnumerateArray(),
            item => item.TryGetProperty("role", out var role) && role.GetString() == "developer");
    }

    private static OpenAIResponsesProviderHistoryContext Context(
        ProviderConversationIdentity identity, ProviderHistorySnapshot snapshot,
        IReadOnlyList<ChatMessage> covered, List<ThreadRolloutRecord> records) => new(
        identity, "openai", snapshot, covered,
        (payload, _) => { records.Add(new ThreadRolloutRecord { Kind = RolloutKinds.ProviderHistoryItemsAppended, ProviderHistoryItemsAppended = payload }); return Task.CompletedTask; },
        (payload, _) => { records.Add(new ThreadRolloutRecord { Kind = RolloutKinds.ProviderHistoryReplaced, ProviderHistoryReplaced = payload }); return Task.CompletedTask; },
        abortAsync: null);

    private static string PrefixFor(ProviderConversationIdentity identity, string instructions = "base", string model = "gpt-test")
    {
        using var scope = OpenAIResponsesCodexRuntimeScope.Set(new(identity));
        using var body = Body(Request([new(ChatRole.User, "hello")], new ChatOptions { Instructions = instructions }, model: model));
        return PrefixId(body);
    }

    private static ProviderConversationIdentity Identity(
        string thread, string turn = "turn_1", string window = "window_1", ProviderRequestKind kind = ProviderRequestKind.Turn) =>
        new(thread, thread, null, null, turn, window, kind, 1, "test", null);

    private static string PrefixId(JsonDocument document) =>
        document.RootElement.GetProperty("input")[0].GetProperty("id").GetString()!;

    private static JsonDocument Body(ResponsesToolSearchMapper.OpenAIResponsesRequest request) =>
        JsonDocument.Parse(ModelReaderWriter.Write(request.Options).ToString());

    private static ResponsesToolSearchMapper.OpenAIResponsesRequest Request(
        IReadOnlyList<ChatMessage> messages, ChatOptions options, JsonArray? input = null,
        OpenAIResponsesItemIdentityDiagnostics? identity = null, string model = "gpt-test")
    {
        using var client = new NoopChatClient();
        return ResponsesToolSearchMapper.CreateResponseRequest(model, messages, options,
            canonicalInput: input, canonicalItemIdentity: identity, rawRepresentationClient: client);
    }

    private sealed class NoopChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
