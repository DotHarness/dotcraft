namespace DotCraft.Agents;

public static class OpenAIServiceTierPricing
{
    public static decimal GetMultiplier(string model, string? requestTier, string? responseTier, bool isCodex)
    {
        var tier = isCodex && responseTier == "default" && requestTier is "priority" or "flex" or "ultrafast"
            ? requestTier
            : responseTier ?? requestTier;
        return tier switch
        {
            "flex" => 0.5m,
            "priority" => model == "gpt-5.5" ? 2.5m : 2m,
            "fast" when !isCodex => model == "gpt-5.5" ? 2.5m : 2m,
            "ultrafast" when model is "gpt-6.2-astra" or "gpt-6.2-sol" or "gpt-6-astra" or "gpt-6.1-sol" => 6m,
            _ => 1m
        };
    }
}
