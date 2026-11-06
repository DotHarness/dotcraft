using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAI001, MEAI001

namespace DotCraft.Agents;

internal static partial class ResponsesToolSearchMapper
{
    private static JsonObject CreateWebSearchItem(WebSearchToolResultContent result)
    {
        if (ResponsesWebSearchMetadata.TryRead(result, out var item))
            return item;
        if (result.RawRepresentation is WebSearchCallResponseItem raw)
            return ResponsesWebSearchMetadata.ReadNative(raw, result.CallId);
        throw ResponsesWebSearchMetadata.Invalid();
    }
}
