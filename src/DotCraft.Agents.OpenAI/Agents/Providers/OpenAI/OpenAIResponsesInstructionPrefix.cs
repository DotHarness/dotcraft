using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenAI.Responses;

#pragma warning disable OPENAI001, SCME0001

namespace DotCraft.Agents;

internal static class OpenAIResponsesInstructionPrefix
{
    private const string IdentityDomain = "openai.responses.base_instructions.v1";

    internal static string? ResolveInstructions(
        CreateResponseOptions options, string? mappedInstructions, CreateResponseOptions? source)
    {
        var patched = source is not null
                      && (source.Patch.Contains("$.instructions"u8) || source.Patch.Contains("$"u8));
        var instructions = options.Instructions;
        if (patched)
            instructions = source!.Patch.TryGetValue("$.instructions"u8, out string? value) ? value : null;
        else if (!string.IsNullOrWhiteSpace(mappedInstructions))
            instructions = string.IsNullOrWhiteSpace(instructions)
                ? mappedInstructions
                : instructions + Environment.NewLine + mappedInstructions;

        options.Instructions = null;
        options.Patch.Remove("$.instructions"u8);
        return string.IsNullOrWhiteSpace(instructions) ? null : instructions;
    }

    internal static JsonObject Create(string instructions)
    {
        var threadId = OpenAIResponsesCodexMetadata.ResolveRoutingIdentity().ThreadId ?? string.Empty;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new[] { IdentityDomain, threadId, instructions });
        var suffix = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()[..32];
        return new JsonObject
        {
            ["id"] = $"msg_{suffix}",
            ["type"] = "message",
            ["role"] = "developer",
            ["content"] = new JsonArray
            {
                new JsonObject { ["type"] = "input_text", ["text"] = instructions }
            }
        };
    }
}
