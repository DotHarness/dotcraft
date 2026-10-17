using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

public sealed partial class ThreadStore
{
    private static bool TryBuildWebSearchToolHistory(
        SessionItem item,
        out FunctionCallContent call,
        out ChatMessage resultMessage)
    {
        call = new FunctionCallContent(string.Empty, string.Empty);
        resultMessage = new ChatMessage(ChatRole.Tool, string.Empty);
        if (item.AsWebSearch is not { } payload ||
            string.IsNullOrWhiteSpace(payload.CallId) ||
            string.Equals(payload.Status, "inProgress", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        call = CreatePersistedFunctionCall(
            payload.CallId,
            WebSearchToolSource.ToolNamespace,
            WebSearchToolSource.ToolName,
            WebSearchToolSource.ToolNamespace + "__" + WebSearchToolSource.ToolName,
            payload.Arguments);
        var output = payload.Output
                     ?? $"web search failed: {payload.ErrorMessage ?? "Web search returned no output."}";
        resultMessage = new ChatMessage(
            ChatRole.Tool,
            (IList<AIContent>)[new FunctionResultContent(payload.CallId, output)]);
        return true;
    }
}
