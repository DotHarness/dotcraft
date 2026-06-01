// Maps incoming Wire Protocol notifications to AppState mutations.
// Implements §6 of specs/clients/tui-client.md.
//
// StreamCollector integration:
//   The event_mapper appends deltas to `streaming.message_buffer`.
//   The actual StreamCollector (which needs Theme + width) lives in lib.rs and is
//   driven from the event loop after each delta to produce pre-rendered lines.
//   This keeps event_mapper free of rendering dependencies.

use crate::{
    app::state::{
        ActiveCommandExecution, ActiveToolCall, AppState, HistoryEntry, PlanSnapshot, PlanTodo,
        StreamingState, SubAgentEntry, SystemStatusInfo, TurnStatus,
    },
    wire::types::JsonRpcMessage,
};
// Note: ApprovalState is parsed in lib.rs handle_server_request (needs the JSON-RPC request id).
// event_mapper only handles the resolved notification here.

fn payload_or_item<'a>(item: &'a serde_json::Value) -> &'a serde_json::Value {
    item.get("payload").unwrap_or(item)
}

fn stringify_json_arg(value: &serde_json::Value) -> String {
    if value.is_string() {
        value.as_str().unwrap_or("").to_string()
    } else {
        serde_json::to_string(value).unwrap_or_default()
    }
}

fn tool_started_fields(item: &serde_json::Value, item_type: &str) -> (String, String, String) {
    let payload = payload_or_item(item);
    let call_id = payload
        .get("callId")
        .and_then(|v| v.as_str())
        .unwrap_or("")
        .to_string();
    let tool_name_field = if item_type == "pluginFunctionCall" {
        "functionName"
    } else {
        "toolName"
    };
    let tool_name = payload
        .get(tool_name_field)
        .or_else(|| payload.get("toolName"))
        .or_else(|| payload.get("functionName"))
        .and_then(|v| v.as_str())
        .unwrap_or("")
        .to_string();
    let arguments = payload
        .get("arguments")
        .map(stringify_json_arg)
        .unwrap_or_default();
    (call_id, tool_name, arguments)
}

fn structured_invocation_result_text(payload: &serde_json::Value) -> Option<String> {
    if let Some(items) = payload.get("contentItems").and_then(|v| v.as_array()) {
        let mut parts = Vec::new();
        for item in items {
            match item.get("type").and_then(|v| v.as_str()).unwrap_or("text") {
                "text" => {
                    if let Some(text) = item.get("text").and_then(|v| v.as_str()) {
                        if !text.is_empty() {
                            parts.push(text.to_string());
                        }
                    }
                }
                "image" => {
                    let media_type = item
                        .get("mediaType")
                        .and_then(|v| v.as_str())
                        .unwrap_or("image");
                    parts.push(format!("[image: {media_type}]"));
                }
                _ => {}
            }
        }
        if !parts.is_empty() {
            return Some(parts.join("\n"));
        }
    }

    if let Some(structured) = payload.get("structuredResult") {
        if !structured.is_null() {
            return Some(
                serde_json::to_string_pretty(structured).unwrap_or_else(|_| structured.to_string()),
            );
        }
    }

    payload
        .get("errorMessage")
        .and_then(|v| v.as_str())
        .map(str::to_string)
}

fn is_run_in_background_terminal(terminal: &serde_json::Value) -> bool {
    terminal
        .get("backgroundReason")
        .and_then(|v| v.as_str())
        == Some("runInBackground")
}

fn terminal_duration(terminal: &serde_json::Value) -> Option<std::time::Duration> {
    terminal
        .get("wallTimeMs")
        .and_then(|v| v.as_u64())
        .map(std::time::Duration::from_millis)
}

fn terminal_exit_code(terminal: &serde_json::Value) -> Option<i32> {
    terminal
        .get("exitCode")
        .and_then(|v| v.as_i64())
        .and_then(|v| i32::try_from(v).ok())
}

fn merge_shell_output_by_call_id(
    state: &mut AppState,
    call_id: &str,
    output: Option<String>,
    exit_code: Option<i32>,
    duration: Option<std::time::Duration>,
    status: Option<&str>,
) {
    let failed_status = matches!(status, Some("failed" | "killed" | "timedOut" | "lost"));

    if let Some(tool) = state
        .streaming
        .active_tools
        .iter_mut()
        .find(|t| t.call_id == call_id)
    {
        if let Some(ref out) = output {
            if !out.is_empty() {
                tool.result = Some(out.clone());
            }
        }
        if let Some(d) = duration {
            tool.duration = Some(d);
        }
        if let Some(code) = exit_code {
            tool.success = code == 0;
        } else if failed_status {
            tool.success = false;
        }
    }

    for entry in state.history.iter_mut().rev() {
        if let HistoryEntry::ToolCall {
            call_id: ref id,
            result: ref mut r,
            success: ref mut s,
            duration: ref mut d,
            ..
        } = entry
        {
            if id == call_id {
                if let Some(ref out) = output {
                    if !out.is_empty() {
                        *r = Some(out.clone());
                    }
                }
                if let Some(code) = exit_code {
                    *s = code == 0;
                } else if failed_status {
                    *s = false;
                }
                if duration.is_some() {
                    *d = duration;
                }
                break;
            }
        }
    }
}

/// Process one incoming wire message and mutate AppState accordingly.
/// Returns true if the message was handled, false if it was unknown/ignored.
pub fn apply(state: &mut AppState, msg: &JsonRpcMessage) -> bool {
    let method = match &msg.method {
        Some(m) => m.as_str(),
        None => return false, // Response message; handled separately by client.
    };

    let params = msg.params.as_ref().unwrap_or(&serde_json::Value::Null);

    match method {
        // ── Turn lifecycle ────────────────────────────────────────────────
        "turn/started" => {
            state.current_turn_id = params
                .get("turn")
                .and_then(|t| t.get("id"))
                .and_then(|v| v.as_str())
                .map(|s| s.to_string());
            state.turn_status = TurnStatus::Running;
            state.turn_started_at = Some(std::time::Instant::now());
            state.streaming.clear();
            state.subagent_entries.clear();
            state.last_subagent_entries.clear();
            state.token_tracker.reset();
            // Auto-scroll to bottom on new turn start.
            state.at_bottom = true;
            true
        }
        "turn/completed" => {
            finalize_streaming(state);
            state.turn_status = TurnStatus::Idle;
            state.current_turn_id = None;
            state.turn_started_at = None;
            state.pending_user_input = None;
            if !state.subagent_entries.is_empty() {
                state.last_subagent_entries = state.subagent_entries.clone();
            }
            if let Some(usage) = params.get("turn").and_then(|t| t.get("tokenUsage")) {
                if let (Some(inp), Some(out)) = (
                    usage.get("inputTokens").and_then(|v| v.as_i64()),
                    usage.get("outputTokens").and_then(|v| v.as_i64()),
                ) {
                    // Only use turn/completed tokenUsage if no item/usage/delta events
                    // were received (fallback for servers that don't send deltas).
                    // When deltas are received, the tracker already holds the correct total
                    // (per appserver-protocol.md §6.6: sum of deltas == tokenUsage).
                    if state.token_tracker.input_tokens == 0
                        && state.token_tracker.output_tokens == 0
                    {
                        state.token_tracker.add(inp, out);
                    }
                }
            }
            true
        }
        "turn/failed" => {
            let err = params
                .get("error")
                .and_then(|v| v.as_str())
                .unwrap_or("unknown error")
                .to_string();
            finalize_streaming(state);
            state.history.push(HistoryEntry::Error { message: err });
            state.turn_status = TurnStatus::Idle;
            state.current_turn_id = None;
            state.turn_started_at = None;
            state.pending_user_input = None;
            true
        }
        "turn/cancelled" => {
            finalize_streaming(state);
            state.turn_status = TurnStatus::Idle;
            state.current_turn_id = None;
            state.turn_started_at = None;
            state.pending_user_input = None;
            true
        }

        // ── Item lifecycle ────────────────────────────────────────────────
        "item/started" => {
            let item = params.get("item").unwrap_or(&serde_json::Value::Null);
            let item_type = item.get("type").and_then(|v| v.as_str()).unwrap_or("");
            match item_type {
                "approvalRequest" => {
                    // Set turn status; the actual ApprovalState is built in
                    // lib.rs::handle_server_request when item/approval/request arrives.
                    state.turn_status = TurnStatus::WaitingApproval;
                }
                "userInputRequest" => {
                    state.turn_status = TurnStatus::WaitingInput;
                }
                "toolCall" | "pluginFunctionCall" | "dynamicToolCall" => {
                    let (call_id, tool_name, arguments) = tool_started_fields(item, item_type);
                    state.streaming.active_tools.push(ActiveToolCall {
                        call_id,
                        tool_name,
                        arguments,
                        completed: false,
                        result: None,
                        success: true,
                        started_at: std::time::Instant::now(),
                        duration: None,
                    });
                }
                "commandExecution" => {
                    let payload = item.get("payload").unwrap_or(item);
                    let item_id = item
                        .get("id")
                        .and_then(|v| v.as_str())
                        .unwrap_or("")
                        .to_string();
                    if item_id.is_empty() {
                        return true;
                    }
                    let call_id = payload
                        .get("callId")
                        .and_then(|v| v.as_str())
                        .map(str::to_string);
                    let command = payload
                        .get("command")
                        .and_then(|v| v.as_str())
                        .unwrap_or("")
                        .to_string();
                    let working_directory = payload
                        .get("workingDirectory")
                        .and_then(|v| v.as_str())
                        .map(str::to_string);
                    let source = payload
                        .get("source")
                        .and_then(|v| v.as_str())
                        .map(str::to_string);
                    let status = payload
                        .get("status")
                        .and_then(|v| v.as_str())
                        .unwrap_or("inProgress")
                        .to_string();
                    let aggregated_output = payload
                        .get("aggregatedOutput")
                        .and_then(|v| v.as_str())
                        .unwrap_or("")
                        .to_string();

                    if let Some(exec) = state
                        .streaming
                        .active_command_executions
                        .iter_mut()
                        .find(|e| e.item_id == item_id)
                    {
                        exec.call_id = call_id;
                        exec.command = command;
                        exec.working_directory = working_directory;
                        exec.source = source;
                        exec.status = status;
                        if !aggregated_output.is_empty() {
                            exec.aggregated_output = aggregated_output;
                        }
                    } else {
                        state
                            .streaming
                            .active_command_executions
                            .push(ActiveCommandExecution {
                                item_id,
                                call_id,
                                command,
                                working_directory,
                                source,
                                aggregated_output,
                                completed: false,
                                started_at: std::time::Instant::now(),
                                duration: None,
                                exit_code: None,
                                status,
                            });
                    }
                }
                _ => {}
            }
            true
        }

        "item/agentMessage/delta" => {
            if let Some(delta) = params.get("delta").and_then(|v| v.as_str()) {
                state.streaming.message_buffer.push_str(delta);
                state.streaming.is_reasoning = false;
                // Note: StreamCollector is updated in lib.rs after this returns,
                // since it needs Theme + width for rendering.
                state.at_bottom = true; // auto-scroll while streaming
            }
            true
        }

        "item/reasoning/delta" => {
            if let Some(delta) = params.get("delta").and_then(|v| v.as_str()) {
                state.streaming.reasoning_buffer.push_str(delta);
                state.streaming.is_reasoning = true;
                state.at_bottom = true;
            }
            true
        }

        "item/toolCall/argumentsDelta" => {
            let delta = params
                .get("delta")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string();
            if delta.is_empty() {
                return true;
            }

            let call_id = params
                .get("callId")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string();
            let tool_name = params
                .get("toolName")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string();

            if !call_id.is_empty() {
                if let Some(tool) = state
                    .streaming
                    .active_tools
                    .iter_mut()
                    .find(|t| t.call_id == call_id)
                {
                    tool.arguments.push_str(&delta);
                } else {
                    state.streaming.active_tools.push(ActiveToolCall {
                        call_id,
                        tool_name,
                        arguments: delta,
                        completed: false,
                        result: None,
                        success: true,
                        started_at: std::time::Instant::now(),
                        duration: None,
                    });
                }
            } else if let Some(tool) = state
                .streaming
                .active_tools
                .iter_mut()
                .rev()
                .find(|t| !t.completed)
            {
                if tool.tool_name.is_empty() && !tool_name.is_empty() {
                    tool.tool_name = tool_name;
                }
                tool.arguments.push_str(&delta);
            }

            true
        }

        "item/commandExecution/outputDelta" => {
            let item_id = params
                .get("itemId")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string();
            let delta = params
                .get("delta")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string();
            if item_id.is_empty() || delta.is_empty() {
                return true;
            }

            if let Some(exec) = state
                .streaming
                .active_command_executions
                .iter_mut()
                .find(|e| e.item_id == item_id)
            {
                exec.aggregated_output.push_str(&delta);
                state.at_bottom = true;
                let output_for_merge = exec.aggregated_output.clone();
                let call_id_for_merge = exec.call_id.clone();

                if let Some(call_id) = call_id_for_merge.as_deref() {
                    if let Some(tool) = state
                        .streaming
                        .active_tools
                        .iter_mut()
                        .find(|t| t.call_id == call_id)
                    {
                        tool.result = Some(output_for_merge.clone());
                    }

                    for entry in state.history.iter_mut().rev() {
                        if let HistoryEntry::ToolCall {
                            call_id: ref id,
                            result: ref mut r,
                            ..
                        } = entry
                        {
                            if id == call_id {
                                *r = Some(output_for_merge.clone());
                                break;
                            }
                        }
                    }
                }
            }

            true
        }

        "terminal/started" | "terminal/outputDelta" | "terminal/completed" | "terminal/stalled"
        | "terminal/cleaned" => {
            let terminal = params.get("terminal").unwrap_or(&serde_json::Value::Null);
            if method == "terminal/cleaned" || is_run_in_background_terminal(terminal) {
                return true;
            }

            let session_id = terminal
                .get("sessionId")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string();
            let call_id = terminal
                .get("callId")
                .and_then(|v| v.as_str())
                .map(str::to_string);
            let Some(call_id_value) = call_id.clone() else {
                return true;
            };
            if session_id.is_empty() {
                return true;
            }

            let command = terminal
                .get("command")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string();
            let working_directory = terminal
                .get("workingDirectory")
                .and_then(|v| v.as_str())
                .map(str::to_string);
            let source = terminal
                .get("source")
                .and_then(|v| v.as_str())
                .map(str::to_string);
            let status = terminal
                .get("status")
                .and_then(|v| v.as_str())
                .unwrap_or("running")
                .to_string();
            let snapshot_output = terminal
                .get("output")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string();
            let delta = params
                .get("delta")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string();
            let exit_code = terminal_exit_code(terminal);
            let duration = terminal_duration(terminal);
            let completed = method == "terminal/completed"
                || matches!(
                    status.as_str(),
                    "completed" | "failed" | "killed" | "timedOut" | "lost"
                );

            let output_for_merge;
            let existing_index = state
                .streaming
                .active_command_executions
                .iter()
                .position(|e| {
                    e.item_id == session_id || e.call_id.as_deref() == Some(call_id_value.as_str())
                });

            if let Some(index) = existing_index {
                let exec = &mut state.streaming.active_command_executions[index];
                exec.call_id = Some(call_id_value.clone());
                if !command.is_empty() {
                    exec.command = command;
                }
                exec.working_directory = working_directory;
                exec.source = source;
                exec.status = status.clone();
                exec.exit_code = exit_code;
                exec.duration = duration.or_else(|| Some(exec.started_at.elapsed()));
                exec.completed = completed;
                if !delta.is_empty() {
                    exec.aggregated_output.push_str(&delta);
                } else if !snapshot_output.is_empty() {
                    exec.aggregated_output = snapshot_output;
                }
                output_for_merge = exec.aggregated_output.clone();
            } else {
                let aggregated_output = if !delta.is_empty() {
                    delta
                } else {
                    snapshot_output
                };
                output_for_merge = aggregated_output.clone();
                state
                    .streaming
                    .active_command_executions
                    .push(ActiveCommandExecution {
                        item_id: session_id.clone(),
                        call_id: Some(call_id_value.clone()),
                        command,
                        working_directory,
                        source,
                        aggregated_output,
                        completed,
                        started_at: std::time::Instant::now(),
                        duration,
                        exit_code,
                        status: status.clone(),
                    });
            }

            if !output_for_merge.is_empty() {
                state.at_bottom = true;
            }
            merge_shell_output_by_call_id(
                state,
                &call_id_value,
                Some(output_for_merge),
                exit_code,
                duration,
                Some(status.as_str()),
            );

            if completed {
                state
                    .streaming
                    .active_command_executions
                    .retain(|e| e.item_id != session_id);
            }

            true
        }

        "item/completed" => {
            let item = params.get("item").unwrap_or(&serde_json::Value::Null);
            let item_type = item.get("type").and_then(|v| v.as_str()).unwrap_or("");
            match item_type {
                "agentMessage" => {
                    let text = std::mem::take(&mut state.streaming.message_buffer);
                    if !text.is_empty() {
                        state.history.push(HistoryEntry::AgentMessage { text });
                    }
                }
                "commandExecution" => {
                    let payload = item.get("payload").unwrap_or(item);
                    let item_id = item
                        .get("id")
                        .and_then(|v| v.as_str())
                        .unwrap_or("")
                        .to_string();
                    let call_id = payload
                        .get("callId")
                        .and_then(|v| v.as_str())
                        .map(str::to_string);
                    let final_output = payload
                        .get("aggregatedOutput")
                        .and_then(|v| v.as_str())
                        .map(str::to_string);
                    let exit_code = payload
                        .get("exitCode")
                        .and_then(|v| v.as_i64())
                        .and_then(|v| i32::try_from(v).ok());
                    let duration = payload
                        .get("durationMs")
                        .and_then(|v| v.as_u64())
                        .map(std::time::Duration::from_millis);
                    let status = payload
                        .get("status")
                        .and_then(|v| v.as_str())
                        .unwrap_or("completed")
                        .to_string();

                    let mut output_for_merge = final_output.clone().unwrap_or_default();
                    let mut call_id_for_merge = call_id.clone();
                    if let Some(exec) = state
                        .streaming
                        .active_command_executions
                        .iter_mut()
                        .find(|e| e.item_id == item_id)
                    {
                        exec.completed = true;
                        exec.status = status;
                        exec.exit_code = exit_code;
                        exec.duration = duration.or_else(|| Some(exec.started_at.elapsed()));
                        if let Some(ref out) = final_output {
                            exec.aggregated_output = out.clone();
                        }
                        if output_for_merge.is_empty() {
                            output_for_merge = exec.aggregated_output.clone();
                        }
                        if call_id_for_merge.is_none() {
                            call_id_for_merge = exec.call_id.clone();
                        }
                    }

                    if let Some(call_id) = call_id_for_merge.as_deref() {
                        if let Some(tool) = state
                            .streaming
                            .active_tools
                            .iter_mut()
                            .find(|t| t.call_id == call_id)
                        {
                            if !output_for_merge.is_empty() {
                                tool.result = Some(output_for_merge.clone());
                            }
                            if let Some(d) = duration {
                                tool.duration = Some(d);
                            }
                            if let Some(code) = exit_code {
                                tool.success = code == 0;
                            }
                        }

                        for entry in state.history.iter_mut().rev() {
                            if let HistoryEntry::ToolCall {
                                call_id: ref id,
                                result: ref mut r,
                                success: ref mut s,
                                duration: ref mut d,
                                ..
                            } = entry
                            {
                                if id == call_id {
                                    if !output_for_merge.is_empty() {
                                        *r = Some(output_for_merge.clone());
                                    }
                                    if let Some(code) = exit_code {
                                        *s = code == 0;
                                    }
                                    if duration.is_some() {
                                        *d = duration;
                                    }
                                    break;
                                }
                            }
                        }
                    }

                    state
                        .streaming
                        .active_command_executions
                        .retain(|e| e.item_id != item_id);
                }
                "toolExecution" => {
                    let payload = payload_or_item(item);
                    let call_id = payload
                        .get("callId")
                        .and_then(|v| v.as_str())
                        .unwrap_or("")
                        .to_string();
                    let success = payload
                        .get("success")
                        .and_then(|v| v.as_bool())
                        .unwrap_or_else(|| {
                            payload
                                .get("status")
                                .and_then(|v| v.as_str())
                                .map(|s| s == "completed")
                                .unwrap_or(true)
                        });
                    let result_text = payload
                        .get("resultPreview")
                        .and_then(|v| v.as_str())
                        .map(str::to_string);
                    let duration = payload
                        .get("durationMs")
                        .and_then(|v| v.as_u64())
                        .map(std::time::Duration::from_millis);

                    if let Some(pos) = state
                        .streaming
                        .active_tools
                        .iter()
                        .position(|t| t.call_id == call_id)
                    {
                        let mut tool = state.streaming.active_tools.remove(pos);
                        tool.completed = true;
                        tool.success = success;
                        tool.duration = duration.or_else(|| Some(tool.started_at.elapsed()));
                        if result_text.is_some() {
                            tool.result = result_text;
                        }
                        state.history.push(HistoryEntry::ToolCall {
                            call_id: tool.call_id.clone(),
                            name: tool.tool_name,
                            args: tool.arguments,
                            result: tool.result,
                            success: tool.success,
                            duration: tool.duration,
                        });
                    } else {
                        for entry in state.history.iter_mut().rev() {
                            if let HistoryEntry::ToolCall {
                                call_id: ref id,
                                result: ref mut r,
                                success: ref mut s,
                                duration: ref mut d,
                                ..
                            } = entry
                            {
                                if id == &call_id {
                                    if r.is_none() {
                                        *r = result_text;
                                    }
                                    *s = success;
                                    if duration.is_some() {
                                        *d = duration;
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }
                "toolCall" => {
                    // toolCall completion only finalizes arguments. Execution completion is
                    // represented by toolExecution when negotiated, or toolResult as fallback.
                }
                "toolResult" | "pluginFunctionCall" | "dynamicToolCall" => {
                    let payload = payload_or_item(item);
                    let call_id = payload
                        .get("callId")
                        .and_then(|v| v.as_str())
                        .unwrap_or("")
                        .to_string();
                    let success = payload
                        .get("success")
                        .and_then(|v| v.as_bool())
                        .unwrap_or(true);
                    let result_text = payload
                        .get("result")
                        .and_then(|v| v.as_str())
                        .map(str::to_string)
                        .or_else(|| {
                            payload
                                .get("output")
                                .and_then(|v| v.as_str())
                                .map(str::to_string)
                        })
                        .or_else(|| {
                            if item_type == "pluginFunctionCall" || item_type == "dynamicToolCall" {
                                structured_invocation_result_text(payload)
                            } else {
                                None
                            }
                        });

                    // Update the ActiveToolCall if it is still in the streaming list.
                    if let Some(tool) = state
                        .streaming
                        .active_tools
                        .iter_mut()
                        .find(|t| t.call_id == call_id)
                    {
                        let now = std::time::Instant::now();
                        tool.duration = Some(now.duration_since(tool.started_at));
                        tool.completed = true;
                        tool.success = success;
                        if result_text.is_some() {
                            tool.result = result_text.clone();
                        }
                    }

                    if let Some(pos) = state
                        .streaming
                        .active_tools
                        .iter()
                        .position(|t| t.call_id == call_id && t.completed)
                    {
                        let tool = state.streaming.active_tools.remove(pos);
                        state.history.push(HistoryEntry::ToolCall {
                            call_id: tool.call_id.clone(),
                            name: tool.tool_name,
                            args: tool.arguments,
                            result: tool.result,
                            success: tool.success,
                            duration: tool.duration,
                        });
                    }

                    if item_type == "toolResult" {
                        let still_active = state
                            .streaming
                            .active_tools
                            .iter()
                            .any(|t| t.call_id == call_id);
                        if !still_active {
                            for entry in state.history.iter_mut().rev() {
                                if let HistoryEntry::ToolCall {
                                    call_id: ref id,
                                    result: ref mut r,
                                    success: ref mut s,
                                    ..
                                } = entry
                                {
                                    if id == &call_id {
                                        *r = result_text;
                                        *s = success;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                _ => {}
            }
            true
        }

        "item/approval/resolved" => {
            state.pending_approval = None;
            state.active_overlay = None;
            if state.turn_status == TurnStatus::WaitingApproval {
                state.turn_status = TurnStatus::Running;
            }
            true
        }

        "item/tool/requestUserInput/resolved" => {
            state.pending_user_input = None;
            state.active_overlay = None;
            if state.turn_status == TurnStatus::WaitingInput {
                state.turn_status = TurnStatus::Running;
            }
            true
        }

        // ── SubAgent progress ─────────────────────────────────────────────
        "subagent/progress" => {
            if let Some(entries) = params.get("entries").and_then(|v| v.as_array()) {
                state.subagent_entries = entries
                    .iter()
                    .filter_map(|e| {
                        Some(SubAgentEntry {
                            label: e.get("label")?.as_str()?.to_string(),
                            current_tool: e
                                .get("currentTool")
                                .and_then(|v| v.as_str())
                                .map(str::to_string),
                            input_tokens: e
                                .get("inputTokens")
                                .and_then(|v| v.as_i64())
                                .unwrap_or(0),
                            output_tokens: e
                                .get("outputTokens")
                                .and_then(|v| v.as_i64())
                                .unwrap_or(0),
                            is_completed: e
                                .get("isCompleted")
                                .and_then(|v| v.as_bool())
                                .unwrap_or(false),
                        })
                    })
                    .collect();
                if !state.subagent_entries.is_empty()
                    && state
                        .subagent_entries
                        .iter()
                        .all(|entry| entry.is_completed)
                {
                    state.last_subagent_entries = state.subagent_entries.clone();
                }
            }
            true
        }

        // ── Token usage ───────────────────────────────────────────────────
        "item/usage/delta" => {
            let input = params
                .get("inputTokens")
                .and_then(|v| v.as_i64())
                .unwrap_or(0);
            let output = params
                .get("outputTokens")
                .and_then(|v| v.as_i64())
                .unwrap_or(0);
            state.token_tracker.add(input, output);
            true
        }

        // ── System events ─────────────────────────────────────────────────
        "system/event" => {
            let kind = params
                .get("kind")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string();
            let message = params
                .get("message")
                .and_then(|v| v.as_str())
                .map(str::to_string);
            match kind.as_str() {
                "compacting" | "consolidating" => {
                    state.system_status = Some(SystemStatusInfo {
                        kind: kind.clone(),
                        message: message.clone(),
                    });
                }
                "compacted"
                | "compactSkipped"
                | "consolidated"
                | "consolidationSkipped"
                | "consolidationFailed" => {
                    state.system_status = None;
                    if kind != "consolidationSkipped" {
                        if let Some(msg) = message {
                            state
                                .history
                                .push(HistoryEntry::SystemInfo { message: msg });
                        }
                    }
                }
                _ => {}
            }
            true
        }

        // ── Plan updates ──────────────────────────────────────────────────
        "plan/updated" => {
            let title = params
                .get("title")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string();
            let overview = params
                .get("overview")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string();
            let todos = params
                .get("todos")
                .and_then(|v| v.as_array())
                .map(|arr| {
                    arr.iter()
                        .filter_map(|t| {
                            Some(PlanTodo {
                                id: t.get("id")?.as_str()?.to_string(),
                                content: t.get("content")?.as_str()?.to_string(),
                                priority: t
                                    .get("priority")
                                    .and_then(|v| v.as_str())
                                    .unwrap_or("medium")
                                    .to_string(),
                                status: t
                                    .get("status")
                                    .and_then(|v| v.as_str())
                                    .unwrap_or("pending")
                                    .to_string(),
                            })
                        })
                        .collect()
                })
                .unwrap_or_default();
            state.plan = Some(PlanSnapshot {
                title,
                overview,
                todos,
            });
            true
        }

        // ── Job results ───────────────────────────────────────────────────
        "system/jobResult" => {
            use chrono::Utc;
            let dismiss_at_ms = Utc::now().timestamp_millis() + 10_000;
            state
                .notifications
                .push_back(crate::app::state::NotificationEntry {
                    source: params
                        .get("source")
                        .and_then(|v| v.as_str())
                        .unwrap_or("cron")
                        .to_string(),
                    job_name: params
                        .get("jobName")
                        .and_then(|v| v.as_str())
                        .map(str::to_string),
                    result: params
                        .get("result")
                        .and_then(|v| v.as_str())
                        .map(str::to_string),
                    error: params
                        .get("error")
                        .and_then(|v| v.as_str())
                        .map(str::to_string),
                    dismiss_at_ms,
                });
            true
        }

        // ── Thread lifecycle ──────────────────────────────────────────────
        "thread/started" | "thread/resumed" => {
            if let Some(thread) = params.get("thread") {
                state.current_thread_id = thread
                    .get("id")
                    .and_then(|v| v.as_str())
                    .map(str::to_string);
                state.current_thread_name = thread
                    .get("displayName")
                    .and_then(|v| v.as_str())
                    .map(str::to_string);
                state.current_goal = thread
                    .get("goal")
                    .and_then(|goal| serde_json::from_value(goal.clone()).ok());
            }
            true
        }

        "thread/statusChanged" => true,

        "thread/goal/updated" => {
            state.current_goal = params
                .get("goal")
                .and_then(|goal| serde_json::from_value(goal.clone()).ok());
            true
        }

        "thread/goal/cleared" => {
            state.current_goal = None;
            true
        }

        _ => false,
    }
}

/// Move any in-flight streaming content into committed history.
fn finalize_streaming(state: &mut AppState) {
    let msg = std::mem::take(&mut state.streaming.message_buffer);
    if !msg.is_empty() {
        state.history.push(HistoryEntry::AgentMessage { text: msg });
    }
    state.streaming = StreamingState::default();
}

#[cfg(test)]
mod tests {
    use super::apply;
    use crate::app::state::{ActiveToolCall, AppState, HistoryEntry};
    use crate::wire::types::JsonRpcMessage;

    fn notification(method: &str, params: serde_json::Value) -> JsonRpcMessage {
        JsonRpcMessage {
            jsonrpc: Some("2.0".to_string()),
            id: None,
            method: Some(method.to_string()),
            params: Some(params),
            result: None,
            error: None,
        }
    }

    #[test]
    fn consolidation_failed_clears_system_status_and_records_message() {
        let mut state = AppState::new("workspace".to_string());

        assert!(apply(
            &mut state,
            &notification(
                "system/event",
                serde_json::json!({
                    "kind": "consolidating",
                    "message": "Consolidating memory"
                })
            )
        ));
        assert_eq!(
            state
                .system_status
                .as_ref()
                .map(|status| status.kind.as_str()),
            Some("consolidating")
        );

        assert!(apply(
            &mut state,
            &notification(
                "system/event",
                serde_json::json!({
                    "kind": "consolidationFailed",
                    "message": "Memory consolidation failed"
                })
            )
        ));

        assert!(state.system_status.is_none());
        assert!(matches!(
            state.history.last(),
            Some(HistoryEntry::SystemInfo { message }) if message == "Memory consolidation failed"
        ));
    }

    #[test]
    fn command_execution_lifecycle_merges_into_exec_tool() {
        let mut state = AppState::new("workspace".to_string());
        state.streaming.active_tools.push(ActiveToolCall {
            call_id: "call-1".to_string(),
            tool_name: "Exec".to_string(),
            arguments: r#"{"command":"echo hi"}"#.to_string(),
            completed: false,
            result: None,
            success: true,
            started_at: std::time::Instant::now(),
            duration: None,
        });
        state.history.push(HistoryEntry::ToolCall {
            call_id: "call-1".to_string(),
            name: "Exec".to_string(),
            args: r#"{"command":"echo hi"}"#.to_string(),
            result: None,
            success: true,
            duration: None,
        });

        assert!(apply(
            &mut state,
            &notification(
                "item/started",
                serde_json::json!({
                    "item": {
                        "id": "cmd-1",
                        "type": "commandExecution",
                        "payload": {
                            "callId": "call-1",
                            "command": "echo hi",
                            "workingDirectory": "/tmp",
                            "source": "host",
                            "status": "inProgress"
                        }
                    }
                })
            )
        ));
        assert_eq!(state.streaming.active_command_executions.len(), 1);

        state.at_bottom = false;
        assert!(apply(
            &mut state,
            &notification(
                "item/commandExecution/outputDelta",
                serde_json::json!({
                    "threadId": "t1",
                    "turnId": "turn-1",
                    "itemId": "cmd-1",
                    "delta": "hello\\n"
                })
            )
        ));
        assert!(state.at_bottom);
        assert_eq!(
            state
                .streaming
                .active_command_executions
                .first()
                .map(|e| e.aggregated_output.clone()),
            Some("hello\\n".to_string())
        );
        assert_eq!(
            state
                .streaming
                .active_tools
                .first()
                .and_then(|t| t.result.clone()),
            Some("hello\\n".to_string())
        );

        assert!(apply(
            &mut state,
            &notification(
                "item/completed",
                serde_json::json!({
                    "item": {
                        "id": "cmd-1",
                        "type": "commandExecution",
                        "payload": {
                            "callId": "call-1",
                            "status": "completed",
                            "exitCode": 0,
                            "durationMs": 12,
                            "aggregatedOutput": "hello\\nworld\\n"
                        }
                    }
                })
            )
        ));

        assert!(state.streaming.active_command_executions.is_empty());

        let merged_tool = state
            .history
            .iter()
            .rev()
            .find_map(|entry| match entry {
                HistoryEntry::ToolCall {
                    call_id,
                    result,
                    success,
                    duration,
                    ..
                } if call_id == "call-1" => Some((result.clone(), *success, *duration)),
                _ => None,
            })
            .expect("merged exec tool");
        assert_eq!(merged_tool.0, Some("hello\\nworld\\n".to_string()));
        assert!(merged_tool.1);
        assert!(merged_tool.2.is_some());
    }

    #[test]
    fn terminal_lifecycle_merges_into_exec_tool() {
        let mut state = AppState::new("workspace".to_string());
        state.streaming.active_tools.push(ActiveToolCall {
            call_id: "call-term".to_string(),
            tool_name: "Exec".to_string(),
            arguments: r#"{"command":"echo hi"}"#.to_string(),
            completed: false,
            result: None,
            success: true,
            started_at: std::time::Instant::now(),
            duration: None,
        });
        state.history.push(HistoryEntry::ToolCall {
            call_id: "call-term".to_string(),
            name: "Exec".to_string(),
            args: r#"{"command":"echo hi"}"#.to_string(),
            result: None,
            success: true,
            duration: None,
        });

        assert!(apply(
            &mut state,
            &notification(
                "terminal/started",
                serde_json::json!({
                    "terminal": {
                        "sessionId": "term-1",
                        "threadId": "thread-1",
                        "turnId": "turn-1",
                        "callId": "call-term",
                        "command": "echo hi",
                        "workingDirectory": "/tmp",
                        "source": "host",
                        "status": "running",
                        "output": ""
                    }
                })
            )
        ));
        assert_eq!(state.streaming.active_command_executions.len(), 1);

        state.at_bottom = false;
        assert!(apply(
            &mut state,
            &notification(
                "terminal/outputDelta",
                serde_json::json!({
                    "terminal": {
                        "sessionId": "term-1",
                        "threadId": "thread-1",
                        "turnId": "turn-1",
                        "callId": "call-term",
                        "command": "echo hi",
                        "workingDirectory": "/tmp",
                        "source": "host",
                        "status": "running",
                        "output": "hello\\n",
                        "wallTimeMs": 8
                    },
                    "delta": "hello\\n"
                })
            )
        ));
        assert!(state.at_bottom);
        assert_eq!(
            state
                .streaming
                .active_command_executions
                .first()
                .map(|e| e.aggregated_output.clone()),
            Some("hello\\n".to_string())
        );
        assert_eq!(
            state
                .streaming
                .active_tools
                .first()
                .and_then(|t| t.result.clone()),
            Some("hello\\n".to_string())
        );

        assert!(apply(
            &mut state,
            &notification(
                "terminal/completed",
                serde_json::json!({
                    "terminal": {
                        "sessionId": "term-1",
                        "threadId": "thread-1",
                        "turnId": "turn-1",
                        "callId": "call-term",
                        "command": "echo hi",
                        "workingDirectory": "/tmp",
                        "source": "host",
                        "status": "completed",
                        "output": "hello\\nworld\\n",
                        "exitCode": 0,
                        "wallTimeMs": 12
                    }
                })
            )
        ));

        assert!(state.streaming.active_command_executions.is_empty());
        let merged_tool = state
            .history
            .iter()
            .rev()
            .find_map(|entry| match entry {
                HistoryEntry::ToolCall {
                    call_id,
                    result,
                    success,
                    duration,
                    ..
                } if call_id == "call-term" => Some((result.clone(), *success, *duration)),
                _ => None,
            })
            .expect("merged terminal exec tool");
        assert_eq!(merged_tool.0, Some("hello\\nworld\\n".to_string()));
        assert!(merged_tool.1);
        assert_eq!(merged_tool.2, Some(std::time::Duration::from_millis(12)));
    }

    #[test]
    fn run_in_background_terminal_output_is_not_inline() {
        let mut state = AppState::new("workspace".to_string());
        state.streaming.active_tools.push(ActiveToolCall {
            call_id: "call-bg".to_string(),
            tool_name: "Exec".to_string(),
            arguments: r#"{"command":"npm run dev","runInBackground":true}"#.to_string(),
            completed: false,
            result: None,
            success: true,
            started_at: std::time::Instant::now(),
            duration: None,
        });

        assert!(apply(
            &mut state,
            &notification(
                "terminal/outputDelta",
                serde_json::json!({
                    "terminal": {
                        "sessionId": "term-bg",
                        "threadId": "thread-1",
                        "turnId": "turn-1",
                        "callId": "call-bg",
                        "command": "npm run dev",
                        "status": "running",
                        "output": "server ready\\n",
                        "backgroundReason": "runInBackground"
                    },
                    "delta": "server ready\\n"
                })
            )
        ));

        assert!(state.streaming.active_command_executions.is_empty());
        assert_eq!(
            state
                .streaming
                .active_tools
                .first()
                .and_then(|t| t.result.clone()),
            None
        );
    }

    #[test]
    fn command_execution_output_delta_updates_committed_exec_tool() {
        let mut state = AppState::new("workspace".to_string());
        state.streaming.active_tools.push(ActiveToolCall {
            call_id: "call-2".to_string(),
            tool_name: "Exec".to_string(),
            arguments: r#"{"command":"echo hi"}"#.to_string(),
            completed: false,
            result: None,
            success: true,
            started_at: std::time::Instant::now(),
            duration: None,
        });

        assert!(apply(
            &mut state,
            &notification(
                "item/started",
                serde_json::json!({
                    "item": {
                        "id": "cmd-2",
                        "type": "commandExecution",
                        "payload": {
                            "callId": "call-2",
                            "command": "echo hi",
                            "workingDirectory": "/tmp",
                            "source": "host",
                            "status": "inProgress"
                        }
                    }
                })
            )
        ));
        assert_eq!(state.streaming.active_command_executions.len(), 1);

        assert!(apply(
            &mut state,
            &notification(
                "item/completed",
                serde_json::json!({
                    "item": {
                        "id": "tool-2",
                        "type": "toolExecution",
                        "payload": {
                            "callId": "call-2",
                            "toolName": "Exec",
                            "status": "completed",
                            "success": true
                        }
                    }
                })
            )
        ));
        assert!(state.streaming.active_tools.is_empty());

        let committed_before_delta = state.history.iter().rev().find_map(|entry| match entry {
            HistoryEntry::ToolCall {
                call_id, result, ..
            } if call_id == "call-2" => Some(result.clone()),
            _ => None,
        });
        assert_eq!(committed_before_delta, Some(None));

        assert!(apply(
            &mut state,
            &notification(
                "item/commandExecution/outputDelta",
                serde_json::json!({
                    "threadId": "t1",
                    "turnId": "turn-1",
                    "itemId": "cmd-2",
                    "delta": "live\\n"
                })
            )
        ));

        let committed_after_delta = state.history.iter().rev().find_map(|entry| match entry {
            HistoryEntry::ToolCall {
                call_id, result, ..
            } if call_id == "call-2" => Some(result.clone()),
            _ => None,
        });
        assert_eq!(committed_after_delta, Some(Some("live\\n".to_string())));

        assert!(apply(
            &mut state,
            &notification(
                "item/completed",
                serde_json::json!({
                    "item": {
                        "id": "cmd-2",
                        "type": "commandExecution",
                        "payload": {
                            "callId": "call-2",
                            "status": "completed",
                            "exitCode": 0,
                            "durationMs": 10,
                            "aggregatedOutput": "live\\nfinal\\n"
                        }
                    }
                })
            )
        ));

        let committed_after_completed = state.history.iter().rev().find_map(|entry| match entry {
            HistoryEntry::ToolCall {
                call_id,
                result,
                success,
                duration,
                ..
            } if call_id == "call-2" => Some((result.clone(), *success, *duration)),
            _ => None,
        });
        assert_eq!(
            committed_after_completed,
            Some((
                Some("live\\nfinal\\n".to_string()),
                true,
                Some(std::time::Duration::from_millis(10))
            ))
        );
    }

    #[test]
    fn tool_call_completed_only_finalizes_arguments_not_execution() {
        let mut state = AppState::new("workspace".to_string());
        state.streaming.active_tools.push(ActiveToolCall {
            call_id: "call-args".to_string(),
            tool_name: "ReadFile".to_string(),
            arguments: r#"{"path":"a.txt"}"#.to_string(),
            completed: false,
            result: None,
            success: true,
            started_at: std::time::Instant::now(),
            duration: None,
        });

        assert!(apply(
            &mut state,
            &notification(
                "item/completed",
                serde_json::json!({
                    "item": {
                        "id": "tool-args",
                        "type": "toolCall",
                        "payload": {
                            "callId": "call-args",
                            "toolName": "ReadFile",
                            "arguments": { "path": "a.txt" }
                        }
                    }
                })
            )
        ));

        assert_eq!(state.streaming.active_tools.len(), 1);
        assert!(state.history.is_empty());
    }

    #[test]
    fn command_execution_output_delta_unknown_item_is_ignored() {
        let mut state = AppState::new("workspace".to_string());
        assert!(apply(
            &mut state,
            &notification(
                "item/commandExecution/outputDelta",
                serde_json::json!({
                    "threadId": "t1",
                    "turnId": "turn-1",
                    "itemId": "unknown",
                    "delta": "chunk"
                })
            )
        ));
        assert!(state.streaming.active_command_executions.is_empty());
    }

    #[test]
    fn plugin_function_call_completes_without_tool_result() {
        let mut state = AppState::new("workspace".to_string());

        assert!(apply(
            &mut state,
            &notification(
                "item/started",
                serde_json::json!({
                    "item": {
                        "id": "plugin-1",
                        "type": "pluginFunctionCall",
                        "payload": {
                            "pluginId": "browser",
                            "namespace": "node_repl",
                            "callId": "plugin-call-1",
                            "functionName": "NodeReplJs",
                            "arguments": { "code": "1 + 1" }
                        }
                    }
                })
            )
        ));

        assert_eq!(state.streaming.active_tools.len(), 1);
        assert_eq!(state.streaming.active_tools[0].tool_name, "NodeReplJs");

        assert!(apply(
            &mut state,
            &notification(
                "item/completed",
                serde_json::json!({
                    "item": {
                        "id": "plugin-1",
                        "type": "pluginFunctionCall",
                        "payload": {
                            "pluginId": "browser",
                            "namespace": "node_repl",
                            "callId": "plugin-call-1",
                            "functionName": "NodeReplJs",
                            "contentItems": [
                                { "type": "text", "text": "2" },
                                { "type": "image", "mediaType": "image/png", "dataBase64": "abc123" }
                            ],
                            "success": true
                        }
                    }
                })
            )
        ));

        assert!(state.streaming.active_tools.is_empty());
        assert!(matches!(
            state.history.last(),
            Some(HistoryEntry::ToolCall {
                call_id,
                name,
                result,
                success,
                ..
            }) if call_id == "plugin-call-1"
                && name == "NodeReplJs"
                && result.as_deref() == Some("2\n[image: image/png]")
                && *success
        ));
    }

    #[test]
    fn dynamic_tool_call_completes_without_tool_result() {
        let mut state = AppState::new("workspace".to_string());

        assert!(apply(
            &mut state,
            &notification(
                "item/started",
                serde_json::json!({
                    "item": {
                        "id": "dynamic-1",
                        "type": "dynamicToolCall",
                        "payload": {
                            "namespace": "oratorio",
                            "callId": "dynamic-call-1",
                            "toolName": "ListBoardItems",
                            "arguments": { "status": "todo" }
                        }
                    }
                })
            )
        ));

        assert_eq!(state.streaming.active_tools.len(), 1);
        assert_eq!(state.streaming.active_tools[0].tool_name, "ListBoardItems");

        assert!(apply(
            &mut state,
            &notification(
                "item/completed",
                serde_json::json!({
                    "item": {
                        "id": "dynamic-1",
                        "type": "dynamicToolCall",
                        "payload": {
                            "namespace": "oratorio",
                            "callId": "dynamic-call-1",
                            "toolName": "ListBoardItems",
                            "contentItems": [
                                { "type": "text", "text": "2 board items" },
                                { "type": "image", "mediaType": "image/png", "dataBase64": "abc123" }
                            ],
                            "success": true
                        }
                    }
                })
            )
        ));

        assert!(state.streaming.active_tools.is_empty());
        assert!(matches!(
            state.history.last(),
            Some(HistoryEntry::ToolCall {
                call_id,
                name,
                result,
                success,
                ..
            }) if call_id == "dynamic-call-1"
                && name == "ListBoardItems"
                && result.as_deref() == Some("2 board items\n[image: image/png]")
                && *success
        ));
    }
}
