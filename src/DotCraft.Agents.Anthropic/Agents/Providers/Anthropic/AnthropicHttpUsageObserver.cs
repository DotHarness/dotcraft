namespace DotCraft.Agents;

public static class AnthropicHttpUsageObserver
{
    public static IProviderHttpUsageObserver Create(bool eventStream) => CreateObserver(eventStream);

    public static IProviderHttpUsageObserver CreateAuto() => CreateObserver(null);

    private static IProviderHttpUsageObserver CreateObserver(bool? eventStream)
    {
        long input = 0, cacheRead = 0, cacheWrite = 0;
        Func<ProviderHttpUsage, string, long, ProviderHttpUsage> update = (usage, name, value) =>
        {
            switch (name)
            {
                case "input_tokens": input = value; break;
                case "cache_read_input_tokens": cacheRead = value; break;
                case "cache_creation_input_tokens": cacheWrite = value; break;
                case "output_tokens": usage = usage with { OutputTokens = value }; break;
            }
            return usage with
            {
                InputTokens = input + cacheRead + cacheWrite,
                CachedInputTokens = cacheRead,
                CacheWriteTokens = cacheWrite
            };
        };
        return eventStream.HasValue
            ? new JsonHttpUsageObserver(eventStream.Value, update)
            : JsonHttpUsageObserver.CreateAuto(update);
    }
}
