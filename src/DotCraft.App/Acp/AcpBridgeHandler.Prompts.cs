using System.Text;
using System.Text.Json;
using DotCraft.Sessions;
using DotHarness.Acp.Rpc;
using DotHarness.Acp.V1;
using Spectre.Console;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.Acp;

public sealed partial class AcpBridgeHandler
{
    public async ValueTask<PromptResponse> SessionPromptAsync(PromptRequest parameters, CancellationToken ct)
    {
        var sessionId = parameters.SessionId.Value;
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new AcpRpcException(-32602, "Invalid params: sessionId is required.");

        using var sessionCancelCts = new CancellationTokenSource();
        using var promptCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _bridgeLifetime, sessionCancelCts.Token);
        if (!_activePrompts.TryAdd(sessionId, sessionCancelCts))
            throw new AcpRpcException(-32600, "A prompt is already active for this session.");

        var channelRegistered = false;
        var observing = false;
        try
        {
            var promptBlocks = parameters.Prompt;
            var promptText = ExtractPromptText(promptBlocks);
            var commandExpanded = false;

            if (promptText.StartsWith('/'))
            {
                var expanded = commandRegistry?.TryResolvePromptExpansion(promptText);
                if (expanded != null)
                {
                    promptText = expanded;
                    commandExpanded = true;
                }
            }

            var inputParts = commandExpanded
                ? [new SessionInputPart { Type = "text", Text = promptText }]
                : BuildWireInputParts(promptBlocks);

            logger?.LogEvent(
                $"Prompt start [session={sessionId}]: {(promptText.Length > 200 ? promptText[..200] + "..." : promptText)}");

            wire.RegisterThreadChannel(sessionId);
            channelRegistered = true;
            await ObserveSessionAsync(sessionId, _bridgeLifetime).ConfigureAwait(false);
            observing = true;

            using var resumeDoc = await wire.SendRequestAsync(
                Contract.AppServerMethodNames.ThreadResume,
                new { threadId = sessionId },
                ct: promptCts.Token).ConfigureAwait(false);
            ThrowIfWireError(resumeDoc, "thread/resume");

            var startTurnTask = wire.SendRequestAsync(
                Contract.AppServerMethodNames.TurnStart,
                new
                {
                    threadId = sessionId,
                    input = inputParts
                },
                timeout: TimeSpan.FromSeconds(120),
                ct: _bridgeLifetime);

            JsonDocument startDoc;
            try
            {
                startDoc = await startTurnTask.WaitAsync(promptCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (sessionCancelCts.IsCancellationRequested || ct.IsCancellationRequested)
            {
                await InterruptPendingTurnStartAsync(sessionId, startTurnTask).ConfigureAwait(false);
                throw;
            }

            string turnId;
            using (startDoc)
            {
                ThrowIfWireError(startDoc, "turn/start");
                turnId = startDoc.RootElement.GetProperty("result").GetProperty("turn").GetProperty("id").GetString()
                    ?? throw new AcpRpcException(-32603, "turn/start returned no turn id");
                _activeTurnIds[sessionId] = turnId;
            }

            promptCts.Token.ThrowIfCancellationRequested();

            var turnFailed = false;
            string? turnFailMessage = null;
            var turnCancelled = false;
            var turnEnded = false;
            await foreach (var notif in wire.ReadThreadTurnNotificationsAsync(
                sessionId,
                turnId,
                timeout: TimeSpan.FromHours(2),
                ct: promptCts.Token).ConfigureAwait(false))
            {
                using var notification = notif;
                if (!notif.RootElement.TryGetProperty("method", out var mEl))
                    continue;
                var method = mEl.GetString() ?? "";
                if (!notif.RootElement.TryGetProperty("params", out var @params))
                    continue;

                if (method == Contract.AppServerMethodNames.TurnFailed)
                {
                    turnFailed = true;
                    if (@params.TryGetProperty("error", out var err))
                        turnFailMessage = err.GetString();
                }
                else if (method == Contract.AppServerMethodNames.TurnCancelled)
                {
                    turnCancelled = true;
                }

                turnEnded |= method is Contract.AppServerMethodNames.TurnCompleted
                    or Contract.AppServerMethodNames.TurnFailed
                    or Contract.AppServerMethodNames.TurnCancelled;
                await MapWireNotificationToAcpAsync(sessionId, method, @params, promptCts.Token).ConfigureAwait(false);
                await Task.Yield();
            }

            promptCts.Token.ThrowIfCancellationRequested();
            if (!turnEnded)
                throw new IOException("The AppServer turn stream ended before the target Turn finished.");

            if (turnFailed)
                throw new AcpRpcException(-32603, turnFailMessage ?? "Turn failed");

            if (turnCancelled)
                return new PromptResponse { StopReason = StopReason.Cancelled };

            if (planStore != null && planStore.StructuredPlanExists(sessionId))
            {
                var structuredPlan = await planStore.LoadStructuredPlanAsync(sessionId).ConfigureAwait(false);
                if (structuredPlan != null)
                    await SendPlanUpdateAsync(sessionId, structuredPlan, ct).ConfigureAwait(false);
            }

            logger?.LogEvent($"Prompt complete [session={sessionId}] stop=end_turn (AppServer wire)");
            return new PromptResponse { StopReason = StopReason.EndTurn };
        }
        catch (OperationCanceledException) when (sessionCancelCts.IsCancellationRequested)
        {
            await TryInterruptActiveTurnAsync(sessionId).ConfigureAwait(false);
            logger?.LogEvent($"Prompt cancelled [session={sessionId}]");
            return new PromptResponse { StopReason = StopReason.Cancelled };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await TryInterruptActiveTurnAsync(sessionId).ConfigureAwait(false);
            throw;
        }
        catch (AcpRpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogError($"Prompt error [session={sessionId}]", ex);
            AnsiConsole.MarkupLine($"[red][[ACP]][/] Prompt error: {Markup.Escape(ex.Message)}");
            throw new AcpRpcException(-32603, ex.Message);
        }
        finally
        {
            if (channelRegistered)
                wire.UnregisterThreadChannel(sessionId);
            if (observing)
                await ReleaseSessionObservationAsync(sessionId).ConfigureAwait(false);
            _activePrompts.TryRemove(sessionId, out _);
            _activeTurnIds.TryRemove(sessionId, out _);
        }
    }

    public async ValueTask SessionCancelAsync(CancelNotification parameters, CancellationToken ct)
    {
        var sessionId = parameters.SessionId.Value;
        if (string.IsNullOrWhiteSpace(sessionId))
            return;

        if (_activePrompts.TryGetValue(sessionId, out var cts))
        {
            try { await cts.CancelAsync().ConfigureAwait(false); }
            catch (ObjectDisposedException) { }
        }

        await TryInterruptActiveTurnAsync(sessionId, ct).ConfigureAwait(false);

        logger?.LogEvent($"Session cancel requested: {sessionId}");
        AnsiConsole.MarkupLine($"[yellow][[ACP]][/] Cancelled session: {Markup.Escape(sessionId)}");
    }

    private async Task InterruptPendingTurnStartAsync(string sessionId, Task<JsonDocument> startTurnTask)
    {
        try
        {
            using var interruptCts = CancellationTokenSource.CreateLinkedTokenSource(_bridgeLifetime);
            interruptCts.CancelAfter(TimeSpan.FromSeconds(10));
            using var startDoc = await startTurnTask.WaitAsync(interruptCts.Token).ConfigureAwait(false);
            if (!startDoc.RootElement.TryGetProperty("result", out var result)
                || !result.TryGetProperty("turn", out var turn)
                || !turn.TryGetProperty("id", out var idEl)
                || idEl.ValueKind != JsonValueKind.String)
            {
                return;
            }

            var turnId = idEl.GetString();
            if (!string.IsNullOrEmpty(turnId))
                await InterruptTurnAsync(sessionId, turnId, interruptCts.Token).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task TryInterruptActiveTurnAsync(string sessionId, CancellationToken ct = default)
    {
        if (!_activeTurnIds.TryRemove(sessionId, out var turnId) || string.IsNullOrEmpty(turnId))
            return;

        using var interruptCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _bridgeLifetime);
        interruptCts.CancelAfter(TimeSpan.FromSeconds(10));
        await InterruptTurnAsync(sessionId, turnId, interruptCts.Token).ConfigureAwait(false);
    }

    private async Task InterruptTurnAsync(string sessionId, string turnId, CancellationToken ct)
    {
        try
        {
            using var _ = await wire.SendRequestAsync(
                Contract.AppServerMethodNames.TurnInterrupt,
                new { threadId = sessionId, turnId },
                ct: ct).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static List<SessionInputPart> BuildWireInputParts(ContentBlock[] prompt)
    {
        var parts = new List<SessionInputPart>();
        foreach (var block in prompt)
        {
            switch (block)
            {
                case ContentBlock.Text text when !string.IsNullOrEmpty(text.Value.Text):
                    parts.Add(new SessionInputPart { Type = "text", Text = text.Value.Text });
                    break;
                case ContentBlock.Image image
                    when !string.IsNullOrEmpty(image.Value.Data)
                         && image.Value.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase):
                    parts.Add(new SessionInputPart
                    {
                        Type = "image",
                        Url = $"data:{image.Value.MimeType};base64,{image.Value.Data}"
                    });
                    break;
                case ContentBlock.ResourceLink link:
                {
                    var t = $"[File: {link.Value.Uri}]";
                    if (link.Value.Description.GetValueOrDefault() is { Length: > 0 } desc)
                        t += "\n" + desc;
                    parts.Add(new SessionInputPart { Type = "text", Text = t });
                    break;
                }
                case ContentBlock.Resource resource when resource.Value.Resource is EmbeddedResourceResource.TextResourceContents textRes:
                {
                    var t = $"[File: {textRes.Value.Uri}]";
                    if (!string.IsNullOrEmpty(textRes.Value.Text))
                        t += "\n" + textRes.Value.Text;
                    parts.Add(new SessionInputPart { Type = "text", Text = t });
                    break;
                }
                case ContentBlock.Resource resource when resource.Value.Resource is EmbeddedResourceResource.BlobResourceContents blobRes:
                {
                    var blob = blobRes.Value;
                    var mimeType = blob.MimeType.GetValueOrDefault();
                    if (!string.IsNullOrEmpty(blob.Blob)
                        && !string.IsNullOrEmpty(mimeType)
                        && mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        parts.Add(new SessionInputPart
                        {
                            Type = "image",
                            Url = $"data:{mimeType};base64,{blob.Blob}"
                        });
                    }
                    else
                    {
                        parts.Add(new SessionInputPart { Type = "text", Text = $"[File: {blob.Uri}]" });
                    }
                    break;
                }
            }
        }

        if (parts.Count == 0 && prompt.Length > 0)
            parts.Add(new SessionInputPart { Type = "text", Text = ExtractPromptText(prompt) });

        return parts;
    }

    private static string ExtractPromptText(ContentBlock[] prompt)
    {
        if (prompt.Length == 0)
            return "";

        var sb = new StringBuilder();
        foreach (var block in prompt)
        {
            switch (block)
            {
                case ContentBlock.Text text when !string.IsNullOrEmpty(text.Value.Text):
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(text.Value.Text);
                    break;
                case ContentBlock.ResourceLink link:
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append($"[File: {link.Value.Uri}]");
                    break;
                case ContentBlock.Resource resource when resource.Value.Resource is EmbeddedResourceResource.TextResourceContents textRes:
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append($"[File: {textRes.Value.Uri}]");
                    if (!string.IsNullOrEmpty(textRes.Value.Text))
                    {
                        sb.Append('\n');
                        sb.Append(textRes.Value.Text);
                    }
                    break;
                case ContentBlock.Resource resource when resource.Value.Resource is EmbeddedResourceResource.BlobResourceContents blobRes:
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append($"[File: {blobRes.Value.Uri}]");
                    break;
            }
        }

        return sb.ToString();
    }
}
