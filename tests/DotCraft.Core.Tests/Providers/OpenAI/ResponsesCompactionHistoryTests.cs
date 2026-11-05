using System.Text.Json;
using DotCraft.Agents;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Core.Tests.Agents;

public sealed class ResponsesCompactionHistoryTests
{
    [Fact]
    public void RetainsOnlyUserAndClientContextBeforeNewCompaction()
    {
        var items = new[]
        {
            Item("""{"type":"message","role":"user","content":[],"future":1}"""),
            Item("""{"type":"message","role":"assistant","content":[]}"""),
            Item("""{"type":"function_call","call_id":"old"}"""),
            Item("""{"type":"function_call_output","call_id":"old","output":"old"}"""),
            Item("""{"type":"compaction","encrypted_content":"old"}"""),
            Item("""{"type":"message","role":"developer","content":[]}"""),
            Item("""{"type":"additional_tools","tools":[]}"""),
            Item("""{"type":"compaction_trigger"}""")
        };
        var first = ResponsesCompactionHistory.Build(items, Compact("first"));
        Assert.Equal(3, first.Count);
        Assert.Equal(1, first[0].GetProperty("future").GetInt32());
        Assert.Equal("developer", first[1].GetProperty("role").GetString());
        var second = ResponsesCompactionHistory.Build(
            first.Select((item, index) => new ProviderHistoryItem(index.ToString(), item)).ToArray(), Compact("second"));
        Assert.Equal(3, second.Count);
        Assert.Equal("second", second[^1].GetProperty("encrypted_content").GetString());
        Assert.Equal(first[0].GetRawText(), second[0].GetRawText());
    }

    [Fact]
    public void BudgetKeepsLatestMessagesAndTruncatesOnlyBoundaryText()
    {
        var old = Item(Message(new string('x', 8_000)));
        var recent = Item(Message("keep recent"));
        var before = old.Payload.GetRawText();
        var result = ResponsesCompactionHistory.Build([old, recent], Compact("new"), tokenBudget: 100);
        Assert.Equal(3, result.Count);
        Assert.Equal(recent.Payload.GetRawText(), result[1].GetRawText());
        Assert.True(OpenAIResponsesNativeTokenEstimator.Estimate(result.Take(2).ToArray(), [], null) <= 100);
        Assert.True(result[0].GetProperty("content")[0].GetProperty("text").GetString()!.Length < 8_000);
        Assert.Equal(before, old.Payload.GetRawText());
    }

    [Theory]
    [InlineData(100, false)]
    [InlineData(3_000, true)]
    public void MediaIsRetainedWholeOrDroppedAtBudgetBoundary(int budget, bool keep)
    {
        var image = Item("""{"type":"message","role":"user","content":[{"type":"input_image","image_url":"data:image/png;base64,YWJj"}]}""");
        var result = ResponsesCompactionHistory.Build([image], Compact("new"), budget);
        Assert.Equal(keep ? 2 : 1, result.Count);
        if (keep)
            Assert.Equal(image.Payload.GetRawText(), result[0].GetRawText());
    }

    [Fact]
    public void UnicodeBoundaryDoesNotProduceReplacementCharacters()
    {
        var source = Item(Message(string.Concat(Enumerable.Repeat("🦊", 1_000))));
        var result = ResponsesCompactionHistory.Build([source], Compact("new"), 100);
        var text = result[0].GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.NotEmpty(text);
        Assert.Equal(0, text.Length % 2);
        Assert.DoesNotContain("�", text);
    }

    [Fact]
    public void InstallContext_ReplacesKnownContextBeforeLastUserAndPreservesOpaqueItems()
    {
        var olderUser = Item(Message("earlier question"));
        var oldContext = Item("""{"type":"message","id":"msg_previous","role":"developer","content":[{"text":"old instructions","type":"input_text"}],"future":1}""");
        var sameTextUser = Item(Message("old instructions"));
        var latestUser = Item(Message("latest question"));
        var compact = Item("""{"type":"compaction","encrypted_content":"opaque","future":{"value":1}}""");
        var replacement = new ProviderNativeCompactionReplacement(
            "openai-responses", [olderUser, oldContext, sameTextUser, latestUser, compact], 5, "turn_001", 10)
        {
            PreviousContext = [new ChatMessage(new ChatRole("developer"), "old instructions")],
            InitialContext =
            [
                new ChatMessage(new ChatRole("developer"), "new instructions"),
                new ChatMessage(ChatRole.User, "current project instructions")
            ]
        };

        var result = ResponsesCompactionHistory.InstallContext(replacement);

        Assert.Equal(6, result.Count);
        Assert.Equal(olderUser.Payload.GetRawText(), result[0].GetRawText());
        Assert.Equal(sameTextUser.Payload.GetRawText(), result[1].GetRawText());
        Assert.Equal("new instructions", result[2].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("current project instructions", result[3].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal(latestUser.Payload.GetRawText(), result[4].GetRawText());
        Assert.Equal(compact.Payload.GetRawText(), result[5].GetRawText());
    }

    [Fact]
    public void InstallContext_ReplacesTruncatedInheritedContextByPreservedIdentity()
    {
        const string contextId = "msg_inherited_context";
        var context = new ChatMessage(new ChatRole("developer"), new string('x', 8_000))
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [OpenAIResponsesItemIdentity.MetadataKey] = contextId
            }
        };
        var nativeContext = ResponsesToolSearchMapper.BuildInputItems([context], null).Input[0];
        var recent = Item(Message("latest question"));
        var compact = Compact("opaque");
        var compacted = ResponsesCompactionHistory.Build(
            [new ProviderHistoryItem("context", JsonSerializer.SerializeToElement(nativeContext)), recent],
            compact, tokenBudget: 100);
        Assert.Equal(3, compacted.Count);
        Assert.Equal(contextId, compacted[0].GetProperty("id").GetString());
        Assert.NotEqual(context.Text, compacted[0].GetProperty("content")[0].GetProperty("text").GetString());
        var replacement = new ProviderNativeCompactionReplacement(
            "openai-responses",
            compacted.Select((item, index) => new ProviderHistoryItem(index.ToString(), item)).ToArray(),
            2, "turn_001", 100)
        {
            PreviousContext = [context],
            InitialContext = [context.Clone()]
        };

        var result = ResponsesCompactionHistory.InstallContext(replacement);

        Assert.Equal(3, result.Count);
        var restored = Assert.Single(result, item => item.TryGetProperty("id", out var id)
            && id.GetString() == contextId);
        Assert.Equal(context.Text, restored.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal(recent.Payload.GetRawText(), result[1].GetRawText());
        Assert.Equal(compact.GetRawText(), result[2].GetRawText());
    }

    [Fact]
    public void InstallContext_WithoutUserInsertsBeforeCompaction()
    {
        var retained = Item("""{"type":"message","role":"developer","content":[{"type":"input_text","text":"retained client context"}]}""");
        var compact = Compact("opaque");
        var replacement = new ProviderNativeCompactionReplacement(
            "openai-responses",
            [retained, new ProviderHistoryItem("compact", compact)],
            1, "turn_001", 10)
        {
            InitialContext = [new ChatMessage(new ChatRole("developer"), "restored context")]
        };

        var result = ResponsesCompactionHistory.InstallContext(replacement);

        Assert.Equal(3, result.Count);
        Assert.Equal(retained.Payload.GetRawText(), result[0].GetRawText());
        Assert.Equal("restored context", result[1].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal(compact.GetRawText(), result[2].GetRawText());
    }

    private static string Message(string text) => JsonSerializer.Serialize(new
    {
        type = "message", role = "user", content = new[] { new { type = "input_text", text } }
    });

    private static ProviderHistoryItem Item(string json) => new("item", JsonSerializer.Deserialize<JsonElement>(json));
    private static JsonElement Compact(string payload) =>
        JsonSerializer.SerializeToElement(new { type = "compaction", encrypted_content = payload });
}
