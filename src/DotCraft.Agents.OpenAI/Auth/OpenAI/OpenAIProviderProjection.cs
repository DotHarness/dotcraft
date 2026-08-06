using DotCraft.Agents;

namespace DotCraft.Auth.OpenAI;

public static class OpenAIProviderProjection
{
    public static ProviderAuthenticationStatus ToProviderStatus(OpenAIAuthStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new ProviderAuthenticationStatus(
            status.LoggedIn,
            status.AccountId,
            status.Email,
            PlanType: status.PlanType,
            Email: status.Email,
            LastRefresh: status.LastRefresh,
            AccessTokenExpiresAt: status.AccessTokenExpiresAt);
    }

    public static ProviderUsageSnapshot? ToProviderUsage(OpenAIUsageSnapshot? snapshot) =>
        snapshot == null ? null : new ProviderUsageSnapshot(
            snapshot.FetchedAt,
            new Dictionary<string, double>(),
            PlanType: snapshot.PlanType,
            Primary: snapshot.Primary == null ? null : new ProviderRateLimitWindow(
                snapshot.Primary.UsedPercent,
                snapshot.Primary.WindowDuration,
                snapshot.Primary.ResetAt),
            Secondary: snapshot.Secondary == null ? null : new ProviderRateLimitWindow(
                snapshot.Secondary.UsedPercent,
                snapshot.Secondary.WindowDuration,
                snapshot.Secondary.ResetAt),
            Credits: snapshot.Credits == null ? null : new ProviderCreditStatus(
                snapshot.Credits.HasCredits,
                snapshot.Credits.Unlimited,
                snapshot.Credits.Balance),
            LimitReachedKind: snapshot.LimitReachedKind);
}
