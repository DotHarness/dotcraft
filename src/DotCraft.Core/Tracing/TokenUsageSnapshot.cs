using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;
using DotCraft.Sessions;

namespace DotCraft.Tracing;

/// <summary>
/// Normalized token usage captured from provider responses.
/// </summary>
public readonly record struct TokenUsageSnapshot(
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens,
    long ReasoningOutputTokens,
    long CacheWriteInputTokens = 0)
{
    public long FreshInputTokens => Math.Max(0, InputTokens - CachedInputTokens - CacheWriteInputTokens);

    public long NonCachedInputTokens => Math.Max(0, InputTokens - CachedInputTokens);

    public long TotalTokens => InputTokens + OutputTokens;

    public double CacheHitRate => InputTokens > 0
        ? CachedInputTokens / (double)InputTokens
        : 0;
}

/// <summary>
/// Extracts cached-token details from strongly typed Microsoft.Extensions.AI usage
/// fields and provider-specific raw metadata shapes.
/// </summary>
public static class TokenUsageExtractor
{
    private const int MaxTraversalDepth = 32;

    public static TokenUsageSnapshot FromResponse(ChatResponse response) =>
        FromUsageDetails(response.Usage, response.AdditionalProperties, response.RawRepresentation);

    public static TokenUsageSnapshot FromUsageContent(UsageContent usage) =>
        FromUsageDetails(usage.Details, usage.AdditionalProperties, usage.RawRepresentation);

    public static TokenUsageSnapshot FromUsageDetails(
        UsageDetails? details,
        object? metadata = null,
        object? rawRepresentation = null)
    {
        var promptInput = TryFindLong(metadata, TokenField.PromptInput)
            ?? TryFindLong(rawRepresentation, TokenField.PromptInput);
        var nativeInput = TryFindLong(metadata, TokenField.NativeInput)
            ?? TryFindLong(rawRepresentation, TokenField.NativeInput);
        var output = details?.OutputTokenCount
            ?? TryFindLong(metadata, TokenField.Output)
            ?? TryFindLong(rawRepresentation, TokenField.Output)
            ?? 0;
        var cacheRead = TryFindLong(metadata, TokenField.CacheReadInput)
            ?? TryFindLong(rawRepresentation, TokenField.CacheReadInput);
        var cached = details?.CachedInputTokenCount
            ?? TryFindLong(details?.AdditionalCounts, TokenField.CachedInput)
            ?? cacheRead
            ?? TryFindLong(metadata, TokenField.CachedInput)
            ?? TryFindLong(rawRepresentation, TokenField.CachedInput)
            ?? 0;
        var cacheWrite = TryFindLong(metadata, TokenField.CacheWriteInput)
            ?? TryFindLong(rawRepresentation, TokenField.CacheWriteInput)
            ?? TryFindLong(details?.AdditionalCounts, TokenField.CacheWriteInput)
            ?? 0;
        var reasoning = details?.ReasoningTokenCount
            ?? TryFindLong(details?.AdditionalCounts, TokenField.ReasoningOutput)
            ?? TryFindLong(metadata, TokenField.ReasoningOutput)
            ?? TryFindLong(rawRepresentation, TokenField.ReasoningOutput)
            ?? 0;
        var input = details?.InputTokenCount
            ?? promptInput
            ?? nativeInput
            ?? TryFindLong(metadata, TokenField.Input)
            ?? TryFindLong(rawRepresentation, TokenField.Input)
            ?? 0;

        // Anthropic usage reports input_tokens as only the non-cache portion
        // after the last breakpoint. When no OpenAI-style prompt_tokens total is
        // present, reconstruct total input from native cache read/write fields.
        if (!promptInput.HasValue
            && nativeInput.HasValue
            && (cacheRead.GetValueOrDefault() > 0 || cacheWrite > 0))
        {
            input = nativeInput.Value + cacheRead.GetValueOrDefault() + cacheWrite;
        }

        return new TokenUsageSnapshot(
            Math.Max(0, input),
            Math.Max(0, output),
            Math.Clamp(cached, 0, Math.Max(0, input)),
            Math.Max(0, reasoning),
            Math.Clamp(cacheWrite, 0, Math.Max(0, input)));
    }

    private static long? TryFindLong(object? value, TokenField field)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        return TryFindLongCore(value, field, visited, depth: 0);
    }

    private static long? TryFindLongCore(object? value, TokenField field, HashSet<object> visited, int depth)
    {
        if (value == null)
            return null;

        if (depth > MaxTraversalDepth)
            return null;

        if (value is JsonElement element)
            return TryFindLong(element, field, visited, depth);

        if (!value.GetType().IsValueType && value is not string && !visited.Add(value))
            return null;

        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is string key && IsFieldName(key, field) && TryConvertLong(entry.Value, out var direct))
                    return direct;
            }

            foreach (DictionaryEntry entry in dictionary)
            {
                var nested = TryFindLongCore(entry.Value, field, visited, depth + 1);
                if (nested.HasValue)
                    return nested.Value;
            }
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            foreach (var item in enumerable)
            {
                var itemType = item?.GetType();
                if (itemType == null)
                    continue;

                var keyProperty = itemType.GetProperty("Key");
                var valueProperty = itemType.GetProperty("Value");
                if (keyProperty?.GetValue(item) is string key)
                {
                    var itemValue = valueProperty?.GetValue(item);
                    if (IsFieldName(key, field) && TryConvertLong(itemValue, out var direct))
                        return direct;
                    var nestedValue = TryFindLongCore(itemValue, field, visited, depth + 1);
                    if (nestedValue.HasValue)
                        return nestedValue.Value;
                }
            }
        }

        var type = value.GetType();
        if (type.FullName?.StartsWith("System.", StringComparison.Ordinal) == true)
            return null;

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length != 0)
                continue;

            object? propertyValue;
            try
            {
                propertyValue = property.GetValue(value);
            }
            catch
            {
                continue;
            }

            if (IsFieldName(property.Name, field) && TryConvertLong(propertyValue, out var direct))
                return direct;

            var nested = TryFindLongCore(propertyValue, field, visited, depth + 1);
            if (nested.HasValue)
                return nested.Value;
        }

        return null;
    }

    private static long? TryFindLong(JsonElement element, TokenField field, HashSet<object> visited, int depth)
    {
        if (depth > MaxTraversalDepth)
            return null;

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (IsFieldName(property.Name, field) && TryConvertLong(property.Value, out var direct))
                        return direct;
                }

                foreach (var property in element.EnumerateObject())
                {
                    var nested = TryFindLongCore(property.Value, field, visited, depth + 1);
                    if (nested.HasValue)
                        return nested.Value;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var nested = TryFindLongCore(item, field, visited, depth + 1);
                    if (nested.HasValue)
                        return nested.Value;
                }
                break;
        }

        return null;
    }

    private static bool TryConvertLong(object? value, out long result)
    {
        switch (value)
        {
            case null:
                result = 0;
                return false;
            case long l:
                result = l;
                return true;
            case int i:
                result = i;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Number } element:
                return element.TryGetInt64(out result);
            case JsonElement { ValueKind: JsonValueKind.String } element:
                return long.TryParse(element.GetString(), out result);
            case string s:
                return long.TryParse(s, out result);
            default:
                try
                {
                    result = Convert.ToInt64(value);
                    return true;
                }
                catch
                {
                    result = 0;
                    return false;
                }
        }
    }

    private static bool IsFieldName(string name, TokenField field)
    {
        var normalized = NormalizeName(name);
        return field switch
        {
            TokenField.Input => normalized is "inputtokens" or "inputtokencount" or "prompttokens" or "prompttokencount",
            TokenField.NativeInput => normalized is "inputtokens" or "inputtokencount",
            TokenField.PromptInput => normalized is "prompttokens" or "prompttokencount",
            TokenField.Output => normalized is "outputtokens" or "outputtokencount" or "completiontokens" or "completiontokencount" or "candidatestokencount",
            TokenField.CachedInput => normalized is "cachedtokens" or "cachedinputtokens" or "cachedinputtokencount" or "cachedcontenttokencount",
            TokenField.CacheReadInput => normalized is "cachereadinputtokens" or "cachereadinputtokencount" or "cachedcontenttokencount",
            TokenField.CacheWriteInput => normalized is "cachecreationinputtokens" or "cachewriteinputtokens" or "cachewriteinputtokencount",
            TokenField.ReasoningOutput => normalized is "reasoningtokens" or "reasoningoutputtokens" or "reasoningoutputtokencount" or "reasoningtokencount",
            _ => false
        };
    }

    private static string NormalizeName(string name) =>
        new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private enum TokenField
    {
        Input,
        NativeInput,
        PromptInput,
        Output,
        CachedInput,
        CacheReadInput,
        CacheWriteInput,
        ReasoningOutput
    }
}
