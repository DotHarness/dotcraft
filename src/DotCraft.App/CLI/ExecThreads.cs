using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.AppServer;
using DotCraft.Protocol.AppServer;

namespace DotCraft.CLI;

internal static class ExecThreads
{
    private const string AutoApprovePolicy = "autoApprove";

    public static async Task<string> StartAsync(AppServerWireClient wire, ExecRequest request, CancellationToken ct)
    {
        var result = await RequestAsync(wire, AppServerMethodNames.ThreadStart, new
        {
            identity = Identity(request.WorkspacePath),
            config = request.Model is null && !request.AutoApprove
                ? null
                : new
                {
                    model = request.Model,
                    approvalPolicy = request.AutoApprove ? AutoApprovePolicy : null
                },
            historyMode = "server",
            ephemeral = request.Ephemeral ? true : (bool?)null
        }, ct).ConfigureAwait(false);

        return result.GetProperty("thread").GetProperty("id").GetString()
            ?? throw new InvalidOperationException("AppServer returned a thread with no id.");
    }

    public static async Task<string> FindLatestAsync(AppServerWireClient wire, string workspacePath, CancellationToken ct)
    {
        var result = await RequestAsync(wire, AppServerMethodNames.ThreadList, new
        {
            identity = Identity(workspacePath),
            scope = "workspace",
            includeArchived = false,
            includeSubAgents = false,
            limit = 1
        }, ct).ConfigureAwait(false);

        if (result.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Array
            && data.GetArrayLength() > 0
            && data[0].TryGetProperty("id", out var id)
            && id.GetString() is { Length: > 0 } threadId)
        {
            return threadId;
        }

        throw new InvalidOperationException("No thread found in this workspace.");
    }

    public static async Task UpdateConfigAsync(
        AppServerWireClient wire,
        string threadId,
        string? model,
        bool autoApprove,
        CancellationToken ct)
    {
        var thread = await ReadAsync(wire, threadId, ct).ConfigureAwait(false);
        var config = thread.TryGetProperty("configuration", out var configuration)
                     && configuration.ValueKind == JsonValueKind.Object
            ? JsonNode.Parse(configuration.GetRawText())!.AsObject()
            : new JsonObject();

        if (model != null)
        {
            config["model"] = model;
            config.Remove("reasoning");
            config.Remove("speed");
        }

        if (autoApprove)
            config["approvalPolicy"] = AutoApprovePolicy;

        await RequestAsync(wire, AppServerMethodNames.ThreadConfigUpdate, new { threadId, config }, ct).ConfigureAwait(false);
    }

    public static async Task<JsonElement> ReadAsync(AppServerWireClient wire, string threadId, CancellationToken ct)
    {
        var result = await RequestAsync(wire, AppServerMethodNames.ThreadRead, new { threadId }, ct).ConfigureAwait(false);
        return result.TryGetProperty("thread", out var thread) && thread.ValueKind == JsonValueKind.Object
            ? thread
            : throw new InvalidOperationException($"AppServer returned no thread for '{threadId}'.");
    }

    public static async Task<string> StartTurnAsync(AppServerWireClient wire, string threadId, ExecRequest request, CancellationToken ct)
    {
        var input = new List<object> { new { type = "text", text = request.Prompt } };
        input.AddRange(request.Images.Select(path => new { type = "localImage", path }));

        var result = await RequestAsync(wire, AppServerMethodNames.TurnStart, new { threadId, input }, ct).ConfigureAwait(false);
        var turnId = result.GetProperty("turn").GetProperty("id").GetString();
        return string.IsNullOrWhiteSpace(turnId)
            ? throw new InvalidOperationException("AppServer returned a turn with no id.")
            : turnId;
    }

    private static object Identity(string workspacePath) => new
    {
        channelName = "cli",
        userId = "local",
        channelContext = (string?)null,
        workspacePath
    };

    public static Task InterruptTurnAsync(AppServerWireClient wire, string threadId, string turnId, CancellationToken ct) =>
        RequestAsync(wire, AppServerMethodNames.TurnInterrupt, new { threadId, turnId }, ct);

    private static async Task<JsonElement> RequestAsync(
        AppServerWireClient wire,
        string method,
        object @params,
        CancellationToken ct)
    {
        using var response = await wire.SendRequestAsync(method, @params, ct: ct).ConfigureAwait(false);
        if (response.RootElement.TryGetProperty("error", out var error))
        {
            var message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString()
                : null;
            var code = error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var value) ? value : 0;
            throw new ExecRequestException(code, string.IsNullOrWhiteSpace(message)
                ? $"AppServer request '{method}' failed."
                : message);
        }

        return response.RootElement.GetProperty("result").Clone();
    }
}

internal sealed class ExecRequestException(int code, string message) : InvalidOperationException(message)
{
    public int Code { get; } = code;
}
