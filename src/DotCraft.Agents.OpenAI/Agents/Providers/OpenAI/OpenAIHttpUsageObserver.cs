namespace DotCraft.Agents;

public static class OpenAIHttpUsageObserver
{
    public static IProviderHttpUsageObserver Create(bool eventStream) =>
        new JsonHttpUsageObserver(eventStream, static (usage, name, value) => name switch
        {
            "input_tokens" or "prompt_tokens" => usage with { InputTokens = value },
            "output_tokens" or "completion_tokens" => usage with { OutputTokens = value },
            "cached_tokens" => usage with { CachedInputTokens = value },
            "reasoning_tokens" => usage with { ReasoningTokens = value },
            _ => usage
        });
}
