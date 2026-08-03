using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.Auth.OpenAI;

/// <summary>
/// Translates between the in-process <see cref="OpenAIUsageSnapshot"/> and the on-wire
/// <see cref="Contract.AuthOpenAiUsageResult"/> sent over the AppServer JSON-RPC protocol.
/// </summary>
public static class OpenAIUsageMapping
{
    public static Contract.AuthOpenAiUsageResult ToWire(OpenAIUsageSnapshot? snapshot)
    {
        if (snapshot is null)
            return new Contract.AuthOpenAiUsageResult { Available = false };

        return new Contract.AuthOpenAiUsageResult
        {
            Available = true,
            PlanType = snapshot.PlanType,
            Primary = ToWire(snapshot.Primary),
            Secondary = ToWire(snapshot.Secondary),
            Credits = snapshot.Credits is null
                ? null
                : new Contract.AuthOpenAiUsageCredits
                {
                    HasCredits = snapshot.Credits.HasCredits,
                    Unlimited = snapshot.Credits.Unlimited,
                    Balance = snapshot.Credits.Balance
                },
            LimitReachedKind = snapshot.LimitReachedKind,
            FetchedAt = snapshot.FetchedAt
        };
    }

    private static Contract.AuthOpenAiUsageWindow? ToWire(RateLimitWindow? window)
    {
        if (window is null)
            return null;
        return new Contract.AuthOpenAiUsageWindow
        {
            UsedPercent = window.UsedPercent,
            WindowSeconds = (int)Math.Round(window.WindowDuration.TotalSeconds),
            ResetAt = window.ResetAt
        };
    }
}
