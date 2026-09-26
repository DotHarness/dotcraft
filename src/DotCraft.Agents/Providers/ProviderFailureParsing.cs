using System.Globalization;
using System.Text.RegularExpressions;

namespace DotCraft.Agents;

public static partial class ProviderFailureParsing
{
    private static readonly string[] QuotaTokens =
    [
        "insufficient_quota",
        "insufficient balance",
        "credit_balance_exhausted",
        "usage_limit_reached",
        "billing_hard_limit_reached",
        "organization_spend_limit_exceeded",
        "project_spend_limit_exceeded",
        "organization_usage_limit_exceeded",
        "exceeded your current quota"
    ];

    public static TimeSpan? ParseRetryAfter(string? value, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (double.TryParse(trimmed, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds))
            return SafeDelay(seconds);
        if (DateTimeOffset.TryParseExact(trimmed,
                ["r", "dddd, dd-MMM-yy HH':'mm':'ss 'GMT'", "ddd MMM d HH':'mm':'ss yyyy", "ddd MMM  d HH':'mm':'ss yyyy"],
                CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when))
            return when > now ? when - now : TimeSpan.Zero;
        return null;
    }

    private static TimeSpan? SafeDelay(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return null;
        try { return TimeSpan.FromSeconds(seconds); }
        catch (OverflowException) { return null; }
    }

    public static TimeSpan? ParseRetryAfterFromMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        var match = RetryAfterPhrase().Match(message);
        if (!match.Success
            || !double.TryParse(
                match.Groups[1].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var amount))
        {
            return null;
        }

        var unit = match.Groups[2].Value.ToLowerInvariant();
        return SafeDelay(unit.StartsWith("ms", StringComparison.Ordinal) ? amount / 1000 : amount);
    }

    /// <summary>
    /// Providers reuse HTTP 429 for throttling and for an exhausted plan, so only the body can
    /// tell them apart.
    /// </summary>
    public static bool LooksLikeQuotaExhausted(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return false;

        foreach (var token in QuotaTokens)
        {
            if (detail.Contains(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool Mentions(string? detail, string token) =>
        !string.IsNullOrWhiteSpace(detail)
        && detail.Contains(token, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(
        @"try again in\s*(\d+(?:\.\d+)?)\s*(ms|milliseconds?|s|seconds?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RetryAfterPhrase();
}
