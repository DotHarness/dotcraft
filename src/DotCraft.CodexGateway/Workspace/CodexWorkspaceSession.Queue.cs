using System.Globalization;
using System.Text.Json;

namespace DotCraft.CodexGateway.Workspace;

public sealed partial class CodexWorkspaceSession
{
    private async Task<object> ListQueueAsync(JsonElement parameters, CancellationToken ct)
    {
        var thread = ThreadId(parameters);
        var cursor = parameters.Get("cursor");
        var offset = 0;
        if (cursor.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) &&
            (cursor.ValueKind != JsonValueKind.String ||
             !int.TryParse(cursor.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out offset)))
            throw new ArgumentException("Invalid queue pagination cursor.");

        var pageSize = parameters.Get("limit");
        var limit = 25;
        if (pageSize.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
        {
            if (pageSize.ValueKind != JsonValueKind.Number || !pageSize.TryGetUInt32(out var requestedLimit))
                throw new ArgumentException("'limit' must be a nonnegative integer.");
            limit = (int)Math.Clamp(requestedLimit, 1u, 100u);
        }

        var response = await RequestAsync("thread/read", new { threadId = thread }, ct);
        var source = response.Get("thread");
        if (source.Flag("ephemeral"))
            throw new ArgumentException("Ephemeral threads do not support queued submissions.");

        var page = source.GetProperty("queuedInputs").EnumerateArray()
            .Where(input => input.Text("status") == "queued" && input.Text("triggerKind") is null)
            .Skip(offset).Take(limit + 1).ToArray();
        return new
        {
            data = page.Take(limit).Select(input => _projector.QueuedSubmission(thread, input)).ToArray(),
            nextCursor = page.Length > limit ? (offset + limit).ToString(CultureInfo.InvariantCulture) : null
        };
    }
}
