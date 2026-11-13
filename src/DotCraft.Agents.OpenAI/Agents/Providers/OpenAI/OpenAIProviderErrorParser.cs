using System.ClientModel.Primitives;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotCraft.Agents;

internal static partial class OpenAIProviderErrorParser
{
    private const int MaximumMessageLength = 1_000;
    private const int MaximumInputLength = 32_768;
    private const string SecretName = @"(?:authorization|token|secret|api[_-]?key|access[_-]?key|cookie|password|passwd|pwd|passcode|credential|private[_-]?key|otp|database[_-]?url|db[_-]?url|postgres[_-]?url|redis[_-]?url|mongodb[_-]?uri)";

    internal static (string? Code, string? Message, string? Detail) Read(PipelineResponse? response)
    {
        try
        {
            var body = response?.Content?.ToString();
            if (string.IsNullOrWhiteSpace(body))
                return default;

            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.Object)
            {
                return default;
            }

            var code = ReadString(error, "code");
            var message = ReadString(error, "message");
            var type = ReadString(error, "type");
            return (SafeIdentifier(code), SafeMessage(message), string.Join(' ', code, type, message));
        }
        catch (Exception)
        {
            return default;
        }
    }

    internal static string? SafeIdentifier(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed is { Length: > 0 and <= 128 } && Identifier().IsMatch(trimmed)
            ? trimmed
            : null;
    }

    private static string? ReadString(JsonElement error, string name) =>
        error.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? SafeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > MaximumInputLength)
            return null;

        try
        {
            var safe = PrivateKey().Replace(message, "[redacted_private_key]");
            safe = AuthorizationOrCookie().Replace(safe, "[redacted_secret]");
            safe = BearerToken().Replace(safe, "Bearer [redacted_secret]");
            safe = QuotedSecret().Replace(safe, "[redacted_secret]");
            safe = AssignedSecret().Replace(safe, "[redacted_secret]");
            safe = ApiKey().Replace(safe, "[redacted_api_key]");
            safe = string.Join('\n', safe.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .Select(line => PrivateDetails().IsMatch(line) ? "[redacted_private_details]" : line));
            safe = new string(safe.Where(character => !char.IsControl(character) || character == '\n').ToArray());
            if (safe.Length == 0)
                return null;
            return safe.Length <= MaximumMessageLength ? safe : safe[..(MaximumMessageLength - 1)] + "…";
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"\A[A-Za-z0-9_.:-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();

    [GeneratedRegex(@"-----BEGIN (?:RSA |EC |DSA |OPENSSH |PGP |ENCRYPTED )?PRIVATE KEY-----[\s\S]*", RegexOptions.CultureInvariant)]
    private static partial Regex PrivateKey();

    [GeneratedRegex(@"\b(?:Authorization|Cookie)\s*:[^\r\n]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationOrCookie();

    [GeneratedRegex(@"\bBearer\s+[^\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerToken();

    [GeneratedRegex("\"[^\"\r\n]*" + SecretName + "[^\"\r\n]*\"\\s*:\\s*\"(?:\\\\.|[^\"\\\\])*\"|'[^'\r\n]*" + SecretName + "[^'\r\n]*'\\s*:\\s*'(?:\\\\.|[^'\\\\])*'", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex QuotedSecret();

    [GeneratedRegex("\\b[A-Z0-9_-]*" + SecretName + "[A-Z0-9_-]*\\s*(?::[^\\r\\n]*|=\\s*(?:\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|[^\\s]+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex AssignedSecret();

    [GeneratedRegex(@"\bsk-[A-Za-z0-9_-]{8,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex ApiKey();

    [GeneratedRegex("(?:[\\p{L}\\p{N}.!#$%&'*+/=?^_`{|}~-])(?:@|%40|\\\\u0040)|(?:^|[\\s\"'`(<=>:,])(?:file://|[a-z]:[\\\\/]|\\\\|/(?!/)|~[\\\\/]|\\.{1,2}[\\\\/]|(?:[a-z]%3a)?%(?:2f|5c))|(?:^|[\\s\"'`(<=>,])[\\p{L}\\p{N}_.-]+[\\\\/](?!/)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex PrivateDetails();
}
