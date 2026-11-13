using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

public sealed record ProviderMessageOrigin(string ProviderId, string Protocol, string Model);

public static class ProviderMessageProjection
{
    private const string OriginKey = "dotcraft.message.origin";

    public static void Stamp(ChatMessage message, ProviderMessageOrigin origin)
    {
        message.AdditionalProperties ??= new();
        message.AdditionalProperties[OriginKey] = JsonSerializer.SerializeToElement(origin);
    }

    public static ProviderMessageOrigin? ReadOrigin(ChatMessage message) =>
        message.AdditionalProperties?.TryGetValue(OriginKey, out var value) == true
            && value is JsonElement { ValueKind: JsonValueKind.Object } element
                ? element.Deserialize<ProviderMessageOrigin>()
                : null;

    public static IReadOnlyList<ChatMessage> Project(
        IReadOnlyList<ChatMessage> messages,
        ProviderMessageOrigin target)
    {
        List<ChatMessage>? projected = null;
        var callIds = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < messages.Count; i++)
        {
            var original = messages[i];
            ChatMessage? message = original;
            if (original.Role == ChatRole.Assistant)
            {
                var sameOrigin = ReadOrigin(original) == target;
                var contents = new List<AIContent>();
                foreach (var content in original.Contents)
                {
                    if (content is FunctionCallContent call)
                    {
                        var callId = sameOrigin ? call.CallId : NormalizeCallId(call.CallId);
                        if (sameOrigin)
                        {
                            callIds.Remove(call.CallId);
                            contents.Add(call);
                            continue;
                        }
                        callIds[call.CallId] = callId;
                        var mappedCall = new FunctionCallContent(callId, call.Name, call.Arguments)
                        {
                            InformationalOnly = call.InformationalOnly
                        };
                        if (ProviderFunctionCallMetadata.TryGetNamespace(call, out var toolNamespace))
                            mappedCall.AdditionalProperties = new() { [ProviderFunctionCallMetadata.NamespaceKey] = toolNamespace };
                        if (call.AdditionalProperties?.TryGetValue("dotcraft.tool.provider_flat_name", out var flatName) == true)
                        {
                            mappedCall.AdditionalProperties ??= new();
                            mappedCall.AdditionalProperties["dotcraft.tool.provider_flat_name"] = flatName;
                        }
                        if (ProviderFunctionCallMetadata.IsCustomToolCall(call))
                            ProviderFunctionCallMetadata.MarkCustomToolCall(mappedCall);
                        contents.Add(mappedCall);
                    }
                    else if (sameOrigin)
                        contents.Add(content);
                    else if (content is TextContent text)
                        contents.Add(new TextContent(text.Text));
                    else if (content is FunctionResultContent result)
                        contents.Add(ProjectResult(result, callIds));
                    else if (content is DataContent or UriContent)
                        contents.Add(content);
                }
                if (!sameOrigin)
                    message = contents.Count == 0 ? null : CopyMessage(original, contents);
            }
            else if (original.Contents.OfType<FunctionResultContent>()
                .Any(result => callIds.ContainsKey(result.CallId)))
            {
                message = CopyMessage(original,
                    original.Contents.Select(content => content is FunctionResultContent result
                        ? ProjectResult(result, callIds) : content).ToList());
            }

            if (!ReferenceEquals(message, original))
                projected ??= messages.Take(i).ToList();
            if (message is not null)
                projected?.Add(message);
        }
        return projected ?? messages;
    }

    private static FunctionResultContent ProjectResult(
        FunctionResultContent result, IReadOnlyDictionary<string, string> callIds) =>
        new(callIds.TryGetValue(result.CallId, out var id) ? id : result.CallId, result.Result)
        {
            Exception = result.Exception
        };

    private static ChatMessage CopyMessage(ChatMessage original, IList<AIContent> contents) =>
        new(original.Role, contents)
        {
            AuthorName = original.AuthorName,
            CreatedAt = original.CreatedAt
        };

    private static string NormalizeCallId(string id) =>
        id.Length is > 0 and <= 64 && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
            ? id
            : $"call_{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..48]}";
}
