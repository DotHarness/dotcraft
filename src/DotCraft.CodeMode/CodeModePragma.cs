using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DotCraft.CodeMode;

public sealed record CodeModeProgram(string Source, TimeSpan Timeout, int MaxOutputTokens);

public static partial class CodeModePragma
{
    public static bool TryParse(string? code, CodeModeLimits limits, out CodeModeProgram program, out string error)
    {
        program = new CodeModeProgram(string.Empty, limits.DefaultTimeout, limits.DefaultMaxOutputTokens);
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(code))
        {
            error = "exec requires a non-empty JavaScript program.";
            return false;
        }

        var lineEnd = code.IndexOf('\n');
        var firstLine = (lineEnd < 0 ? code : code[..lineEnd]).TrimEnd('\r');
        var match = PragmaLine().Match(firstLine);
        if (!match.Success)
        {
            program = program with { Source = code };
            return true;
        }

        var source = lineEnd < 0 ? string.Empty : code[lineEnd..];
        if (string.IsNullOrWhiteSpace(source))
        {
            error = "exec requires a JavaScript program after the // @exec: line.";
            return false;
        }

        JsonObject options;
        try
        {
            options = JsonNode.Parse(match.Groups[1].Value) as JsonObject
                ?? throw new JsonException("The value is not a JSON object.");
        }
        catch (JsonException ex)
        {
            error = $"The // @exec: line must contain a JSON object: {ex.Message}";
            return false;
        }

        var timeout = limits.DefaultTimeout;
        var maxOutputTokens = limits.DefaultMaxOutputTokens;
        foreach (var (key, value) in options)
        {
            switch (key)
            {
                case "timeout_ms":
                    if (!TryReadInteger(value, 1, (long)limits.MaxTimeout.TotalMilliseconds, out var milliseconds))
                    {
                        error = $"timeout_ms must be an integer from 1 to {(long)limits.MaxTimeout.TotalMilliseconds}.";
                        return false;
                    }
                    timeout = TimeSpan.FromMilliseconds(milliseconds);
                    break;
                case "max_output_tokens":
                    if (!TryReadInteger(value, 1, limits.MaxOutputTokens, out var tokens))
                    {
                        error = $"max_output_tokens must be an integer from 1 to {limits.MaxOutputTokens}.";
                        return false;
                    }
                    maxOutputTokens = (int)tokens;
                    break;
                default:
                    error = $"Unknown // @exec: option '{key}'. Only timeout_ms and max_output_tokens are accepted.";
                    return false;
            }
        }

        program = new CodeModeProgram(source, timeout, maxOutputTokens);
        return true;
    }

    private static bool TryReadInteger(JsonNode? value, long min, long max, out long result)
    {
        result = 0;
        return value is JsonValue json
               && json.GetValueKind() == JsonValueKind.Number
               && json.TryGetValue(out result)
               && result >= min
               && result <= max;
    }

    [GeneratedRegex(@"^[ \t]*// @exec:(.*)$")]
    private static partial Regex PragmaLine();
}
