using DotCraft.Context.Compaction;
using Xunit;

namespace DotCraft.Tests.Context.Compaction;

public sealed class CompactionErrorsTests
{
    [Theory]
    [InlineData("prompt_too_long", true)]
    [InlineData("prompt is too long: 137500 tokens > 135000 maximum", true)]
    [InlineData("This model's maximum context length is 128000 tokens. However, your messages resulted in 140000 tokens.", true)]
    [InlineData("Status Code: BadRequest {\"error\":{\"code\":\"context_length_exceeded\",\"message\":\"Your input exceeds the context window of this model.\"}}", true)]
    [InlineData("Status Code: BadRequest {\"error\":{\"type\":\"invalid_request_error\",\"message\":\"messages.20: tool_use ids were found without tool_result blocks immediately after\"}}", false)]
    [InlineData("The model name is too long.", false)]
    public void IsPromptTooLong_RecognizesProviderOverflowMessagesInInnerExceptions(string message, bool expected)
    {
        var exception = new InvalidOperationException(
            "outer wrapper",
            new InvalidOperationException(message));

        Assert.Equal(expected, CompactionErrors.IsPromptTooLong(exception));
    }
}
