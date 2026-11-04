using System.Text.RegularExpressions;

namespace DotCraft.Configuration;

internal static partial class AnthropicThinkingBindingSupport
{
    internal static bool IsVerified(string? endpoint, string? model)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.Host is not ("api.anthropic.com" or "openrouter.ai")
            || string.IsNullOrWhiteSpace(model))
            return false;

        var id = model.Trim().ToLowerInvariant();
        if (id.StartsWith("~anthropic/", StringComparison.Ordinal))
            id = id[11..];
        else if (id.StartsWith("anthropic/", StringComparison.Ordinal))
            id = id[10..];
        if (uri.Host == "openrouter.ai" && id == "claude-opus-5")
            return false;
        return VerifiedModels().IsMatch(id);
    }

    [GeneratedRegex(@"^claude-(?:opus-(?:5|5[.-]5)|(?:sonnet|haiku)-5[.-]5|(?:fable|mythos)-5[.-]1)(?:-\d{8})?$", RegexOptions.CultureInvariant)]
    private static partial Regex VerifiedModels();
}
