using DotCraft.Agents;
using DotCraft.Configuration;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

public static class ProviderUsageMapping
{
    public static Contract.ProviderAuthUsageResult ToResult(ProviderUsageSnapshot? snapshot) =>
        snapshot is null
            ? new Contract.ProviderAuthUsageResult { Available = false }
            : new Contract.ProviderAuthUsageResult
            {
                Available = true,
                FetchedAt = snapshot.ObservedAt,
                PlanType = snapshot.PlanType,
                Primary = ToWire(snapshot.Primary),
                Secondary = ToWire(snapshot.Secondary),
                Credits = ToWire(snapshot.Credits),
                LimitReachedKind = snapshot.LimitReachedKind
            };

    public static IReadOnlyList<Contract.ProviderAuthUsageUpdatedNotification> ToChatGptNotifications(
        AppConfig config, ProviderUsageSnapshot? snapshot) =>
        config.Providers
            .Where(pair => string.IsNullOrWhiteSpace(pair.Value.BackendId)
                && ModelProviderAuthMethods.IsChatGptOAuth(pair.Value.AuthMethod))
            .Select(pair => ToNotification(pair.Key, snapshot))
            .ToArray();

    private static Contract.ProviderAuthUsageUpdatedNotification ToNotification(string providerId, ProviderUsageSnapshot? snapshot)
    {
        var result = ToResult(snapshot);
        return new Contract.ProviderAuthUsageUpdatedNotification
        {
            ProviderId = providerId,
            Available = result.Available,
            FetchedAt = result.FetchedAt,
            PlanType = result.PlanType,
            Primary = result.Primary,
            Secondary = result.Secondary,
            Credits = result.Credits,
            LimitReachedKind = result.LimitReachedKind
        };
    }

    private static Contract.ProviderAuthUsageWindow? ToWire(ProviderRateLimitWindow? window) =>
        window is null ? null : new Contract.ProviderAuthUsageWindow
        {
            UsedPercent = window.UsedPercent,
            WindowSeconds = (int)Math.Round(window.WindowDuration.TotalSeconds),
            ResetAt = window.ResetAt
        };

    private static Contract.ProviderAuthUsageCredits? ToWire(ProviderCreditStatus? credits) =>
        credits is null ? null : new Contract.ProviderAuthUsageCredits
        {
            HasCredits = credits.HasCredits,
            Unlimited = credits.Unlimited,
            Balance = credits.Balance
        };
}
