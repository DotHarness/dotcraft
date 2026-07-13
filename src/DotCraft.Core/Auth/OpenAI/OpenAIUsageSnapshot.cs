namespace DotCraft.Auth.OpenAI;

/// <summary>
/// Snapshot of the ChatGPT subscription usage / rate-limit state for the signed-in account.
/// Mirrors the response payload of <c>GET https://chatgpt.com/backend-api/wham/usage</c>.
/// Primary and secondary are upstream slots; callers must use each window's duration for
/// user-facing 5-hour or weekly labels.
/// </summary>
public sealed record OpenAIUsageSnapshot(
    string PlanType,
    RateLimitWindow? Primary,
    RateLimitWindow? Secondary,
    CreditStatus? Credits,
    string? LimitReachedKind,
    DateTimeOffset FetchedAt);

/// <summary>
/// One rate-limit window. <see cref="UsedPercent"/> is 0-100; <see cref="ResetAt"/> is when the
/// window rolls over and counters drop back to zero. <see cref="WindowDuration"/> determines the
/// user-facing window kind independently of the upstream primary/secondary slot.
/// </summary>
public sealed record RateLimitWindow(
    int UsedPercent,
    TimeSpan WindowDuration,
    DateTimeOffset ResetAt);

/// <summary>
/// Credit account state — only populated for accounts on credit-based pricing (e.g. workspace seats
/// purchased via credits rather than a flat-rate subscription).
/// </summary>
public sealed record CreditStatus(
    bool HasCredits,
    bool Unlimited,
    string? Balance);
