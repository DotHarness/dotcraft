using System.Text.Json;
using DotCraft.Agents;
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

    private static string Message(string text) => JsonSerializer.Serialize(new
    {
        type = "message", role = "user", content = new[] { new { type = "input_text", text } }
    });

    private static ProviderHistoryItem Item(string json) => new("item", JsonSerializer.Deserialize<JsonElement>(json));
    private static JsonElement Compact(string payload) =>
        JsonSerializer.SerializeToElement(new { type = "compaction", encrypted_content = payload });
}
