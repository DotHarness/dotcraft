using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Security.ShellCommands;

public static class ShellPrefixRuleJson
{
    public static IReadOnlyList<ShellPrefixRule> Read(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Shell policy rules must be a JSON array.", nameof(array));

        var rules = new List<ShellPrefixRule>();
        var index = 0;
        foreach (var element in array.EnumerateArray())
            rules.Add(ReadRule(element, index++));

        return rules;
    }

    private static ShellPrefixRule ReadRule(JsonElement element, int index)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw Invalid(index, "must be a JSON object.");

        if (!TryGetProperty(element, "prefix", out var prefixElement) || prefixElement.ValueKind != JsonValueKind.Array)
            throw Invalid(index, "needs a 'prefix' array.");

        var prefix = new List<string>();
        foreach (var word in prefixElement.EnumerateArray())
        {
            if (word.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(word.GetString()))
                throw Invalid(index, "has a 'prefix' entry that is not a non-empty string.");
            prefix.Add(word.GetString()!);
        }

        if (prefix.Count == 0)
            throw Invalid(index, "needs at least one prefix word.");

        if (!TryGetProperty(element, "decision", out var decisionElement)
            || decisionElement.ValueKind != JsonValueKind.String)
            throw Invalid(index, "needs a 'decision' string.");

        var decisionText = decisionElement.GetString()!;
        if (!TryParseDecision(decisionText, out var decision))
            throw Invalid(index, $"has an unsupported decision '{decisionText}'; use allow, prompt, or forbidden.");

        string? justification = null;
        if (TryGetProperty(element, "justification", out var justificationElement)
            && justificationElement.ValueKind != JsonValueKind.Null)
        {
            if (justificationElement.ValueKind != JsonValueKind.String)
                throw Invalid(index, "has a 'justification' that is not a string.");
            justification = justificationElement.GetString();
        }

        return new ShellPrefixRule(prefix, decision, justification);
    }

    public static JsonNode Write(ShellPrefixRule rule)
    {
        var prefix = new JsonArray();
        foreach (var word in rule.Prefix)
            prefix.Add(word);

        var node = new JsonObject
        {
            ["prefix"] = prefix,
            ["decision"] = DecisionText(rule.Decision)
        };

        if (rule.Justification is not null)
            node["justification"] = rule.Justification;

        return node;
    }

    private static string DecisionText(ShellDecision decision) => decision switch
    {
        ShellDecision.Allow => "allow",
        ShellDecision.Prompt => "prompt",
        _ => "forbidden"
    };

    private static bool TryParseDecision(string text, out ShellDecision decision)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "allow":
                decision = ShellDecision.Allow;
                return true;
            case "prompt":
                decision = ShellDecision.Prompt;
                return true;
            case "forbidden":
                decision = ShellDecision.Forbidden;
                return true;
            default:
                decision = ShellDecision.Forbidden;
                return false;
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
            return true;

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static ArgumentException Invalid(int index, string problem) =>
        new($"Shell policy rule at index {index} {problem}");
}
