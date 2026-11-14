using System.Text.Json;
using DotCraft.CodexGateway.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.CodexGateway.Workspace;

internal sealed class CodexInteractiveBridge(
    WorkspaceRoutes routes,
    string workspace,
    CodexRequestBroker requests,
    Func<JsonElement, CancellationToken, Task> send)
{
    public async Task<ApprovalResponseResult> HandleApprovalAsync(ApprovalRequestParams request, CancellationToken ct)
    {
        var threadId = routes.External("thread", request.ThreadId);
        var turnId = routes.External("turn", request.ThreadId, request.TurnId);
        var key = request.CallId is { Length: > 0 } callId
            ? (request.ApprovalType == "shell" ? "command:" : "call:") + callId
            : "approval:" + request.RequestId;
        var itemId = routes.External("item", request.ThreadId, request.TurnId, key);
        var startedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string method;
        object parameters;
        var readPermission = false;

        switch (request.ApprovalType, request.Operation)
        {
            case ("shell", _):
                method = "item/commandExecution/requestApproval";
                parameters = new
                {
                    kind = "command", threadId, turnId, itemId, startedAtMs,
                    approvalId = request.RequestId, reason = request.Reason,
                    command = request.Operation, cwd = request.Target,
                    commandActions = Array.Empty<object>(),
                    availableDecisions = new[] { "accept", "decline", "cancel" }
                };
                break;
            case ("file", "write" or "edit" or "delete"):
                method = "item/fileChange/requestApproval";
                parameters = new
                {
                    threadId, turnId, itemId, startedAtMs,
                    reason = request.Reason, grantRoot = (string?)null
                };
                break;
            case ("file", "read" or "list"):
                method = "item/permissions/requestApproval";
                readPermission = true;
                parameters = new
                {
                    threadId, turnId, itemId, startedAtMs, cwd = workspace, reason = request.Reason,
                    permissions = new
                    {
                        network = (object?)null,
                        fileSystem = new
                        {
                            read = new[] { request.Target }, write = (string[]?)null,
                            entries = new[] { new { path = new { type = "path", path = request.Target }, access = "read" } }
                        }
                    }
                };
                break;
            default:
                await send(WorkspaceJson.Element(new
                {
                    method = "warning",
                    @params = new
                    {
                        threadId,
                        message = "codex_gateway.unsupported_approval: Answer this device/resource approval in DotCraft Desktop or cancel the turn."
                    }
                }), ct);
                throw new ArgumentException("codex_gateway.unsupported_approval: This non-coding approval has no Codex mapping.");
        }

        if (request.CallId is not { Length: > 0 })
        {
            var item = new { type = "agentMessage", id = itemId,
                text = $"DotCraft approval request: {request.Operation} — {request.Target}" };
            await send(WorkspaceJson.Element(new { method = "item/started", @params = new { threadId, turnId, item } }), ct);
            await send(WorkspaceJson.Element(new { method = "item/completed", @params = new { threadId, turnId, item } }), ct);
        }

        var response = await requests.RequestAsync(
            Logical("approval", request.ThreadId, request.TurnId, request.RequestId), method, parameters, ct);
        var decision = readPermission
            ? ReadPermissionDecision(response, request.Target)
            : response.Required("decision");
        if (decision is not ("accept" or "decline" or "cancel"))
            throw new ArgumentException("codex_gateway.unsupported_decision: Only one-time accept, decline and cancel are supported.");
        return new ApprovalResponseResult { Decision = decision };
    }

    public async Task<UserInputResponseResult> HandleUserInputAsync(UserInputRequestParams request, CancellationToken ct)
    {
        var result = await requests.RequestAsync(
            Logical("input", request.ThreadId, request.TurnId, request.RequestId),
            "item/tool/requestUserInput",
            new
            {
                threadId = routes.External("thread", request.ThreadId),
                turnId = routes.External("turn", request.ThreadId, request.TurnId),
                itemId = routes.External("item", request.ThreadId, request.TurnId, request.ItemId),
                questions = request.Questions.Select(question => new
                {
                    id = question.Id, header = question.Header, question = question.Question,
                    isOther = question.IsOther, isSecret = question.IsSecret,
                    options = question.Options.Select(option => new { label = option.Label, description = option.Description })
                }),
                isBlocking = request.IsBlocking
            }, ct);

        var answers = result.Get("answers");
        if (answers.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("codex_gateway.invalid_answers: Structured answers are required.");
        var known = request.Questions.Select(question => question.Id).ToHashSet(StringComparer.Ordinal);
        var mapped = new Dictionary<string, UserInputAnswer>(StringComparer.Ordinal);
        foreach (var answer in answers.EnumerateObject())
        {
            var values = answer.Value.Get("answers");
            if (!known.Contains(answer.Name) || values.ValueKind != JsonValueKind.Array ||
                values.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.String))
                throw new ArgumentException("codex_gateway.invalid_answers: Answer ids and values must match the request.");
            mapped.Add(answer.Name, new UserInputAnswer
            {
                Answers = values.EnumerateArray().Select(value => value.GetString()!).ToArray()
            });
        }
        return new UserInputResponseResult { Answers = mapped };
    }

    public Task ObserveAsync(string method, JsonElement parameters, CancellationToken ct)
    {
        var kind = method switch
        {
            "item/approval/resolved" => "approval",
            "item/tool/requestUserInput/resolved" => "input",
            _ => null
        };
        if (kind is null) return Task.CompletedTask;
        var thread = parameters.Required("threadId");
        var turn = parameters.Required("turnId");
        var request = parameters.Get("item").Get("payload").Text("requestId") ?? parameters.Text("requestId");
        return request is null ? Task.CompletedTask : requests.ResolveAsync(
            Logical(kind, thread, turn, request), routes.External("thread", thread), ct);
    }

    private string Logical(string kind, string thread, string turn, string request) =>
        JsonSerializer.Serialize(new[] { workspace, kind, thread, turn, request });

    private static string ReadPermissionDecision(JsonElement response, string target)
    {
        if (response.Text("scope") is { } scope && scope != "turn" || response.Flag("strictAutoReview"))
            throw new ArgumentException("codex_gateway.unsupported_decision: Session grants and review amendments are not supported.");
        var permissions = response.Get("permissions");
        if (permissions.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("codex_gateway.invalid_permissions: A granted permission profile is required.");
        var fs = permissions.Get("fileSystem");
        if (permissions.Get("network").Flag("enabled") || fs.Get("write").Rows().Any() ||
            fs.Get("globScanMaxDepth").ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
            throw new ArgumentException("codex_gateway.unsupported_decision: Only the requested read access can be granted.");
        var read = fs.Get("read").Rows().ToArray();
        var entries = fs.Get("entries").Rows().ToArray();
        if (read.Any(path => path.ValueKind != JsonValueKind.String || path.GetString() != target) ||
            entries.Any(entry => entry.Text("access") != "read" ||
                entry.Get("path").Text("type") != "path" || entry.Get("path").Text("path") != target))
            throw new ArgumentException("codex_gateway.unsupported_decision: The grant must match the requested path.");
        return read.Length + entries.Length == 0 ? "decline" : "accept";
    }
}
