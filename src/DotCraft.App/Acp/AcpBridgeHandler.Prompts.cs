using System.Text.Json;
using DotCraft.Sessions;
using Spectre.Console;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.Acp;

public sealed partial class AcpBridgeHandler
{
    private async Task HandleSessionPromptAsync(JsonRpcRequest request, CancellationToken ct)
    {
        if (!await EnsureInitialized(request)) return;

        var p = Deserialize<SessionPromptParams>(request.Params);
        if (p == null)
        {
            await acpTransport.SendErrorAsync(request.Id, -32602, "Invalid params");
            return;
        }

        var sessionId = p.SessionId;
        using var promptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!_activePrompts.TryAdd(sessionId, promptCts))
        {
            await acpTransport.SendErrorAsync(request.Id, -32600, "A prompt is already active for this session.");
            return;
        }

        var channelRegistered = false;
        var observing = false;
        try
        {
            var promptText = ExtractPromptText(p.Prompt);
            var commandExpanded = false;
            if (p.Command != null)
                promptText = $"/{p.Command} {promptText}".Trim();

            if (promptText.StartsWith('/'))
            {
                var expanded = commandRegistry?.TryResolvePromptExpansion(promptText);
                if (expanded != null)
                {
                    promptText = expanded;
                    commandExpanded = true;
                }
            }

            List<SessionInputPart> inputParts;
            if (commandExpanded)
            {
                inputParts = [new SessionInputPart { Type = "text", Text = promptText }];
            }
            else if (p.Command != null)
            {
                inputParts = [new SessionInputPart { Type = "text", Text = promptText }];
                foreach (var block in p.Prompt)
                {
                    if (!string.IsNullOrEmpty(block.Data) &&
                        !string.IsNullOrEmpty(block.MimeType) &&
                        block.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        inputParts.Add(new SessionInputPart
                        {
                            Type = "image",
                            Url = $"data:{block.MimeType};base64,{block.Data}"
                        });
                    }
                }
            }
            else
            {
                inputParts = BuildWireInputParts(p.Prompt);
            }

            logger?.LogEvent(
                $"Prompt start [session={sessionId}]: {(promptText.Length > 200 ? promptText[..200] + "..." : promptText)}");

            wire.RegisterThreadChannel(sessionId);
            channelRegistered = true;
            await ObserveSessionAsync(sessionId, ct);
            observing = true;
            using var resumeDoc = await wire.SendRequestAsync(
                Contract.AppServerMethodNames.ThreadResume, new { threadId = sessionId }, ct: promptCts.Token);
            ThrowIfWireError(resumeDoc, "thread/resume");
            using var startDoc = await wire.SendRequestAsync(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
            {
                threadId = sessionId,
                input = inputParts
            }, timeout: TimeSpan.FromSeconds(120), ct: promptCts.Token);

            ThrowIfWireError(startDoc, "turn/start");
            var turnId = startDoc.RootElement.GetProperty("result").GetProperty("turn").GetProperty("id").GetString()
                ?? throw new InvalidOperationException("turn/start returned no turn id");
            _activeTurnIds[sessionId] = turnId;

            var turnFailed = false;
            string? turnFailMessage = null;
            var turnCancelled = false;
            var turnEnded = false;
            await foreach (var notif in wire.ReadThreadTurnNotificationsAsync(sessionId, turnId, timeout: TimeSpan.FromHours(2),
                         ct: promptCts.Token))
            {
                using var notification = notif;
                if (!notif.RootElement.TryGetProperty("method", out var mEl))
                    continue;
                var method = mEl.GetString() ?? "";
                if (!notif.RootElement.TryGetProperty("params", out var @params))
                    continue;

                if (method == DotCraft.Protocol.AppServer.AppServerMethodNames.TurnFailed)
                {
                    turnFailed = true;
                    if (@params.TryGetProperty("error", out var err))
                        turnFailMessage = err.GetString();
                }
                else if (method == DotCraft.Protocol.AppServer.AppServerMethodNames.TurnCancelled)
                {
                    turnCancelled = true;
                }

                turnEnded |= method is Contract.AppServerMethodNames.TurnCompleted
                    or Contract.AppServerMethodNames.TurnFailed or Contract.AppServerMethodNames.TurnCancelled;
                await MapWireNotificationToAcp(sessionId, method, @params);
                // Let concurrent permission and tool requests acquire the transport write lock during streaming bursts.
                await Task.Yield();
            }

            promptCts.Token.ThrowIfCancellationRequested();
            if (!turnEnded)
                throw new IOException("The AppServer turn stream ended before the target Turn finished.");

            if (turnFailed)
            {
                await acpTransport.SendErrorAsync(request.Id, -32603, turnFailMessage ?? "Turn failed");
                return;
            }

            if (turnCancelled)
            {
                await acpTransport.SendResponseAsync(request.Id, new SessionPromptResult { StopReason = AcpStopReason.Cancelled });
                return;
            }

            if (planStore != null && planStore.StructuredPlanExists(sessionId))
            {
                var structuredPlan = await planStore.LoadStructuredPlanAsync(sessionId);
                if (structuredPlan != null)
                    await SendPlanUpdate(sessionId, structuredPlan);
            }

            logger?.LogEvent($"Prompt complete [session={sessionId}] stop=end_turn (AppServer wire)");
            await acpTransport.SendResponseAsync(request.Id, new SessionPromptResult { StopReason = AcpStopReason.EndTurn });
        }
        catch (OperationCanceledException)
        {
            logger?.LogEvent($"Prompt cancelled [session={sessionId}]");
            await acpTransport.SendResponseAsync(request.Id, new SessionPromptResult { StopReason = AcpStopReason.Cancelled });
        }
        catch (Exception ex)
        {
            logger?.LogError($"Prompt error [session={sessionId}]", ex);
            AnsiConsole.MarkupLine($"[red][[ACP]][/] Prompt error: {Markup.Escape(ex.Message)}");
            await acpTransport.SendErrorAsync(request.Id, -32603, ex.Message);
        }
        finally
        {
            if (channelRegistered)
                wire.UnregisterThreadChannel(sessionId);
            if (observing)
                await ReleaseSessionObservationAsync(sessionId);
            _activePrompts.TryRemove(sessionId, out _);
            _activeTurnIds.TryRemove(sessionId, out _);
        }
    }

}
