using System.Text.RegularExpressions;

namespace DotCraft.Configuration;

internal static partial class AnthropicThinkingBindingSupport
{
    internal static bool IsEnabledByDefault(string? endpoint, string? model)
    {
        if (ModelCapabilityIdentity.ForMatching(model) is not { } id)
            return false;

        var version = ClaudeVersion().Match(id[(id.LastIndexOf('/') + 1)..]);
        if (!int.TryParse(version.Groups["major"].ValueSpan, out var major) || major < 5)
            return false;
        var minor = version.Groups["minor"];
        if (major > 5 || (int.TryParse(minor.ValueSpan, out var number) && number >= 1))
            return true;

        return !minor.Success
            && version.Groups["family"].ValueSpan.Equals("opus", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host == "api.anthropic.com";
    }

    [GeneratedRegex(@"^claude-(?<family>opus|sonnet|haiku|fable|mythos)-(?<major>[0-9]+)(?:-(?<minor>[0-9]{1,2}))?(?:-[0-9]{8})?(?:[@:][a-z0-9_.-]+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClaudeVersion();
}
