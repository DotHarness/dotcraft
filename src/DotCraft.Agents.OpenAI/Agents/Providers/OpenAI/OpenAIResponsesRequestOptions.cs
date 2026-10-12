using System.ClientModel.Primitives;
using System.Text;
using System.Text.Json;
using OpenAI.Responses;

#pragma warning disable OPENAI001, SCME0001

namespace DotCraft.Agents;

internal static class OpenAIResponsesRequestOptions
{
    internal static void Freeze(CreateResponseOptions options)
    {
        // SDK array patches are consumed by serialization; capture the final body for repeatable writes.
        options.Patch.Set("$"u8, ModelReaderWriter.Write(options));
    }

    internal static CreateResponseOptions Copy(CreateResponseOptions source)
    {
        var copy = ModelReaderWriter.Read<CreateResponseOptions>(source.Patch.ToBinaryData("J"))!;
        copy.Model = source.Model;
        copy.Instructions = source.Instructions;
        copy.Temperature = source.Temperature;
        copy.TopP = source.TopP;
        copy.TopLogProbabilityCount = source.TopLogProbabilityCount;
        copy.EndUserId = source.EndUserId;
        copy.SafetyIdentifier = source.SafetyIdentifier;
        copy.ServiceTier = source.ServiceTier;
        copy.PreviousResponseId = source.PreviousResponseId;
        copy.BackgroundModeEnabled = source.BackgroundModeEnabled;
        copy.MaxOutputTokenCount = source.MaxOutputTokenCount;
        copy.MaxToolCallCount = source.MaxToolCallCount;
        copy.ToolChoice = source.ToolChoice;
        copy.TruncationMode = source.TruncationMode;
        copy.ParallelToolCallsEnabled = source.ParallelToolCallsEnabled;
        copy.StoredOutputEnabled = source.StoredOutputEnabled;
        copy.StreamingEnabled = source.StreamingEnabled;
        copy.ConversationOptions = source.ConversationOptions;
        copy.PromptCacheKey = source.PromptCacheKey;
        copy.PromptCacheRetentionPolicy = source.PromptCacheRetentionPolicy;
        foreach (var pair in source.Metadata)
            copy.Metadata[pair.Key] = pair.Value;
        if (copy.IncludedProperties.Count > 0)
            copy.IncludedProperties.Clear();
        foreach (var included in source.IncludedProperties)
            copy.IncludedProperties.Add(included);
        if (source.ReasoningOptions is { } reasoning)
        {
            copy.ReasoningOptions = ModelReaderWriter.Read<ResponseReasoningOptions>(reasoning.Patch.ToBinaryData("J"))!;
            copy.ReasoningOptions.ReasoningEffortLevel = reasoning.ReasoningEffortLevel;
            copy.ReasoningOptions.ReasoningSummaryVerbosity = reasoning.ReasoningSummaryVerbosity;
            copy.ReasoningOptions.Context = reasoning.Context;
            CopyPatch(reasoning.Patch, ref copy.ReasoningOptions.Patch);
        }
        if (source.TextOptions is { } text)
        {
            copy.TextOptions = ModelReaderWriter.Read<ResponseTextOptions>(text.Patch.ToBinaryData("J"))!;
            copy.TextOptions.TextFormat = text.TextFormat;
            CopyPatch(text.Patch, ref copy.TextOptions.Patch);
        }
        CopyPatch(source.Patch, ref copy.Patch);
        return copy;
    }

    private static void CopyPatch(JsonPatch source, ref JsonPatch target)
    {
        using var document = JsonDocument.Parse(source.ToBinaryData("JP").ToString());
        foreach (var operation in document.RootElement.EnumerateArray())
        {
            var pointer = operation.GetProperty("path").GetString()!;
            if (pointer is "" or "/" && operation.GetProperty("op").GetString() != "remove")
            {
                foreach (var property in operation.GetProperty("value").EnumerateObject())
                    target.Set(Encoding.UTF8.GetBytes($"$[{JsonSerializer.Serialize(property.Name)}]"),
                        BinaryData.FromString(property.Value.GetRawText()));
                continue;
            }
            var segments = pointer.Split('/').Skip(1)
                .Select(static segment => segment.Replace("~1", "/").Replace("~0", "~")).ToArray();
            var append = segments.LastOrDefault() == "-";
            var path = "$" + string.Concat((append ? segments[..^1] : segments)
                .Select(static segment => int.TryParse(segment, out _)
                    ? $"[{segment}]"
                    : $"[{JsonSerializer.Serialize(segment)}]"));
            var encodedPath = Encoding.UTF8.GetBytes(path);
            if (operation.GetProperty("op").GetString() == "remove")
                target.Remove(encodedPath);
            else if (append)
                target.Append(encodedPath, Encoding.UTF8.GetBytes(operation.GetProperty("value").GetRawText()));
            else
                target.Set(encodedPath, BinaryData.FromString(operation.GetProperty("value").GetRawText()));
        }
    }
}
