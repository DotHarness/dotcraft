using DotCraft.Context.Compaction;

namespace DotCraft.Tests.Context.Compaction;

public sealed class CompactionErrorsTests
{
    [Fact]
    public void IsPromptTooLong_WalksInnerExceptions()
    {
        var exception = new InvalidOperationException(
            "outer wrapper",
            new InvalidOperationException("context_length_exceeded"));

        Assert.True(CompactionErrors.IsPromptTooLong(exception));
    }

    [Theory]
    [InlineData("prompt_too_long")]
    [InlineData("prompt is too long: 137500 tokens > 135000 maximum")]
    [InlineData("context_length_exceeded")]
    [InlineData("Your input exceeds the context window of this model. Please adjust your input and try again.")]
    [InlineData("This model's maximum context length is 128000 tokens. However, your messages resulted in 140000 tokens.")]
    [InlineData("The input token count (1196265) exceeds the maximum number of tokens allowed (1048575).")]
    [InlineData("Please reduce the length of the messages.")]
    [InlineData("Status Code: BadRequest {\"error\":{\"code\":\"context_length_exceeded\",\"message\":\"Your input exceeds the context window of this model.\"}}")]
    public void IsPromptTooLong_ReturnsTrueForProviderOverflowMessages(string message)
    {
        Assert.True(CompactionErrors.IsPromptTooLong(new InvalidOperationException(message)));
    }

    [Theory]
    [InlineData("provider unavailable")]
    [InlineData("Status Code: BadRequest {\"error\":{\"type\":\"invalid_request_error\",\"message\":\"messages.20: tool_use ids were found without tool_result blocks immediately after\"}}")]
    [InlineData("Status Code: Unauthorized {\"error\":\"invalid api key\"}")]
    [InlineData("Rate limit exceeded. Please try again later.")]
    [InlineData("The model name is too long.")]
    [InlineData("The file path is too long.")]
    [InlineData("Status Code: BadRequest {\"error\":{\"code\":\"model_not_found\",\"message\":\"model does not exist\"}}")]
    public void IsPromptTooLong_ReturnsFalseWhenNoKnownMarkerExists(string message)
    {
        var exception = new InvalidOperationException(
            "outer wrapper",
            new InvalidOperationException(message));

        Assert.False(CompactionErrors.IsPromptTooLong(exception));
    }
}
