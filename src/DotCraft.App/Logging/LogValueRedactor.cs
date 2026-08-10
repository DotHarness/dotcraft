using System.Text.RegularExpressions;

namespace DotCraft.Logging;

internal static class LogValueRedactor
{
    private static readonly Regex NamedSecret = new(
        "(?i)(?<prefix>[\\\"']?(?:token|accessToken|access_token|apiKey|api_key|password)[\\\"']?\\s*[:=]\\s*[\\\"']?)(?<value>[^\\\"',&\\s}]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BearerSecret = new(
        "(?i)(?<prefix>Bearer\\s+)(?<value>[A-Za-z0-9._~+\\-/]+=*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var redacted = NamedSecret.Replace(value, "${prefix}[REDACTED]");
        return BearerSecret.Replace(redacted, "${prefix}[REDACTED]");
    }
}
