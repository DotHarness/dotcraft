pub mod app;
pub mod clipboard;
pub mod hub;
pub mod i18n;
pub mod terminal;
pub mod theme;
pub mod ui;
pub mod wire;

use anyhow::Result;
use crossterm::event::{Event as CrosstermEvent, EventStream, KeyEventKind, MouseEventKind};
use futures::StreamExt;
use std::{
    path::PathBuf,
    time::{Duration, Instant},
};
use tokio::sync::mpsc as tokio_mpsc;
use tokio::time;

use crate::{
    app::{
        commands::{self, LocalSlashCommand, ParsedSlashCommand},
        event_mapper,
        input_router::{
            self, InputAction, ModelPickerOp, PermissionsPickerOp, SkillsPickerOp, ThreadPickerOp,
        },
        state::{
            AgentMode, AppState, ApprovalState, HistoryEntry, ModelCacheState, ModelCatalogItem,
            ModelPickerState, ModelReasoningCapability, OverlayKind, PermissionOption,
            PermissionsPickerState, ReasoningConfigState, ReasoningPickerOption,
            ReasoningQuickValue, SkillCacheState, SkillsPickerState, ThreadEntry,
            ThreadPickerState, TurnStatus, UserInputOption, UserInputQuestion,
            UserInputRequestState,
        },
    },
    i18n::Strings,
    terminal::{Term, TerminalGuard},
    theme::Theme,
    ui::{
        chat_view::ChatView,
        footer_line::FooterLine,
        input_editor::InputEditor,
        layout,
        overlays::{
            approval::ApprovalOverlay, command_popup::CommandPopup, model_picker::ModelPicker,
            notification::NotificationToast, permissions_picker::PermissionsPicker,
            skill_popup::SkillPopup, skills_picker::SkillsPicker, thread_picker::ThreadPicker,
            user_input::UserInputOverlay,
        },
        status_indicator::StatusIndicator,
    },
    wire::client::WireClient,
};

#[cfg(feature = "websocket")]
use crate::wire::transport::Transport;

/// Tracks how we're connected to the AppServer for reconnection logic.
#[derive(Clone, Debug)]
#[allow(dead_code)]
enum ConnectionMode {
    LocalHub {
        dotcraft_bin: String,
        workspace_path: PathBuf,
    },
    WebSocket {
        url: String,
    },
}

/// Async result forwarded from spawned tasks back into the event loop.
enum DeferredResult {
    ConnectionReady(Result<ConnectedAppServer>),
    ThreadListLoaded(Result<serde_json::Value>),
    ThreadHistoryLoaded(Result<serde_json::Value>),
    ModelCatalogLoaded(Result<serde_json::Value>),
    SkillsListLoaded(Result<serde_json::Value>),
}

struct ConnectedAppServer {
    wire: WireClient,
    ws_url: String,
}

/// Resolve the UI language with the following priority:
///   1. Explicit `--lang` CLI flag (highest priority).
///   2. `Language` field in `{workspace}/.craft/config.json`.
///   3. Default: `"en"`.
///
/// config.json values recognised (case-insensitive):
///   "Chinese" | "中文" | "zh" | "zh-cn" -> "zh"
///   "English" | "en"                    -> "en"
fn resolve_language(cli_lang: Option<&str>, workspace_path: Option<&std::path::Path>) -> String {
    // 1. CLI flag wins unconditionally.
    if let Some(lang) = cli_lang {
        return lang.to_string();
    }

    // 2. Try .craft/config.json in the workspace directory.
    if let Some(ws) = workspace_path {
        let config_path = ws.join(".craft").join("config.json");
        if let Ok(content) = std::fs::read_to_string(&config_path) {
            if let Ok(value) = serde_json::from_str::<serde_json::Value>(&content) {
                if let Some(lang_val) = value.get("Language").and_then(|v| v.as_str()) {
                    return match lang_val.to_lowercase().as_str() {
                        "chinese" | "中文" | "zh" | "zh-cn" | "zh_cn" => "zh".to_string(),
                        _ => "en".to_string(),
                    };
                }
            }
        }
    }

    // 3. Default.
    "en".to_string()
}

fn read_workspace_model(workspace_path: &std::path::Path) -> Option<String> {
    let config_path = workspace_path.join(".craft").join("config.json");
    let content = std::fs::read_to_string(config_path).ok()?;
    let value = serde_json::from_str::<serde_json::Value>(&content).ok()?;
    let obj = value.as_object()?;
    for (k, v) in obj {
        if k.eq_ignore_ascii_case("model") {
            let model = v.as_str()?.trim().to_string();
            if model.is_empty() {
                return None;
            }
            return Some(model);
        }
    }
    None
}

fn read_workspace_reasoning(workspace_path: &std::path::Path) -> Option<ReasoningConfigState> {
    let config_path = workspace_path.join(".craft").join("config.json");
    let content = std::fs::read_to_string(config_path).ok()?;
    let value = serde_json::from_str::<serde_json::Value>(&content).ok()?;
    let obj = value.as_object()?;
    let section = obj
        .iter()
        .find(|(k, _)| k.eq_ignore_ascii_case("reasoning"))
        .map(|(_, v)| v)?;
    parse_reasoning_config(section)
}

fn read_workspace_provider_id(workspace_path: &std::path::Path) -> Option<String> {
    let config_path = workspace_path.join(".craft").join("config.json");
    let content = std::fs::read_to_string(config_path).ok()?;
    let value = serde_json::from_str::<serde_json::Value>(&content).ok()?;
    let obj = value.as_object()?;
    for (k, v) in obj {
        if k.eq_ignore_ascii_case("providerId") {
            let provider_id = v.as_str()?.trim().to_string();
            if provider_id.is_empty() {
                return None;
            }
            return Some(provider_id);
        }
    }
    None
}

/// Entry point called from main.rs.
pub async fn run(
    remote: Option<String>,
    server_bin: Option<String>,
    workspace: Option<String>,
    theme_path: Option<String>,
    lang: Option<String>,
) -> Result<()> {
    // ── 1. Logging ────────────────────────────────────────────────────────
    tracing_subscriber::fmt()
        .with_writer(std::io::stderr)
        .with_env_filter(tracing_subscriber::EnvFilter::from_env("DOTCRAFT_TUI_LOG"))
        .init();

    // ── 2. Theme and i18n ─────────────────────────────────────────────────
    // Resolve the effective workspace path early so theme and language loading
    // can both read .craft/ from it.
    let resolved_workspace: std::path::PathBuf = workspace
        .as_deref()
        .map(std::path::PathBuf::from)
        .or_else(|| std::env::current_dir().ok())
        .unwrap_or_default();
    let workspace_path = Some(resolved_workspace.as_path());

    let cli_theme_path = theme_path.as_deref().map(std::path::Path::new);
    let theme = Theme::resolve(cli_theme_path, workspace_path)?;
    let resolved_lang = resolve_language(lang.as_deref(), workspace_path);
    let strings = i18n::load(&resolved_lang);

    // ── 3. Connection intent ──────────────────────────────────────────────
    let connection_mode = if remote.is_some() {
        ConnectionMode::WebSocket {
            url: remote.clone().unwrap(),
        }
    } else {
        ConnectionMode::LocalHub {
            dotcraft_bin: hub::resolve_dotcraft_binary(server_bin.as_deref()),
            workspace_path: resolved_workspace.clone(),
        }
    };

    // ── 4. Terminal init ──────────────────────────────────────────────────
    let mut terminal = terminal::init()?;
    let _guard = TerminalGuard;

    // ── 5. AppState ───────────────────────────────────────────────────────
    let ws_path = resolved_workspace.to_string_lossy().into_owned();
    let mut state = AppState::new(ws_path.clone());
    state.workspace_model = read_workspace_model(&resolved_workspace);
    state.workspace_reasoning = read_workspace_reasoning(&resolved_workspace);
    state.workspace_provider_id = read_workspace_provider_id(&resolved_workspace);
    state.command_catalog = commands::merge_command_catalog(&state.server_commands);

    // ── 6. Event loop (connection starts in the background) ───────────────
    run_event_loop(
        &mut terminal,
        &mut state,
        &theme,
        &strings,
        &resolved_lang,
        &connection_mode,
    )
    .await?;

    Ok(())
}

// ── Event loop ────────────────────────────────────────────────────────────

async fn run_event_loop(
    terminal: &mut Term,
    state: &mut AppState,
    theme: &Theme,
    strings: &Strings,
    language: &str,
    conn_mode: &ConnectionMode,
) -> Result<()> {
    let mut tick = time::interval(Duration::from_millis(16)); // ~60 fps
    tick.set_missed_tick_behavior(time::MissedTickBehavior::Skip);

    let mut event_stream = EventStream::new();

    let (deferred_tx, mut deferred_rx) = tokio_mpsc::unbounded_channel::<DeferredResult>();
    spawn_connection(conn_mode.clone(), &deferred_tx);
    let mut connection_in_flight = true;
    let mut wire: Option<WireClient> = None;

    loop {
        if let Some(active_wire) = wire.as_mut() {
            let mut lost_connection: Option<String> = None;
            tokio::select! {
                // ── Wire messages ─────────────────────────────────────────
                Some(msg_result) = active_wire.recv() => {
                match msg_result {
                    Err(e) => {
                        tracing::warn!("Wire error: {e}");
                        lost_connection = Some(e.to_string());
                    }
                    Ok(msg) => {
                        if active_wire.resolve_response(&msg) {
                            // handled internally
                        } else if is_server_request(&msg) {
                            handle_server_request(active_wire, state, msg).await?;
                        } else {
                            event_mapper::apply(state, &msg);
                            // Auto-submit first queued message after a turn completes.
                            if state.turn_status == TurnStatus::Idle {
                                if let Some(queued) = state.pending_input.first().cloned() {
                                    state.pending_input.remove(0);
                                    submit_turn(active_wire, state, queued).await?;
                                }
                            }
                        }
                    }
                }
                }

                // ── Deferred async results ───────────────────────────────
                Some(deferred) = deferred_rx.recv() => {
                    handle_connected_deferred_result(
                        &mut connection_in_flight,
                        state,
                        strings,
                        deferred,
                    );
                }

                // ── Terminal events ───────────────────────────────────────
                Some(evt_result) = event_stream.next() => {
                    match evt_result {
                        Err(e) => tracing::warn!("Terminal event error: {e}"),
                        Ok(evt) => {
                            if handle_terminal_event(terminal, active_wire, state, theme, strings, &deferred_tx, evt).await? {
                                break;
                            }
                        }
                    }
                }

                // ── Tick: redraw ──────────────────────────────────────────
                _ = tick.tick() => {
                    state.tick_count = state.tick_count.wrapping_add(1);
                    expire_notifications(state);
                    draw(terminal, state, theme, strings)?;
                }
            }
            if let Some(error) = lost_connection {
                state.connected = false;
                wire = None;
                state.history.push(HistoryEntry::SystemInfo {
                    message: format!("Connection lost: {error}. Reconnecting..."),
                });
                if !connection_in_flight {
                    spawn_connection(conn_mode.clone(), &deferred_tx);
                    connection_in_flight = true;
                }
            }
        } else {
            tokio::select! {
                Some(deferred) = deferred_rx.recv() => {
                    handle_deferred_result(
                        &mut wire,
                        &mut connection_in_flight,
                        state,
                        strings,
                        language,
                        &deferred_tx,
                        deferred,
                    ).await?;
                }

                Some(evt_result) = event_stream.next() => {
                    match evt_result {
                        Err(e) => tracing::warn!("Terminal event error: {e}"),
                        Ok(evt) => {
                            if handle_disconnected_terminal_event(state, evt) {
                                break;
                            }
                        }
                    }
                }

                _ = tick.tick() => {
                    state.tick_count = state.tick_count.wrapping_add(1);
                    expire_notifications(state);
                    draw(terminal, state, theme, strings)?;
                }
            }
        }
    }

    Ok(())
}

// ── Terminal event handler ────────────────────────────────────────────────

async fn handle_terminal_event(
    terminal: &mut Term,
    wire: &mut WireClient,
    state: &mut AppState,
    _theme: &Theme,
    strings: &Strings,
    deferred_tx: &tokio_mpsc::UnboundedSender<DeferredResult>,
    evt: CrosstermEvent,
) -> Result<bool> {
    match evt {
        CrosstermEvent::Key(key) => {
            if key.kind == KeyEventKind::Release {
                return Ok(false);
            }

            // When an overlay is active it captures all key events.
            if let Some(overlay) = &state.active_overlay.clone() {
                match overlay {
                    OverlayKind::Approval => {
                        let action = input_router::handle_approval_overlay(state, key);
                        if let InputAction::ApprovalDecision(decision) = action {
                            if let Some(approval) = state.pending_approval.take() {
                                wire.respond(
                                    approval.request_id,
                                    serde_json::json!({ "decision": decision }),
                                )
                                .await?;
                            }
                            state.active_overlay = None;
                        }
                    }
                    OverlayKind::UserInput => {
                        let action = input_router::handle_user_input_overlay(state, key);
                        if let InputAction::UserInputResponse(response) = action {
                            if let Some(request) = state.pending_user_input.take() {
                                wire.respond(request.request_id, response).await?;
                            }
                            state.active_overlay = None;
                            state.turn_status = TurnStatus::Running;
                        }
                    }
                    OverlayKind::ThreadPicker => {
                        let action = input_router::handle_thread_picker(state, key);
                        handle_thread_picker_action(wire, state, deferred_tx, action).await?;
                    }
                    OverlayKind::ModelPicker => {
                        let action = input_router::handle_model_picker(state, key);
                        handle_model_picker_action(wire, state, action).await?;
                    }
                    OverlayKind::SkillsPicker => {
                        let action = input_router::handle_skills_picker(state, key);
                        handle_skills_picker_action(wire, state, deferred_tx, action).await?;
                    }
                    OverlayKind::PermissionsPicker => {
                        let action = input_router::handle_permissions_picker(state, key);
                        handle_permissions_picker_action(wire, state, action).await?;
                    }
                }
                return Ok(false);
            }

            let action = input_router::handle_key(state, key);
            match action {
                InputAction::SubmitTurn(text) => {
                    if text.is_empty() {
                        return Ok(false);
                    }
                    if let Some(cmd) = commands::parse(&text) {
                        let quit =
                            handle_slash_command(wire, state, strings, deferred_tx, cmd).await?;
                        if quit {
                            return Ok(true);
                        }
                        terminal.clear()?;
                    } else {
                        state.streaming.clear();
                        submit_turn(wire, state, text).await?;
                    }
                }
                InputAction::Interrupt => {
                    if handle_interrupt(wire, state).await? {
                        return Ok(true);
                    }
                }
                InputAction::SoftInterrupt => {
                    if state.turn_status == TurnStatus::Running
                        || state.turn_status == TurnStatus::WaitingApproval
                        || state.turn_status == TurnStatus::WaitingInput
                    {
                        if let Some(thread_id) = &state.current_thread_id.clone() {
                            let turn_id = state.current_turn_id.clone().unwrap_or_default();
                            let params = serde_json::json!({
                                "threadId": thread_id,
                                "turnId": turn_id
                            });
                            let _ = wire.send_request("turn/interrupt", params).await;
                        }
                    }
                }
                InputAction::Quit => return Ok(true),
                InputAction::ToggleMode => {
                    let new_mode = match state.mode {
                        AgentMode::Agent => AgentMode::Plan,
                        AgentMode::Plan => AgentMode::Agent,
                    };
                    state.mode = new_mode.clone();
                    if let Some(thread_id) = state.current_thread_id.clone() {
                        let mode_str = match new_mode {
                            AgentMode::Agent => "agent",
                            AgentMode::Plan => "plan",
                        };
                        wire.send_request(
                            "thread/mode/set",
                            serde_json::json!({ "threadId": thread_id, "mode": mode_str }),
                        )
                        .await?;
                    }
                }
                InputAction::ForceRedraw => {
                    terminal.clear()?;
                }
                InputAction::ApprovalDecision(_)
                | InputAction::UserInputResponse(_)
                | InputAction::ThreadPickerAction(_)
                | InputAction::ModelPickerAction(_)
                | InputAction::SkillsPickerAction(_)
                | InputAction::PermissionsPickerAction(_)
                | InputAction::CloseOverlay
                | InputAction::None => {}
            }
        }
        CrosstermEvent::Paste(text) => {
            state.input_history_pos = None;
            state.input_text.insert_str(state.input_cursor, &text);
            state.input_cursor += text.len();
        }
        CrosstermEvent::Mouse(mouse) => {
            // Modal overlays own input; wheel should not affect base transcript while active.
            if state.active_overlay.is_some() {
                return Ok(false);
            }

            match mouse.kind {
                MouseEventKind::ScrollUp => {
                    input_router::enter_transcript_browse(state);
                    input_router::scroll_line_up(state);
                }
                MouseEventKind::ScrollDown => {
                    input_router::enter_transcript_browse(state);
                    input_router::scroll_line_down(state);
                }
                _ => {}
            }
        }
        CrosstermEvent::Resize(_w, _h) => {
            // Ratatui redraws at new size automatically on next tick.
        }
        _ => {}
    }
    Ok(false)
}

// ── Wire action helpers ───────────────────────────────────────────────────

fn build_identity(workspace_path: &str) -> serde_json::Value {
    serde_json::json!({
        "channelName": "cli",
        "userId": "local",
        "workspacePath": workspace_path
    })
}

async fn spawn_model_catalog_load(
    wire: &mut WireClient,
    deferred_tx: &tokio_mpsc::UnboundedSender<DeferredResult>,
    provider_id: Option<String>,
) -> Result<()> {
    let params = match provider_id
        .as_deref()
        .map(str::trim)
        .filter(|s| !s.is_empty())
    {
        Some(id) => serde_json::json!({ "providerId": id }),
        None => serde_json::json!({}),
    };
    let (_, rx) = wire.send_request("model/list", params).await?;
    let tx = deferred_tx.clone();
    tokio::spawn(async move {
        let result = rx
            .await
            .unwrap_or_else(|_| Err(anyhow::anyhow!("response dropped")));
        let _ = tx.send(DeferredResult::ModelCatalogLoaded(result));
    });
    Ok(())
}

async fn spawn_skills_list_load(
    wire: &mut WireClient,
    deferred_tx: &tokio_mpsc::UnboundedSender<DeferredResult>,
) -> Result<()> {
    let (_, rx) = wire
        .send_request(
            "skills/list",
            serde_json::json!({ "includeUnavailable": true }),
        )
        .await?;
    let tx = deferred_tx.clone();
    tokio::spawn(async move {
        let result = rx
            .await
            .unwrap_or_else(|_| Err(anyhow::anyhow!("response dropped")));
        let _ = tx.send(DeferredResult::SkillsListLoaded(result));
    });
    Ok(())
}

fn normalize_command_language(language: &str) -> &'static str {
    if language.eq_ignore_ascii_case("zh")
        || language.eq_ignore_ascii_case("zh-cn")
        || language.eq_ignore_ascii_case("zh_cn")
    {
        "zh"
    } else {
        "en"
    }
}

async fn refresh_command_catalog(
    wire: &mut WireClient,
    state: &mut AppState,
    language: &str,
) -> Result<()> {
    if !wire.capabilities.command_management.unwrap_or(false) {
        state.server_commands.clear();
        state.command_catalog = commands::merge_command_catalog(&state.server_commands);
        return Ok(());
    }

    let result: wire::types::CommandListResult = wire
        .request(
            "command/list",
            serde_json::json!({ "language": normalize_command_language(language) }),
        )
        .await?;
    state.server_commands = result.commands;
    state.command_catalog = commands::merge_command_catalog(&state.server_commands);
    Ok(())
}

async fn create_thread(wire: &mut WireClient, state: &mut AppState) -> Result<()> {
    let ws = &state.workspace_path;
    let mut params = serde_json::json!({
        "identity": build_identity(ws)
    });
    let mut config = serde_json::Map::new();
    if let Some(model) = state.pending_model_override.clone() {
        config.insert("model".to_string(), serde_json::Value::String(model));
    }
    if let Some(reasoning) = state.pending_reasoning.clone() {
        config.insert(
            "reasoning".to_string(),
            reasoning_config_to_json(&reasoning),
        );
    }
    if let Some(policy) = state.pending_approval_policy.clone() {
        config.insert(
            "approvalPolicy".to_string(),
            serde_json::Value::String(policy),
        );
    }
    if let Some(require_outside) = state.pending_require_approval_outside_workspace {
        config.insert(
            "requireApprovalOutsideWorkspace".to_string(),
            serde_json::Value::Bool(require_outside),
        );
    }
    if !config.is_empty() {
        params["config"] = serde_json::Value::Object(config);
    }

    let result: serde_json::Value = wire.request("thread/start", params).await?;
    if let Some(thread) = result.get("thread") {
        state.current_thread_id = thread
            .get("id")
            .and_then(|v| v.as_str())
            .map(str::to_string);
        state.current_thread_name = thread
            .get("displayName")
            .and_then(|v| v.as_str())
            .map(str::to_string);
        state.current_model_override = thread
            .get("configuration")
            .and_then(|cfg| cfg.get("model"))
            .and_then(|v| v.as_str())
            .map(str::to_string)
            .or_else(|| state.pending_model_override.clone());
        state.current_reasoning = thread
            .get("configuration")
            .and_then(|cfg| cfg.get("reasoning"))
            .and_then(parse_reasoning_config)
            .or_else(|| state.pending_reasoning.clone())
            .or_else(|| state.workspace_reasoning.clone());
        state.current_approval_policy = thread
            .get("configuration")
            .and_then(|cfg| cfg.get("approvalPolicy"))
            .and_then(|v| v.as_str())
            .map(str::to_string)
            .or_else(|| state.pending_approval_policy.clone());
        state.current_require_approval_outside_workspace = thread
            .get("configuration")
            .and_then(|cfg| cfg.get("requireApprovalOutsideWorkspace"))
            .and_then(|v| v.as_bool())
            .or(state.pending_require_approval_outside_workspace);
        state.current_goal = thread
            .get("goal")
            .and_then(|goal| serde_json::from_value(goal.clone()).ok());
        state.pending_model_override = None;
        state.pending_reasoning = None;
        state.pending_approval_policy = None;
        state.pending_require_approval_outside_workspace = None;
    }
    Ok(())
}

async fn submit_turn(wire: &mut WireClient, state: &mut AppState, text: String) -> Result<()> {
    // Lazy thread creation: materialize on first user input.
    if state.current_thread_id.is_none() {
        create_thread(wire, state).await?;
    }

    let thread_id = match &state.current_thread_id {
        Some(id) => id.clone(),
        None => {
            state.history.push(HistoryEntry::Error {
                message: "Failed to create thread.".to_string(),
            });
            return Ok(());
        }
    };

    if state.turn_status != TurnStatus::Idle {
        state.history.push(HistoryEntry::Error {
            message: "A turn is already in progress. Use Ctrl+C to interrupt it.".to_string(),
        });
        return Ok(());
    }

    state
        .history
        .push(HistoryEntry::UserMessage { text: text.clone() });
    state.at_bottom = true;
    let input_parts = build_turn_input_parts(state, &text);

    let params = serde_json::json!({
        "threadId": thread_id,
        "input": input_parts
    });

    match wire.send_request("turn/start", params).await {
        Ok(_) => {
            state.turn_status = TurnStatus::Running;
        }
        Err(e) => {
            state.history.push(HistoryEntry::Error {
                message: format!("Failed to start turn: {e}"),
            });
            return Err(e);
        }
    }
    Ok(())
}

fn build_turn_input_parts(state: &AppState, text: &str) -> Vec<serde_json::Value> {
    let skill_names = match &state.skill_cache {
        SkillCacheState::Ready(skills) => skills
            .iter()
            .filter(|skill| skill.available && skill.enabled)
            .map(|skill| skill.name.to_ascii_lowercase())
            .collect::<std::collections::HashSet<_>>(),
        _ => std::collections::HashSet::new(),
    };
    if skill_names.is_empty() {
        return vec![serde_json::json!({ "type": "text", "text": text })];
    }

    let mut parts = Vec::new();
    let mut text_start = 0usize;
    let mut idx = 0usize;
    while idx < text.len() {
        let Some(ch) = text[idx..].chars().next() else {
            break;
        };
        if ch != '$' {
            idx += ch.len_utf8();
            continue;
        }
        let at_token_boundary = idx == 0
            || text[..idx]
                .chars()
                .next_back()
                .map(char::is_whitespace)
                .unwrap_or(false);
        if !at_token_boundary {
            idx += ch.len_utf8();
            continue;
        }

        let name_start = idx + 1;
        let mut name_end = name_start;
        for (offset, c) in text[name_start..].char_indices() {
            if c.is_ascii_alphanumeric() || matches!(c, '-' | '_' | '.') {
                name_end = name_start + offset + c.len_utf8();
            } else {
                break;
            }
        }
        if name_end == name_start {
            idx += 1;
            continue;
        }

        let name = &text[name_start..name_end];
        if !skill_names.contains(&name.to_ascii_lowercase()) {
            idx = name_end;
            continue;
        }

        if text_start < idx {
            parts.push(serde_json::json!({
                "type": "text",
                "text": &text[text_start..idx]
            }));
        }
        parts.push(serde_json::json!({
            "type": "skillRef",
            "name": name
        }));
        idx = name_end;
        text_start = idx;
    }

    if text_start < text.len() {
        parts.push(serde_json::json!({
            "type": "text",
            "text": &text[text_start..]
        }));
    }
    if parts.is_empty() {
        parts.push(serde_json::json!({ "type": "text", "text": text }));
    }
    parts
}

async fn handle_interrupt(wire: &mut WireClient, state: &mut AppState) -> Result<bool> {
    let now = Instant::now();

    if state.turn_status == TurnStatus::Running
        || state.turn_status == TurnStatus::WaitingApproval
        || state.turn_status == TurnStatus::WaitingInput
    {
        // Double Ctrl+C within 1 second exits even while a turn is running.
        if let Some(last) = state.last_interrupt_at {
            if now.duration_since(last) < Duration::from_secs(1) {
                return Ok(true);
            }
        }
        if let Some(thread_id) = &state.current_thread_id.clone() {
            let turn_id = state.current_turn_id.clone().unwrap_or_default();
            let params = serde_json::json!({
                "threadId": thread_id,
                "turnId": turn_id
            });
            let _ = wire.send_request("turn/interrupt", params).await;
        }
        state.last_interrupt_at = Some(now);
        return Ok(false);
    }

    if let Some(last) = state.last_interrupt_at {
        if now.duration_since(last) < Duration::from_secs(1) {
            return Ok(true);
        }
    }
    state.last_interrupt_at = Some(now);
    Ok(false)
}

async fn handle_server_request(
    _wire: &mut WireClient,
    state: &mut AppState,
    msg: wire::types::JsonRpcMessage,
) -> Result<()> {
    let method = msg.method.as_deref().unwrap_or("");
    if method == "item/approval/request" {
        let params = msg.params.as_ref().unwrap_or(&serde_json::Value::Null);
        let approval_type = params
            .get("approvalType")
            .and_then(|v| v.as_str())
            .unwrap_or("shell")
            .to_string();
        let operation = params
            .get("operation")
            .and_then(|v| v.as_str())
            .unwrap_or("")
            .to_string();
        let target = params
            .get("target")
            .and_then(|v| v.as_str())
            .unwrap_or("")
            .to_string();
        let reason = params
            .get("reason")
            .and_then(|v| v.as_str())
            .map(str::to_string);

        if let Some(id) = msg.id {
            state.pending_approval = Some(ApprovalState {
                request_id: id,
                approval_type,
                operation,
                target,
                reason,
                selected: 0,
            });
            state.active_overlay = Some(OverlayKind::Approval);
            state.turn_status = TurnStatus::WaitingApproval;
        }
    } else if method == "item/tool/requestUserInput" {
        let params = msg.params.as_ref().unwrap_or(&serde_json::Value::Null);
        if let Some(id) = msg.id {
            let request_id_text = params
                .get("requestId")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string();
            let questions = parse_user_input_questions(params);
            if questions.is_empty() {
                _wire
                    .respond(id, serde_json::json!({ "answers": {} }))
                    .await?;
            } else {
                let len = questions.len();
                state.pending_user_input = Some(UserInputRequestState {
                    request_id: id,
                    request_id_text,
                    questions,
                    current_question: 0,
                    selected: vec![0; len],
                    other_text: vec![String::new(); len],
                });
                state.active_overlay = Some(OverlayKind::UserInput);
                state.turn_status = TurnStatus::WaitingInput;
            }
        }
    }
    Ok(())
}

fn parse_user_input_questions(params: &serde_json::Value) -> Vec<UserInputQuestion> {
    params
        .get("questions")
        .and_then(|v| v.as_array())
        .map(|items| {
            items
                .iter()
                .filter_map(|item| {
                    let id = item.get("id").and_then(|v| v.as_str())?.to_string();
                    let question = item
                        .get("question")
                        .and_then(|v| v.as_str())
                        .unwrap_or("")
                        .to_string();
                    let header = item
                        .get("header")
                        .and_then(|v| v.as_str())
                        .unwrap_or("")
                        .to_string();
                    let options = item
                        .get("options")
                        .and_then(|v| v.as_array())
                        .map(|options| {
                            options
                                .iter()
                                .filter_map(|option| {
                                    Some(UserInputOption {
                                        label: option
                                            .get("label")
                                            .and_then(|v| v.as_str())?
                                            .to_string(),
                                        description: option
                                            .get("description")
                                            .and_then(|v| v.as_str())
                                            .unwrap_or("")
                                            .to_string(),
                                    })
                                })
                                .collect::<Vec<_>>()
                        })
                        .unwrap_or_default();
                    Some(UserInputQuestion {
                        id,
                        header,
                        question,
                        is_other: item
                            .get("isOther")
                            .and_then(|v| v.as_bool())
                            .unwrap_or(true),
                        options,
                    })
                })
                .collect()
        })
        .unwrap_or_default()
}

fn is_server_request(msg: &wire::types::JsonRpcMessage) -> bool {
    msg.id.is_some() && msg.method.is_some()
}

fn spawn_connection(
    conn_mode: ConnectionMode,
    deferred_tx: &tokio_mpsc::UnboundedSender<DeferredResult>,
) {
    let tx = deferred_tx.clone();
    tokio::spawn(async move {
        let result = connect_appserver(&conn_mode).await;
        let _ = tx.send(DeferredResult::ConnectionReady(result));
    });
}

async fn connect_appserver(conn_mode: &ConnectionMode) -> Result<ConnectedAppServer> {
    #[cfg(not(feature = "websocket"))]
    {
        let _ = conn_mode;
        anyhow::bail!(
            "TUI local Hub and remote modes require the 'websocket' feature. \
             Rebuild with: cargo build --features websocket"
        )
    }

    #[cfg(feature = "websocket")]
    {
        let ws_url = match conn_mode {
            ConnectionMode::WebSocket { url } => {
                tracing::info!("Connecting to remote AppServer: {url}");
                url.clone()
            }
            ConnectionMode::LocalHub {
                dotcraft_bin,
                workspace_path,
            } => {
                tracing::info!("Ensuring local AppServer through Hub: {dotcraft_bin} hub");
                let ws_url = hub::ensure_appserver(workspace_path, dotcraft_bin).await?;
                tracing::info!("Connecting to Hub-managed AppServer: {ws_url}");
                ws_url
            }
        };

        let transport = Transport::connect_ws(&ws_url).await?;
        let mut wire = WireClient::spawn(transport);
        wire.initialize().await?;
        tracing::info!(
            "Connected to DotCraft AppServer v{}",
            wire.server_info
                .as_ref()
                .map(|i| i.version.as_str())
                .unwrap_or("?")
        );
        Ok(ConnectedAppServer { wire, ws_url })
    }
}

async fn finish_connection_setup(
    wire: &mut WireClient,
    state: &mut AppState,
    language: &str,
    deferred_tx: &tokio_mpsc::UnboundedSender<DeferredResult>,
) {
    state.connected = true;
    state.model_cache = if wire.capabilities.model_catalog_management.unwrap_or(false) {
        ModelCacheState::Loading
    } else {
        ModelCacheState::Idle
    };
    state.skill_cache = if wire.capabilities.skills_management.unwrap_or(false) {
        SkillCacheState::Loading
    } else {
        SkillCacheState::Idle
    };

    if wire.capabilities.model_catalog_management.unwrap_or(false) {
        if let Err(e) =
            spawn_model_catalog_load(wire, deferred_tx, state.workspace_provider_id.clone()).await
        {
            state.model_cache = ModelCacheState::Error(format!("Failed to load models: {e}"));
        }
    }

    if let Err(e) = refresh_command_catalog(wire, state, language).await {
        tracing::warn!("Failed to load command catalog: {e}");
        state.history.push(HistoryEntry::Error {
            message: format!("Failed to load command catalog: {e}"),
        });
    }

    if wire.capabilities.skills_management.unwrap_or(false) {
        if let Err(e) = spawn_skills_list_load(wire, deferred_tx).await {
            state.skill_cache = SkillCacheState::Error(format!("Failed to load skills: {e}"));
        }
    }

    if let Some(ref tid) = state.current_thread_id {
        let _ = wire
            .notify(
                "thread/subscribe",
                serde_json::json!({
                    "threadId": tid,
                    "replayRecent": true,
                }),
            )
            .await;
    }
}

fn handle_disconnected_terminal_event(state: &mut AppState, evt: CrosstermEvent) -> bool {
    match evt {
        CrosstermEvent::Key(key) => {
            if key.kind == KeyEventKind::Release {
                return false;
            }

            if key.code == crossterm::event::KeyCode::Char('c')
                && key
                    .modifiers
                    .contains(crossterm::event::KeyModifiers::CONTROL)
            {
                let now = Instant::now();
                if let Some(last) = state.last_interrupt_at {
                    if now.duration_since(last) < Duration::from_secs(1) {
                        return true;
                    }
                }
                state.last_interrupt_at = Some(now);
                return false;
            }

            if let Some(overlay) = &state.active_overlay.clone() {
                match overlay {
                    OverlayKind::ThreadPicker => {
                        let action = input_router::handle_thread_picker(state, key);
                        if matches!(
                            action,
                            InputAction::ThreadPickerAction(ThreadPickerOp::Close)
                        ) {
                            state.active_overlay = None;
                            state.thread_picker = None;
                        }
                    }
                    OverlayKind::ModelPicker => {
                        let action = input_router::handle_model_picker(state, key);
                        if matches!(action, InputAction::ModelPickerAction(ModelPickerOp::Close)) {
                            state.active_overlay = None;
                            state.model_picker = None;
                        }
                    }
                    OverlayKind::SkillsPicker => {
                        let action = input_router::handle_skills_picker(state, key);
                        match action {
                            InputAction::SkillsPickerAction(SkillsPickerOp::Close) => {
                                state.active_overlay = None;
                                state.skills_picker = None;
                            }
                            InputAction::SkillsPickerAction(SkillsPickerOp::Toggle) => {
                                if let Some(picker) = state.skills_picker.as_mut() {
                                    picker.error =
                                        Some("Connect before changing skill settings.".to_string());
                                }
                            }
                            _ => {}
                        }
                    }
                    OverlayKind::PermissionsPicker => {
                        let action = input_router::handle_permissions_picker(state, key);
                        match action {
                            InputAction::PermissionsPickerAction(PermissionsPickerOp::Close) => {
                                state.active_overlay = None;
                                state.permissions_picker = None;
                            }
                            InputAction::PermissionsPickerAction(PermissionsPickerOp::Apply) => {
                                apply_pending_permission_selection(state);
                            }
                            _ => {}
                        }
                    }
                    OverlayKind::Approval => {
                        if matches!(
                            input_router::handle_approval_overlay(state, key),
                            InputAction::ApprovalDecision(_)
                        ) {
                            state.active_overlay = None;
                            state.pending_approval = None;
                        }
                    }
                    OverlayKind::UserInput => {
                        if matches!(
                            input_router::handle_user_input_overlay(state, key),
                            InputAction::UserInputResponse(_)
                        ) {
                            state.active_overlay = None;
                            state.pending_user_input = None;
                        }
                    }
                }
                return false;
            }

            let enter_would_submit = key.code == crossterm::event::KeyCode::Enter
                && key.modifiers == crossterm::event::KeyModifiers::NONE
                && state.command_popup.is_none()
                && state.skill_popup.is_none()
                && !state.input_text.is_empty();
            if enter_would_submit {
                return false;
            }

            let action = input_router::handle_key(state, key);
            match action {
                InputAction::Quit => return true,
                InputAction::ToggleMode => {
                    state.mode = match state.mode {
                        AgentMode::Agent => AgentMode::Plan,
                        AgentMode::Plan => AgentMode::Agent,
                    };
                }
                InputAction::SubmitTurn(_)
                | InputAction::Interrupt
                | InputAction::SoftInterrupt
                | InputAction::ApprovalDecision(_)
                | InputAction::UserInputResponse(_)
                | InputAction::ThreadPickerAction(_)
                | InputAction::ModelPickerAction(_)
                | InputAction::SkillsPickerAction(_)
                | InputAction::PermissionsPickerAction(_)
                | InputAction::CloseOverlay
                | InputAction::ForceRedraw
                | InputAction::None => {}
            }
        }
        CrosstermEvent::Paste(text) => {
            state.input_history_pos = None;
            state.input_text.insert_str(state.input_cursor, &text);
            state.input_cursor += text.len();
        }
        CrosstermEvent::Mouse(mouse) => {
            if state.active_overlay.is_some() {
                return false;
            }

            match mouse.kind {
                MouseEventKind::ScrollUp => {
                    input_router::enter_transcript_browse(state);
                    input_router::scroll_line_up(state);
                }
                MouseEventKind::ScrollDown => {
                    input_router::enter_transcript_browse(state);
                    input_router::scroll_line_down(state);
                }
                _ => {}
            }
        }
        CrosstermEvent::Resize(_w, _h) => {}
        _ => {}
    }
    false
}

/// Process a deferred async result that arrived from a spawned task.
async fn handle_deferred_result(
    wire: &mut Option<WireClient>,
    connection_in_flight: &mut bool,
    state: &mut AppState,
    strings: &Strings,
    language: &str,
    deferred_tx: &tokio_mpsc::UnboundedSender<DeferredResult>,
    result: DeferredResult,
) -> Result<()> {
    match result {
        DeferredResult::ConnectionReady(Ok(mut connected)) => {
            *connection_in_flight = false;
            finish_connection_setup(&mut connected.wire, state, language, deferred_tx).await;
            state.history.push(HistoryEntry::SystemInfo {
                message: format!("Connected to AppServer: {}", connected.ws_url),
            });
            *wire = Some(connected.wire);
        }
        DeferredResult::ConnectionReady(Err(e)) => {
            *connection_in_flight = false;
            state.connected = false;
            state.history.push(HistoryEntry::Error {
                message: format!("Connection failed: {e}"),
            });
        }
        other => handle_deferred_payload(state, strings, other),
    }
    Ok(())
}

fn handle_connected_deferred_result(
    connection_in_flight: &mut bool,
    state: &mut AppState,
    strings: &Strings,
    result: DeferredResult,
) {
    match result {
        DeferredResult::ConnectionReady(Ok(_connected)) => {
            *connection_in_flight = false;
        }
        DeferredResult::ConnectionReady(Err(e)) => {
            *connection_in_flight = false;
            state.history.push(HistoryEntry::Error {
                message: format!("Connection failed: {e}"),
            });
        }
        other => handle_deferred_payload(state, strings, other),
    }
}

fn handle_deferred_payload(state: &mut AppState, strings: &Strings, result: DeferredResult) {
    match result {
        DeferredResult::ModelCatalogLoaded(Ok(value)) => {
            let (models, error) = parse_model_catalog(&value);
            if let Some(err) = error {
                state.model_cache = ModelCacheState::Error(err.clone());
                if let Some(picker) = state.model_picker.as_mut() {
                    picker.loading = false;
                    picker.error = Some(err);
                    picker.models.clear();
                    picker.reasoning_options.clear();
                }
            } else {
                state.model_cache = ModelCacheState::Ready(models.clone());
                let current_model_label = state
                    .current_model_override
                    .as_deref()
                    .or(state.workspace_model.as_deref())
                    .unwrap_or("Default")
                    .to_string();
                let current_reasoning = effective_reasoning_value(state);
                if let Some(picker) = state.model_picker.as_mut() {
                    picker.loading = false;
                    picker.error = None;
                    picker.models = vec![default_model_item()];
                    picker.models.extend(models);
                    picker.reasoning_options = reasoning_options_for_model(
                        &current_model_label,
                        &picker.models,
                        current_reasoning,
                    );
                    if picker.selected >= picker.reasoning_options.len() + picker.models.len() {
                        picker.selected = 0;
                    }
                }
            }
        }
        DeferredResult::ModelCatalogLoaded(Err(e)) => {
            let msg = format!("Failed to load models: {e}");
            state.model_cache = ModelCacheState::Error(msg.clone());
            if let Some(picker) = state.model_picker.as_mut() {
                picker.loading = false;
                picker.error = Some(msg);
                picker.models.clear();
                picker.reasoning_options.clear();
            }
        }
        DeferredResult::SkillsListLoaded(Ok(value)) => {
            match serde_json::from_value::<wire::types::SkillsListResult>(value) {
                Ok(result) => {
                    state.skill_cache = SkillCacheState::Ready(result.skills.clone());
                    if let Some(picker) = state.skills_picker.as_mut() {
                        picker.loading = false;
                        picker.error = None;
                        picker.skills = result.skills;
                        if picker.selected >= picker.skills.len() {
                            picker.selected = 0;
                        }
                    }
                }
                Err(e) => {
                    let msg = format!("Failed to parse skills: {e}");
                    state.skill_cache = SkillCacheState::Error(msg.clone());
                    if let Some(picker) = state.skills_picker.as_mut() {
                        picker.loading = false;
                        picker.error = Some(msg);
                    }
                }
            }
        }
        DeferredResult::SkillsListLoaded(Err(e)) => {
            let msg = format!("Failed to load skills: {e}");
            state.skill_cache = SkillCacheState::Error(msg.clone());
            if let Some(picker) = state.skills_picker.as_mut() {
                picker.loading = false;
                picker.error = Some(msg);
                picker.skills.clear();
            }
        }
        DeferredResult::ThreadListLoaded(Ok(value)) => {
            let threads = parse_thread_list(&value);
            if let Some(picker) = state.thread_picker.as_mut() {
                picker.threads = threads;
                picker.loading = false;
            }
        }
        DeferredResult::ThreadListLoaded(Err(e)) => {
            if let Some(picker) = state.thread_picker.as_mut() {
                picker.loading = false;
                picker.error = Some(format!("Failed to load sessions: {e}"));
            }
        }
        DeferredResult::ThreadHistoryLoaded(Ok(data)) => {
            replay_thread_history(state, &data);
            let label = state
                .current_thread_name
                .as_deref()
                .or(state.current_thread_id.as_deref())
                .unwrap_or("?");
            let message = format!("{} {label}", strings.session_loaded_prefix);
            let is_duplicate = matches!(
                state.history.last(),
                Some(HistoryEntry::SystemInfo { message: existing }) if existing == &message
            );
            if !is_duplicate {
                state.history.push(HistoryEntry::SystemInfo { message });
            }
        }
        DeferredResult::ThreadHistoryLoaded(Err(e)) => {
            state.history.push(HistoryEntry::Error {
                message: format!("Failed to load thread history: {e}"),
            });
        }
        DeferredResult::ConnectionReady(_) => {}
    }
}

/// Dispatch a ThreadPickerAction returned from the input router.
async fn handle_thread_picker_action(
    wire: &mut WireClient,
    state: &mut AppState,
    deferred_tx: &tokio_mpsc::UnboundedSender<DeferredResult>,
    action: InputAction,
) -> Result<()> {
    match action {
        InputAction::ThreadPickerAction(ThreadPickerOp::Close) => {
            state.active_overlay = None;
            state.thread_picker = None;
        }
        InputAction::ThreadPickerAction(ThreadPickerOp::Resume) => {
            let selected = state
                .thread_picker
                .as_ref()
                .and_then(|p| p.threads.get(p.selected))
                .map(|t| (t.id.clone(), t.display_name.clone()));
            if let Some((id, display_name)) = selected {
                state.active_overlay = None;
                state.thread_picker = None;
                state.history.clear();
                state.plan = None;
                state.subagent_entries.clear();
                state.streaming.clear();
                state.token_tracker.reset();
                wire.send_request("thread/resume", serde_json::json!({ "threadId": id }))
                    .await?;
                state.current_thread_id = Some(id.clone());
                state.current_thread_name = display_name;
                // Fire async thread/read; result handled via deferred channel.
                let (_, rx) = wire
                    .send_request(
                        "thread/read",
                        serde_json::json!({ "threadId": id, "includeTurns": true }),
                    )
                    .await?;
                let tx = deferred_tx.clone();
                tokio::spawn(async move {
                    let result = rx
                        .await
                        .unwrap_or_else(|_| Err(anyhow::anyhow!("response dropped")));
                    let _ = tx.send(DeferredResult::ThreadHistoryLoaded(result));
                });
            }
        }
        InputAction::ThreadPickerAction(ThreadPickerOp::Archive) => {
            let thread_id = state
                .thread_picker
                .as_ref()
                .and_then(|p| p.threads.get(p.selected))
                .map(|t| t.id.clone());
            if let Some(id) = thread_id {
                wire.send_request("thread/archive", serde_json::json!({ "threadId": id }))
                    .await?;
                // Remove from local list immediately for instant feedback.
                if let Some(picker) = state.thread_picker.as_mut() {
                    if !picker.threads.is_empty() {
                        picker.threads.remove(picker.selected);
                        if picker.selected >= picker.threads.len() && picker.selected > 0 {
                            picker.selected -= 1;
                        }
                    }
                }
            }
        }
        InputAction::ThreadPickerAction(ThreadPickerOp::Delete) => {
            let thread_id = state
                .thread_picker
                .as_ref()
                .and_then(|p| p.threads.get(p.selected))
                .map(|t| t.id.clone());
            if let Some(id) = thread_id {
                wire.send_request("thread/delete", serde_json::json!({ "threadId": id }))
                    .await?;
                if let Some(picker) = state.thread_picker.as_mut() {
                    if !picker.threads.is_empty() {
                        picker.threads.remove(picker.selected);
                        if picker.selected >= picker.threads.len() && picker.selected > 0 {
                            picker.selected -= 1;
                        }
                    }
                }
            }
        }
        _ => {}
    }
    Ok(())
}

async fn open_skills_picker(
    wire: &mut WireClient,
    state: &mut AppState,
    deferred_tx: &tokio_mpsc::UnboundedSender<DeferredResult>,
) -> Result<()> {
    if !wire.capabilities.skills_management.unwrap_or(false) {
        state.history.push(HistoryEntry::Error {
            message: "Skills management is not available on this server.".to_string(),
        });
        return Ok(());
    }

    let (loading, skills, error) = match &state.skill_cache {
        SkillCacheState::Ready(skills) => (false, skills.clone(), None),
        SkillCacheState::Error(err) => (false, Vec::new(), Some(err.clone())),
        SkillCacheState::Loading => (true, Vec::new(), None),
        SkillCacheState::Idle => {
            state.skill_cache = SkillCacheState::Loading;
            if let Err(e) = spawn_skills_list_load(wire, deferred_tx).await {
                let msg = format!("Failed to load skills: {e}");
                state.skill_cache = SkillCacheState::Error(msg.clone());
                (false, Vec::new(), Some(msg))
            } else {
                (true, Vec::new(), None)
            }
        }
    };

    state.skills_picker = Some(SkillsPickerState {
        skills,
        selected: 0,
        scroll_offset: 0,
        loading,
        error,
        search: String::new(),
    });
    state.active_overlay = Some(OverlayKind::SkillsPicker);
    Ok(())
}

async fn handle_skills_picker_action(
    wire: &mut WireClient,
    state: &mut AppState,
    deferred_tx: &tokio_mpsc::UnboundedSender<DeferredResult>,
    action: InputAction,
) -> Result<()> {
    match action {
        InputAction::SkillsPickerAction(SkillsPickerOp::Close) => {
            state.active_overlay = None;
            state.skills_picker = None;
        }
        InputAction::SkillsPickerAction(SkillsPickerOp::Toggle) => {
            if !wire.capabilities.skills_management.unwrap_or(false) {
                if let Some(picker) = state.skills_picker.as_mut() {
                    picker.error = Some("Skills management is not available.".to_string());
                }
                return Ok(());
            }

            let Some(name) = input_router::selected_skill_name(state) else {
                return Ok(());
            };
            let next_enabled = state
                .skills_picker
                .as_ref()
                .and_then(|picker| picker.skills.iter().find(|skill| skill.name == name))
                .map(|skill| !skill.enabled)
                .unwrap_or(true);

            let result = wire
                .request::<wire::types::SkillsSetEnabledResult>(
                    "skills/setEnabled",
                    serde_json::json!({
                        "name": name,
                        "enabled": next_enabled
                    }),
                )
                .await;
            match result {
                Ok(result) => {
                    update_skill_cache_entry(state, result.skill);
                    if let Err(e) = spawn_skills_list_load(wire, deferred_tx).await {
                        if let Some(picker) = state.skills_picker.as_mut() {
                            picker.error = Some(format!("Changed skill, but refresh failed: {e}"));
                        }
                    }
                }
                Err(e) => {
                    if let Some(picker) = state.skills_picker.as_mut() {
                        picker.error = Some(format!("Failed to update skill: {e}"));
                    }
                }
            }
        }
        _ => {}
    }
    Ok(())
}

fn update_skill_cache_entry(state: &mut AppState, updated: wire::types::SkillInfo) {
    if let SkillCacheState::Ready(skills) = &mut state.skill_cache {
        if let Some(skill) = skills.iter_mut().find(|skill| skill.name == updated.name) {
            *skill = updated.clone();
        } else {
            skills.push(updated.clone());
        }
    }

    if let Some(picker) = state.skills_picker.as_mut() {
        if let Some(skill) = picker
            .skills
            .iter_mut()
            .find(|skill| skill.name == updated.name)
        {
            *skill = updated;
        } else {
            picker.skills.push(updated);
        }
        picker.loading = false;
        picker.error = None;
    }
}

fn open_permissions_picker(state: &mut AppState, strings: &Strings) {
    let options = permission_options(strings);
    let selected = selected_permission_index(state, &options);
    state.permissions_picker = Some(PermissionsPickerState {
        options,
        selected,
        error: None,
    });
    state.active_overlay = Some(OverlayKind::PermissionsPicker);
}

fn permission_options(strings: &Strings) -> Vec<PermissionOption> {
    vec![
        PermissionOption {
            id: "default".to_string(),
            label: strings.permissions_default_label.to_string(),
            description: strings.permissions_default_desc.to_string(),
            approval_policy: "default".to_string(),
            require_approval_outside_workspace: None,
        },
        PermissionOption {
            id: "autoApprove".to_string(),
            label: strings.permissions_auto_approve_label.to_string(),
            description: strings.permissions_auto_approve_desc.to_string(),
            approval_policy: "autoApprove".to_string(),
            require_approval_outside_workspace: None,
        },
        PermissionOption {
            id: "interrupt".to_string(),
            label: strings.permissions_interrupt_label.to_string(),
            description: strings.permissions_interrupt_desc.to_string(),
            approval_policy: "interrupt".to_string(),
            require_approval_outside_workspace: None,
        },
        PermissionOption {
            id: "workspaceOnly".to_string(),
            label: strings.permissions_workspace_only_label.to_string(),
            description: strings.permissions_workspace_only_desc.to_string(),
            approval_policy: "default".to_string(),
            require_approval_outside_workspace: Some(false),
        },
        PermissionOption {
            id: "askOutsideWorkspace".to_string(),
            label: strings.permissions_ask_outside_label.to_string(),
            description: strings.permissions_ask_outside_desc.to_string(),
            approval_policy: "default".to_string(),
            require_approval_outside_workspace: Some(true),
        },
    ]
}

fn selected_permission_index(state: &AppState, options: &[PermissionOption]) -> usize {
    let policy = state
        .pending_approval_policy
        .as_deref()
        .or(state.current_approval_policy.as_deref())
        .unwrap_or("default");
    let outside = state
        .pending_require_approval_outside_workspace
        .or(state.current_require_approval_outside_workspace);

    options
        .iter()
        .position(|option| {
            option.approval_policy == policy && option.require_approval_outside_workspace == outside
        })
        .unwrap_or(0)
}

async fn handle_permissions_picker_action(
    wire: &mut WireClient,
    state: &mut AppState,
    action: InputAction,
) -> Result<()> {
    match action {
        InputAction::PermissionsPickerAction(PermissionsPickerOp::Close) => {
            state.active_overlay = None;
            state.permissions_picker = None;
        }
        InputAction::PermissionsPickerAction(PermissionsPickerOp::Apply) => {
            if let Some(thread_id) = state.current_thread_id.clone() {
                if !wire.capabilities.config_override.unwrap_or(false) {
                    if let Some(picker) = state.permissions_picker.as_mut() {
                        picker.error = Some(
                            "Permission updates are not available on this server.".to_string(),
                        );
                    }
                    return Ok(());
                }
                let Some(option) = input_router::selected_permission_option(state) else {
                    return Ok(());
                };
                if let Err(e) =
                    apply_thread_permission_option(wire, state, &thread_id, &option).await
                {
                    if let Some(picker) = state.permissions_picker.as_mut() {
                        picker.error = Some(e);
                    }
                    return Ok(());
                }
                state.history.push(HistoryEntry::SystemInfo {
                    message: format!("Permissions set: {}", option.label),
                });
            } else {
                apply_pending_permission_selection(state);
            }
            state.active_overlay = None;
            state.permissions_picker = None;
        }
        _ => {}
    }
    Ok(())
}

fn apply_pending_permission_selection(state: &mut AppState) {
    let Some(option) = input_router::selected_permission_option(state) else {
        return;
    };
    state.pending_approval_policy = Some(option.approval_policy.clone());
    state.pending_require_approval_outside_workspace = option.require_approval_outside_workspace;
    state.current_approval_policy = Some(option.approval_policy.clone());
    state.current_require_approval_outside_workspace = option.require_approval_outside_workspace;
    state.history.push(HistoryEntry::SystemInfo {
        message: format!(
            "Permissions will apply to the next thread: {}",
            option.label
        ),
    });
    state.active_overlay = None;
    state.permissions_picker = None;
}

async fn apply_thread_permission_option(
    wire: &mut WireClient,
    state: &mut AppState,
    thread_id: &str,
    option: &PermissionOption,
) -> Result<(), String> {
    let read = wire
        .request::<serde_json::Value>(
            "thread/read",
            serde_json::json!({ "threadId": thread_id, "includeTurns": false }),
        )
        .await
        .map_err(|e| format!("Failed to read thread config: {e}"))?;

    let mut config = read
        .get("thread")
        .and_then(|thread| thread.get("configuration"))
        .cloned()
        .unwrap_or_else(|| serde_json::json!({}));
    if !config.is_object() {
        config = serde_json::json!({});
    }

    if let Some(cfg_obj) = config.as_object_mut() {
        upsert_or_remove_config_string(
            cfg_obj,
            "approvalPolicy",
            Some(option.approval_policy.clone()),
        );
        match option.require_approval_outside_workspace {
            Some(value) => {
                let key = find_existing_key(cfg_obj, "requireApprovalOutsideWorkspace")
                    .unwrap_or_else(|| "requireApprovalOutsideWorkspace".to_string());
                cfg_obj.insert(key, serde_json::Value::Bool(value));
            }
            None => {
                if let Some(key) = find_existing_key(cfg_obj, "requireApprovalOutsideWorkspace") {
                    cfg_obj.remove(&key);
                }
            }
        }
    }

    wire.send_request(
        "thread/config/update",
        serde_json::json!({ "threadId": thread_id, "config": config }),
    )
    .await
    .map_err(|e| format!("Failed to update permissions: {e}"))?;

    state.current_approval_policy = Some(option.approval_policy.clone());
    state.current_require_approval_outside_workspace = option.require_approval_outside_workspace;
    state.pending_approval_policy = None;
    state.pending_require_approval_outside_workspace = None;
    Ok(())
}

fn upsert_or_remove_config_string(
    cfg_obj: &mut serde_json::Map<String, serde_json::Value>,
    key_name: &str,
    value: Option<String>,
) {
    match value {
        Some(value) => {
            let key = find_existing_key(cfg_obj, key_name).unwrap_or_else(|| key_name.to_string());
            cfg_obj.insert(key, serde_json::Value::String(value));
        }
        None => {
            if let Some(key) = find_existing_key(cfg_obj, key_name) {
                cfg_obj.remove(&key);
            }
        }
    }
}

fn find_existing_key(
    cfg_obj: &serde_json::Map<String, serde_json::Value>,
    key_name: &str,
) -> Option<String> {
    cfg_obj
        .keys()
        .find(|key| key.eq_ignore_ascii_case(key_name))
        .cloned()
}

async fn handle_model_picker_action(
    wire: &mut WireClient,
    state: &mut AppState,
    action: InputAction,
) -> Result<()> {
    match action {
        InputAction::ModelPickerAction(ModelPickerOp::Close) => {
            state.active_overlay = None;
            state.model_picker = None;
        }
        InputAction::ModelPickerAction(ModelPickerOp::Apply) => {
            let selected = state.model_picker.as_ref().and_then(|p| {
                let reasoning_len = p.reasoning_options.len();
                if p.selected < reasoning_len {
                    p.reasoning_options
                        .get(p.selected)
                        .filter(|option| !option.disabled)
                        .map(|option| EitherPickerSelection::Reasoning(option.value))
                } else {
                    p.models
                        .get(p.selected.saturating_sub(reasoning_len))
                        .map(|model| EitherPickerSelection::Model(model.id.clone()))
                }
            });
            if let Some(EitherPickerSelection::Model(model_label)) = selected {
                let model_override = if model_label.eq_ignore_ascii_case("default") {
                    None
                } else {
                    Some(model_label)
                };
                if let Err(e) = apply_model_override(wire, state, model_override).await {
                    state.history.push(HistoryEntry::Error { message: e });
                }
            } else if let Some(EitherPickerSelection::Reasoning(reasoning)) = selected {
                if let Err(e) = apply_reasoning_override(wire, state, reasoning).await {
                    state.history.push(HistoryEntry::Error { message: e });
                }
            }
            state.active_overlay = None;
            state.model_picker = None;
        }
        _ => {}
    }
    Ok(())
}

enum EitherPickerSelection {
    Model(String),
    Reasoning(ReasoningQuickValue),
}

async fn apply_model_override(
    wire: &mut WireClient,
    state: &mut AppState,
    model: Option<String>,
) -> Result<(), String> {
    let workspace_update = wire
        .request::<serde_json::Value>(
            "workspace/config/update",
            serde_json::json!({ "model": model.clone() }),
        )
        .await
        .map_err(|e| format!("Failed to update workspace model: {e}"))?;
    state.workspace_model = workspace_update
        .get("model")
        .and_then(|v| v.as_str())
        .map(str::to_string);

    if let Some(thread_id) = state.current_thread_id.clone() {
        let read = wire
            .request::<serde_json::Value>(
                "thread/read",
                serde_json::json!({ "threadId": thread_id, "includeTurns": false }),
            )
            .await
            .map_err(|e| {
                state.current_model_override = model.clone();
                state.pending_model_override = None;
                format!("Workspace model updated, but failed to read thread config: {e}")
            })?;

        let mut config = read
            .get("thread")
            .and_then(|t| t.get("configuration"))
            .cloned()
            .unwrap_or_else(|| serde_json::json!({}));
        if !config.is_object() {
            config = serde_json::json!({});
        }

        if let Some(cfg_obj) = config.as_object_mut() {
            let existing_model_key = cfg_obj
                .keys()
                .find(|k| k.eq_ignore_ascii_case("model"))
                .cloned();
            if let Some(next) = model.clone() {
                let key = existing_model_key.unwrap_or_else(|| "model".to_string());
                cfg_obj.insert(key, serde_json::Value::String(next));
            } else if let Some(key) = existing_model_key {
                cfg_obj.remove(&key);
            }
        }

        wire.send_request(
            "thread/config/update",
            serde_json::json!({ "threadId": thread_id, "config": config }),
        )
        .await
        .map_err(|e| {
            state.current_model_override = model.clone();
            state.pending_model_override = None;
            format!("Workspace model updated, but failed to apply thread override: {e}")
        })?;

        state.current_model_override = model;
        state.pending_model_override = None;
    } else {
        state.pending_model_override = model;
        state.current_model_override = state.pending_model_override.clone();
    }
    Ok(())
}

async fn apply_reasoning_override(
    wire: &mut WireClient,
    state: &mut AppState,
    reasoning: ReasoningQuickValue,
) -> Result<(), String> {
    let payload = build_reasoning_payload(reasoning, effective_reasoning_config(state));
    let workspace_update = wire
        .request::<serde_json::Value>(
            "workspace/config/update",
            serde_json::json!({ "reasoning": payload.clone() }),
        )
        .await
        .map_err(|e| format!("Failed to update workspace thinking: {e}"))?;
    state.workspace_reasoning = workspace_update
        .get("reasoning")
        .and_then(parse_reasoning_config);

    if let Some(thread_id) = state.current_thread_id.clone() {
        let read = wire
            .request::<serde_json::Value>(
                "thread/read",
                serde_json::json!({ "threadId": thread_id, "includeTurns": false }),
            )
            .await
            .map_err(|e| {
                state.current_reasoning = state.workspace_reasoning.clone();
                state.pending_reasoning = None;
                format!("Workspace thinking updated, but failed to read thread config: {e}")
            })?;

        let mut config = read
            .get("thread")
            .and_then(|t| t.get("configuration"))
            .cloned()
            .unwrap_or_else(|| serde_json::json!({}));
        if !config.is_object() {
            config = serde_json::json!({});
        }

        if let Some(cfg_obj) = config.as_object_mut() {
            let existing_reasoning_key = cfg_obj
                .keys()
                .find(|k| k.eq_ignore_ascii_case("reasoning"))
                .cloned();
            if let Some(next) = payload.clone() {
                let key = existing_reasoning_key.unwrap_or_else(|| "reasoning".to_string());
                cfg_obj.insert(key, next);
            } else if let Some(key) = existing_reasoning_key {
                cfg_obj.remove(&key);
            }
        }

        wire.send_request(
            "thread/config/update",
            serde_json::json!({ "threadId": thread_id, "config": config }),
        )
        .await
        .map_err(|e| {
            state.current_reasoning = state.workspace_reasoning.clone();
            state.pending_reasoning = None;
            format!("Workspace thinking updated, but failed to apply thread override: {e}")
        })?;

        state.current_reasoning = payload.as_ref().and_then(parse_reasoning_config);
        state.pending_reasoning = None;
    } else {
        state.pending_reasoning = payload.as_ref().and_then(parse_reasoning_config);
        state.current_reasoning = state.pending_reasoning.clone();
    }
    Ok(())
}

fn default_model_item() -> ModelCatalogItem {
    ModelCatalogItem {
        id: "Default".to_string(),
        reasoning: None,
    }
}

fn parse_reasoning_quick_value(raw: &str) -> Option<ReasoningQuickValue> {
    match raw.trim().to_ascii_lowercase().replace('_', "-").as_str() {
        "default" => Some(ReasoningQuickValue::Default),
        "off" | "disabled" | "disable" | "none" => Some(ReasoningQuickValue::Off),
        "low" => Some(ReasoningQuickValue::Low),
        "medium" => Some(ReasoningQuickValue::Medium),
        "high" => Some(ReasoningQuickValue::High),
        "extrahigh" | "extra-high" | "extra high" | "xhigh" => Some(ReasoningQuickValue::ExtraHigh),
        _ => None,
    }
}

fn parse_reasoning_config(value: &serde_json::Value) -> Option<ReasoningConfigState> {
    let obj = value.as_object()?;
    let enabled = get_case_insensitive(obj, "enabled")
        .and_then(|v| v.as_bool())
        .unwrap_or(false);
    let effort = get_case_insensitive(obj, "effort")
        .and_then(|v| v.as_str())
        .and_then(parse_reasoning_quick_value)
        .filter(|effort| {
            *effort != ReasoningQuickValue::Default && *effort != ReasoningQuickValue::Off
        })
        .unwrap_or(ReasoningQuickValue::Medium);
    let output = get_case_insensitive(obj, "output")
        .and_then(|v| v.as_str())
        .filter(|v| !v.trim().is_empty())
        .unwrap_or("full")
        .to_string();
    Some(ReasoningConfigState {
        enabled,
        effort,
        output,
    })
}

fn reasoning_config_to_json(config: &ReasoningConfigState) -> serde_json::Value {
    serde_json::json!({
        "enabled": config.enabled,
        "effort": config.effort.as_wire().unwrap_or("medium"),
        "output": config.output,
    })
}

fn effective_reasoning_config(state: &AppState) -> ReasoningConfigState {
    state
        .current_reasoning
        .clone()
        .or_else(|| state.pending_reasoning.clone())
        .or_else(|| state.workspace_reasoning.clone())
        .unwrap_or(ReasoningConfigState {
            enabled: false,
            effort: ReasoningQuickValue::Medium,
            output: "full".to_string(),
        })
}

fn effective_reasoning_value(state: &AppState) -> ReasoningQuickValue {
    let config = effective_reasoning_config(state);
    if config.enabled {
        config.effort
    } else {
        ReasoningQuickValue::Off
    }
}

fn build_reasoning_payload(
    value: ReasoningQuickValue,
    current: ReasoningConfigState,
) -> Option<serde_json::Value> {
    match value {
        ReasoningQuickValue::Default => None,
        ReasoningQuickValue::Off => Some(reasoning_config_to_json(&ReasoningConfigState {
            enabled: false,
            effort: current.effort,
            output: current.output,
        })),
        effort => Some(reasoning_config_to_json(&ReasoningConfigState {
            enabled: true,
            effort,
            output: current.output,
        })),
    }
}

fn reasoning_options_for_model(
    model_id: &str,
    models: &[ModelCatalogItem],
    current: ReasoningQuickValue,
) -> Vec<ReasoningPickerOption> {
    let mut options = vec![ReasoningPickerOption {
        value: ReasoningQuickValue::Default,
        label: ReasoningQuickValue::Default.label().to_string(),
        disabled: false,
    }];

    let capability = models
        .iter()
        .find(|model| model.id.eq_ignore_ascii_case(model_id))
        .and_then(|model| model.reasoning.as_ref());

    if let Some(capability) = capability {
        if capability.supports_disable {
            options.push(ReasoningPickerOption {
                value: ReasoningQuickValue::Off,
                label: ReasoningQuickValue::Off.label().to_string(),
                disabled: false,
            });
        } else {
            options.push(ReasoningPickerOption {
                value: ReasoningQuickValue::Off,
                label: ReasoningQuickValue::Off.label().to_string(),
                disabled: true,
            });
        }

        for effort in &capability.supported_efforts {
            if !options.iter().any(|option| option.value == effort.value) {
                options.push(effort.clone());
            }
        }
        return options;
    }

    if current != ReasoningQuickValue::Default {
        options.push(ReasoningPickerOption {
            value: current,
            label: current.label().to_string(),
            disabled: false,
        });
    }
    options
}

fn get_case_insensitive<'a>(
    obj: &'a serde_json::Map<String, serde_json::Value>,
    key: &str,
) -> Option<&'a serde_json::Value> {
    obj.iter()
        .find(|(candidate, _)| candidate.eq_ignore_ascii_case(key))
        .map(|(_, value)| value)
}

/// Parse the thread/list response into a Vec of ThreadEntry.
fn parse_thread_list(result: &serde_json::Value) -> Vec<ThreadEntry> {
    result
        .get("data")
        .and_then(|v| v.as_array())
        .map(|arr| {
            arr.iter()
                .filter_map(|t| {
                    Some(ThreadEntry {
                        id: t.get("id")?.as_str()?.to_string(),
                        display_name: t
                            .get("displayName")
                            .and_then(|v| v.as_str())
                            .map(str::to_string),
                        status: t
                            .get("status")
                            .and_then(|v| v.as_str())
                            .unwrap_or("unknown")
                            .to_string(),
                        origin_channel: t
                            .get("originChannel")
                            .and_then(|v| v.as_str())
                            .unwrap_or("")
                            .to_string(),
                        last_active_at: t
                            .get("lastActiveAt")
                            .and_then(|v| v.as_str())
                            .unwrap_or("")
                            .to_string(),
                    })
                })
                .collect()
        })
        .unwrap_or_default()
}

fn parse_model_catalog(result: &serde_json::Value) -> (Vec<ModelCatalogItem>, Option<String>) {
    let success = result
        .get("success")
        .and_then(|v| v.as_bool())
        .unwrap_or(false);
    if !success {
        let message = result
            .get("errorMessage")
            .and_then(|v| v.as_str())
            .or_else(|| result.get("errorCode").and_then(|v| v.as_str()))
            .unwrap_or("Model catalog request failed.")
            .to_string();
        return (vec![], Some(message));
    }

    let mut models: Vec<ModelCatalogItem> = result
        .get("models")
        .and_then(|v| v.as_array())
        .map(|arr| {
            arr.iter()
                .filter_map(|item| {
                    let id = item.get("id").and_then(|v| v.as_str())?.trim();
                    if id.is_empty() {
                        return None;
                    }
                    Some(ModelCatalogItem {
                        id: id.to_string(),
                        reasoning: parse_model_reasoning_capability(item.get("reasoning")),
                    })
                })
                .collect::<Vec<ModelCatalogItem>>()
        })
        .unwrap_or_default();

    models.sort_by_key(|a| a.id.to_ascii_lowercase());
    models.dedup_by(|a, b| a.id.eq_ignore_ascii_case(&b.id));
    (models, None)
}

fn parse_model_reasoning_capability(
    value: Option<&serde_json::Value>,
) -> Option<ModelReasoningCapability> {
    let obj = value?.as_object()?;
    let supports_disable = obj
        .get("supportsDisable")
        .and_then(|v| v.as_bool())
        .unwrap_or(true);
    let supported_efforts = obj
        .get("supportedEfforts")
        .and_then(|v| v.as_array())
        .map(|items| {
            items
                .iter()
                .filter_map(|item| {
                    let item_obj = item.as_object()?;
                    let effort = parse_reasoning_quick_value(item_obj.get("effort")?.as_str()?)?;
                    let label = item_obj
                        .get("label")
                        .and_then(|v| v.as_str())
                        .unwrap_or_else(|| effort.label())
                        .to_string();
                    Some(ReasoningPickerOption {
                        value: effort,
                        label,
                        disabled: false,
                    })
                })
                .collect::<Vec<_>>()
        })
        .unwrap_or_default();
    if supported_efforts.is_empty() {
        return None;
    }
    let default_effort = obj
        .get("defaultEffort")
        .and_then(|v| v.as_str())
        .and_then(parse_reasoning_quick_value)
        .unwrap_or(supported_efforts[0].value);
    Some(ModelReasoningCapability {
        supports_disable,
        supported_efforts,
        default_effort,
    })
}

fn stringify_history_args(value: &serde_json::Value) -> String {
    if let Some(s) = value.as_str() {
        s.to_string()
    } else {
        serde_json::to_string_pretty(value).unwrap_or_default()
    }
}

fn structured_invocation_history_result(payload: &serde_json::Value) -> Option<String> {
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

/// Parse a `thread/read` response (with `includeTurns: true`) and rebuild
/// `state.history` from the persisted items.
fn replay_thread_history(state: &mut AppState, data: &serde_json::Value) {
    // Sync thread displayName into state so the status bar is accurate.
    if let Some(name) = data
        .get("thread")
        .and_then(|t| t.get("displayName"))
        .and_then(|v| v.as_str())
    {
        state.current_thread_name = Some(name.to_string());
    }
    state.current_model_override = data
        .get("thread")
        .and_then(|t| t.get("configuration"))
        .and_then(|cfg| cfg.get("model"))
        .and_then(|v| v.as_str())
        .map(str::to_string);
    state.current_reasoning = data
        .get("thread")
        .and_then(|t| t.get("configuration"))
        .and_then(|cfg| cfg.get("reasoning"))
        .and_then(parse_reasoning_config);
    state.current_goal = data
        .get("thread")
        .and_then(|t| t.get("goal"))
        .and_then(|goal| serde_json::from_value(goal.clone()).ok());

    let turns = match data
        .get("thread")
        .and_then(|t| t.get("turns"))
        .and_then(|v| v.as_array())
    {
        Some(t) => t,
        None => return,
    };

    for turn in turns {
        let items = match turn.get("items").and_then(|v| v.as_array()) {
            Some(i) => i,
            None => continue,
        };
        for item in items {
            let item_type = item.get("type").and_then(|v| v.as_str()).unwrap_or("");
            let payload = item
                .get("payload")
                .cloned()
                .unwrap_or(serde_json::Value::Null);

            match item_type {
                "userMessage" => {
                    if let Some(text) = payload.get("text").and_then(|v| v.as_str()) {
                        state.history.push(HistoryEntry::UserMessage {
                            text: text.to_string(),
                        });
                    }
                }
                "agentMessage" => {
                    if let Some(text) = payload.get("text").and_then(|v| v.as_str()) {
                        state.history.push(HistoryEntry::AgentMessage {
                            text: text.to_string(),
                        });
                    }
                }
                "toolCall" => {
                    let call_id = payload
                        .get("callId")
                        .and_then(|v| v.as_str())
                        .unwrap_or("")
                        .to_string();
                    let name = payload
                        .get("toolName")
                        .and_then(|v| v.as_str())
                        .unwrap_or("unknown")
                        .to_string();
                    let args = payload
                        .get("arguments")
                        .map(stringify_history_args)
                        .unwrap_or_default();
                    let success = payload
                        .get("success")
                        .and_then(|v| v.as_bool())
                        .unwrap_or(true);
                    state.history.push(HistoryEntry::ToolCall {
                        call_id,
                        name,
                        args,
                        result: None,
                        success,
                        duration: None,
                    });
                }
                "pluginFunctionCall" | "dynamicToolCall" => {
                    let call_id = payload
                        .get("callId")
                        .and_then(|v| v.as_str())
                        .unwrap_or("")
                        .to_string();
                    let name = payload
                        .get(if item_type == "pluginFunctionCall" {
                            "functionName"
                        } else {
                            "toolName"
                        })
                        .or_else(|| payload.get("toolName"))
                        .or_else(|| payload.get("functionName"))
                        .and_then(|v| v.as_str())
                        .unwrap_or("unknown")
                        .to_string();
                    let args = payload
                        .get("arguments")
                        .map(stringify_history_args)
                        .unwrap_or_default();
                    let success = payload
                        .get("success")
                        .and_then(|v| v.as_bool())
                        .unwrap_or(true);
                    state.history.push(HistoryEntry::ToolCall {
                        call_id,
                        name,
                        args,
                        result: structured_invocation_history_result(&payload),
                        success,
                        duration: None,
                    });
                }
                "toolResult" => {
                    let call_id = payload
                        .get("callId")
                        .and_then(|v| v.as_str())
                        .unwrap_or("")
                        .to_string();
                    let result_text = payload
                        .get("result")
                        .and_then(|v| v.as_str())
                        .map(str::to_string);
                    let success = payload
                        .get("success")
                        .and_then(|v| v.as_bool())
                        .unwrap_or(true);
                    for entry in state.history.iter_mut().rev() {
                        if let HistoryEntry::ToolCall {
                            call_id: ref id,
                            result: ref mut r,
                            success: ref mut s,
                            ..
                        } = entry
                        {
                            if id == &call_id && r.is_none() {
                                *r = result_text;
                                *s = success;
                                break;
                            }
                        }
                    }
                }
                "error" => {
                    let msg = payload
                        .get("message")
                        .or_else(|| payload.get("text"))
                        .and_then(|v| v.as_str())
                        .unwrap_or("Unknown error")
                        .to_string();
                    state.history.push(HistoryEntry::Error { message: msg });
                }
                _ => {}
            }
        }
    }
}

async fn handle_slash_command(
    wire: &mut WireClient,
    state: &mut AppState,
    strings: &Strings,
    deferred_tx: &tokio_mpsc::UnboundedSender<DeferredResult>,
    cmd: ParsedSlashCommand,
) -> Result<bool> {
    if let Some(local_cmd) = commands::to_local_command(&cmd) {
        return handle_local_slash_command(wire, state, strings, deferred_tx, local_cmd).await;
    }

    if state
        .server_commands
        .iter()
        .any(|server_cmd| server_cmd.name.eq_ignore_ascii_case(&cmd.name))
    {
        execute_server_command(wire, state, &cmd).await?;
    } else {
        let name = cmd.name.trim_start_matches('/');
        state.history.push(HistoryEntry::Error {
            message: format!("Unknown command: /{name}."),
        });
    }
    Ok(false)
}

async fn handle_local_slash_command(
    wire: &mut WireClient,
    state: &mut AppState,
    strings: &Strings,
    deferred_tx: &tokio_mpsc::UnboundedSender<DeferredResult>,
    cmd: LocalSlashCommand,
) -> Result<bool> {
    match cmd {
        LocalSlashCommand::Quit => return Ok(true),
        LocalSlashCommand::Clear => {
            state.history.clear();
            state.plan = None;
            state.subagent_entries.clear();
        }
        LocalSlashCommand::Plan => {
            if let Some(thread_id) = state.current_thread_id.clone() {
                wire.send_request(
                    "thread/mode/set",
                    serde_json::json!({ "threadId": thread_id, "mode": "plan" }),
                )
                .await?;
                state.mode = AgentMode::Plan;
            }
        }
        LocalSlashCommand::Agent => {
            if let Some(thread_id) = state.current_thread_id.clone() {
                wire.send_request(
                    "thread/mode/set",
                    serde_json::json!({ "threadId": thread_id, "mode": "agent" }),
                )
                .await?;
                state.mode = AgentMode::Agent;
            }
        }
        LocalSlashCommand::Goal { argument_text } => {
            handle_goal_command(wire, state, strings, argument_text).await?;
        }
        LocalSlashCommand::Model { model_name } => {
            if !wire.capabilities.model_catalog_management.unwrap_or(false)
                || !wire
                    .capabilities
                    .workspace_config_management
                    .unwrap_or(false)
            {
                state.history.push(HistoryEntry::Error {
                    message: strings.feature_unavailable.to_string(),
                });
                return Ok(false);
            }

            if let Some(model_input) = model_name {
                let model = model_input.trim();
                if model.is_empty() {
                    state.history.push(HistoryEntry::Error {
                        message: strings.model_usage.to_string(),
                    });
                    return Ok(false);
                }
                let next = if model.eq_ignore_ascii_case("default") {
                    None
                } else {
                    Some(model.to_string())
                };
                match apply_model_override(wire, state, next.clone()).await {
                    Ok(()) => {
                        let message = match next {
                            Some(m) => strings.model_updated_to.replace("{}", &m),
                            None => strings.model_updated_default.to_string(),
                        };
                        state.history.push(HistoryEntry::SystemInfo { message });
                    }
                    Err(e) => {
                        state.history.push(HistoryEntry::Error { message: e });
                    }
                }
                return Ok(false);
            }

            let (loading, models, error) = match &state.model_cache {
                ModelCacheState::Loading => (true, vec![default_model_item()], None),
                ModelCacheState::Ready(cached) => {
                    let mut all = vec![default_model_item()];
                    all.extend(cached.iter().cloned());
                    (false, all, None)
                }
                ModelCacheState::Error(err) => (false, vec![], Some(err.clone())),
                ModelCacheState::Idle => (true, vec![default_model_item()], None),
            };
            let reasoning_options = reasoning_options_for_model(
                state
                    .current_model_override
                    .as_deref()
                    .or(state.workspace_model.as_deref())
                    .unwrap_or("Default"),
                &models,
                effective_reasoning_value(state),
            );
            state.model_picker = Some(ModelPickerState {
                models,
                reasoning_options,
                selected: 0,
                loading,
                error,
            });
            state.active_overlay = Some(OverlayKind::ModelPicker);
        }
        LocalSlashCommand::Thinking { value } => {
            if !wire
                .capabilities
                .workspace_config_management
                .unwrap_or(false)
            {
                state.history.push(HistoryEntry::Error {
                    message: strings.feature_unavailable.to_string(),
                });
                return Ok(false);
            }

            let Some(value) = value else {
                state.history.push(HistoryEntry::Error {
                    message: strings.thinking_usage.to_string(),
                });
                return Ok(false);
            };

            let Some(reasoning) = parse_reasoning_quick_value(&value) else {
                state.history.push(HistoryEntry::Error {
                    message: strings.thinking_usage.to_string(),
                });
                return Ok(false);
            };

            match apply_reasoning_override(wire, state, reasoning).await {
                Ok(()) => {
                    let message = if reasoning == ReasoningQuickValue::Default {
                        strings.thinking_updated_default.to_string()
                    } else {
                        strings.thinking_updated_to.replace("{}", reasoning.label())
                    };
                    state.history.push(HistoryEntry::SystemInfo { message });
                }
                Err(e) => {
                    state.history.push(HistoryEntry::Error { message: e });
                }
            }
            return Ok(false);
        }
        LocalSlashCommand::Provider { provider_id } => {
            if !wire.capabilities.provider_management.unwrap_or(false)
                || !wire
                    .capabilities
                    .workspace_config_management
                    .unwrap_or(false)
            {
                state.history.push(HistoryEntry::Error {
                    message: strings.feature_unavailable.to_string(),
                });
                return Ok(false);
            }

            if let Some(provider_id) = provider_id {
                let provider_id = provider_id.trim();
                if provider_id.is_empty() {
                    state.history.push(HistoryEntry::Error {
                        message: "Usage: /provider [provider-id]".to_string(),
                    });
                    return Ok(false);
                }
                let result = wire
                    .request::<serde_json::Value>(
                        "workspace/config/update",
                        serde_json::json!({ "providerId": provider_id }),
                    )
                    .await;
                match result {
                    Ok(value) => {
                        state.workspace_provider_id = value
                            .get("providerId")
                            .and_then(|v| v.as_str())
                            .map(str::to_string)
                            .or_else(|| Some(provider_id.to_string()));
                        state.model_cache =
                            if wire.capabilities.model_catalog_management.unwrap_or(false) {
                                ModelCacheState::Loading
                            } else {
                                ModelCacheState::Idle
                            };
                        if wire.capabilities.model_catalog_management.unwrap_or(false) {
                            if let Err(e) = spawn_model_catalog_load(
                                wire,
                                deferred_tx,
                                state.workspace_provider_id.clone(),
                            )
                            .await
                            {
                                state.model_cache =
                                    ModelCacheState::Error(format!("Failed to load models: {e}"));
                            }
                        }
                        state.history.push(HistoryEntry::SystemInfo {
                            message: format!("Workspace provider set to {provider_id}."),
                        });
                    }
                    Err(e) => {
                        state.history.push(HistoryEntry::Error {
                            message: format!("Failed to update provider: {e}"),
                        });
                    }
                }
                return Ok(false);
            }

            match wire
                .request::<wire::types::ProviderListResult>("provider/list", serde_json::json!({}))
                .await
            {
                Ok(result) => {
                    let current = state.workspace_provider_id.as_deref().unwrap_or("openai");
                    let mut lines = vec!["Configured providers:".to_string()];
                    for provider in result.providers {
                        let selected = if provider.id.eq_ignore_ascii_case(current) {
                            " *"
                        } else {
                            ""
                        };
                        let secret = if provider.has_api_key {
                            "key"
                        } else {
                            "no-key"
                        };
                        lines.push(format!(
                            "- {} ({}, {}){}",
                            provider.id, provider.protocol, secret, selected
                        ));
                    }
                    lines.push("Use /provider <id> to select one.".to_string());
                    state.history.push(HistoryEntry::SystemInfo {
                        message: lines.join("\n"),
                    });
                }
                Err(e) => {
                    state.history.push(HistoryEntry::Error {
                        message: format!("Failed to list providers: {e}"),
                    });
                }
            }
        }
        LocalSlashCommand::Skills => {
            open_skills_picker(wire, state, deferred_tx).await?;
        }
        LocalSlashCommand::Permissions => {
            open_permissions_picker(state, strings);
        }
        LocalSlashCommand::Sessions => {
            if !wire.capabilities.thread_management.unwrap_or(false) {
                state.history.push(HistoryEntry::Error {
                    message: strings.feature_unavailable.to_string(),
                });
                return Ok(false);
            }

            state.thread_picker = Some(ThreadPickerState {
                threads: vec![],
                selected: 0,
                loading: true,
                error: None,
            });
            state.active_overlay = Some(OverlayKind::ThreadPicker);

            let identity = build_identity(&state.workspace_path);
            let (_, rx) = wire
                .send_request("thread/list", serde_json::json!({ "identity": identity }))
                .await?;
            let tx = deferred_tx.clone();
            tokio::spawn(async move {
                let result = rx
                    .await
                    .unwrap_or_else(|_| Err(anyhow::anyhow!("response dropped")));
                let _ = tx.send(DeferredResult::ThreadListLoaded(result));
            });
        }
        LocalSlashCommand::Load { thread_id } => {
            if thread_id.is_empty() {
                state.history.push(HistoryEntry::Error {
                    message: "Usage: /load <thread-id>".to_string(),
                });
                return Ok(false);
            }
            let id = thread_id.clone();
            state.history.clear();
            state.plan = None;
            state.subagent_entries.clear();
            state.streaming.clear();
            state.token_tracker.reset();
            wire.send_request("thread/resume", serde_json::json!({ "threadId": id }))
                .await?;
            state.current_thread_id = Some(id.clone());

            let (_, rx) = wire
                .send_request(
                    "thread/read",
                    serde_json::json!({ "threadId": id, "includeTurns": true }),
                )
                .await?;
            let tx = deferred_tx.clone();
            tokio::spawn(async move {
                let result = rx
                    .await
                    .unwrap_or_else(|_| Err(anyhow::anyhow!("response dropped")));
                let _ = tx.send(DeferredResult::ThreadHistoryLoaded(result));
            });
        }
    }
    Ok(false)
}

async fn handle_goal_command(
    wire: &mut WireClient,
    state: &mut AppState,
    strings: &Strings,
    argument_text: String,
) -> Result<()> {
    if !wire.capabilities.thread_goals.unwrap_or(false) {
        state.history.push(HistoryEntry::Error {
            message: strings.feature_unavailable.to_string(),
        });
        return Ok(());
    }

    let args = argument_text.trim();
    if args.is_empty() {
        show_current_goal(wire, state).await?;
        return Ok(());
    }

    let command = args.to_ascii_lowercase();
    match command.as_str() {
        "pause" | "paused" => {
            let Some(thread_id) = state.current_thread_id.clone() else {
                state.history.push(HistoryEntry::Error {
                    message: "No active thread. Set a goal with /goal <objective> first."
                        .to_string(),
                });
                return Ok(());
            };
            let result: wire::types::ThreadGoalSetResult = wire
                .request(
                    "thread/goal/set",
                    serde_json::json!({ "threadId": thread_id, "status": "paused", "mode": "updateOnly" }),
                )
                .await?;
            state.current_goal = Some(result.goal.clone());
            state.history.push(HistoryEntry::SystemInfo {
                message: format_goal_summary("Goal paused", &result.goal),
            });
        }
        "resume" | "active" => {
            let Some(thread_id) = state.current_thread_id.clone() else {
                state.history.push(HistoryEntry::Error {
                    message: "No active thread. Set a goal with /goal <objective> first."
                        .to_string(),
                });
                return Ok(());
            };
            let result: wire::types::ThreadGoalSetResult = wire
                .request(
                    "thread/goal/set",
                    serde_json::json!({ "threadId": thread_id, "status": "active", "mode": "updateOnly" }),
                )
                .await?;
            state.current_goal = Some(result.goal.clone());
            state.history.push(HistoryEntry::SystemInfo {
                message: format_goal_summary("Goal resumed", &result.goal),
            });
        }
        "clear" => {
            let Some(thread_id) = state.current_thread_id.clone() else {
                state.history.push(HistoryEntry::Error {
                    message: "No active thread goal to clear.".to_string(),
                });
                return Ok(());
            };
            let result: wire::types::ThreadGoalClearResult = wire
                .request(
                    "thread/goal/clear",
                    serde_json::json!({ "threadId": thread_id }),
                )
                .await?;
            state.current_goal = None;
            let message = if result.cleared {
                "Goal cleared.".to_string()
            } else {
                "No active thread goal to clear.".to_string()
            };
            state.history.push(HistoryEntry::SystemInfo { message });
        }
        _ => {
            if state.current_thread_id.is_none() {
                create_thread(wire, state).await?;
            }
            let Some(thread_id) = state.current_thread_id.clone() else {
                state.history.push(HistoryEntry::Error {
                    message: "Failed to create thread for goal.".to_string(),
                });
                return Ok(());
            };
            let result: wire::types::ThreadGoalSetResult = wire
                .request(
                    "thread/goal/set",
                    serde_json::json!({ "threadId": thread_id, "objective": args }),
                )
                .await?;
            state.current_goal = Some(result.goal.clone());
            state.history.push(HistoryEntry::SystemInfo {
                message: format_goal_summary("Goal set", &result.goal),
            });
        }
    }

    Ok(())
}

async fn show_current_goal(wire: &mut WireClient, state: &mut AppState) -> Result<()> {
    let Some(thread_id) = state.current_thread_id.clone() else {
        state.history.push(HistoryEntry::SystemInfo {
            message: "Usage: /goal <objective>, /goal pause, /goal resume, /goal clear".to_string(),
        });
        return Ok(());
    };

    let result: wire::types::ThreadGoalGetResult = wire
        .request(
            "thread/goal/get",
            serde_json::json!({ "threadId": thread_id }),
        )
        .await?;
    state.current_goal = result.goal.clone();
    match result.goal {
        Some(goal) => state.history.push(HistoryEntry::SystemInfo {
            message: format_goal_summary("Current goal", &goal),
        }),
        None => state.history.push(HistoryEntry::SystemInfo {
            message: "No current goal. Use /goal <objective> to set one.".to_string(),
        }),
    }
    Ok(())
}

fn format_goal_summary(prefix: &str, goal: &wire::types::ThreadGoal) -> String {
    let usage = match goal.token_budget {
        Some(budget) => format!("{}/{} tokens", goal.tokens_used.total_tokens, budget),
        None => format!("{}s elapsed", goal.time_used_seconds),
    };
    format!(
        "{prefix}: [{}] {} ({usage})",
        goal.status,
        goal.objective.trim()
    )
}

async fn execute_server_command(
    wire: &mut WireClient,
    state: &mut AppState,
    cmd: &ParsedSlashCommand,
) -> Result<()> {
    if state.current_thread_id.is_none() {
        create_thread(wire, state).await?;
    }

    let thread_id = match &state.current_thread_id {
        Some(id) => id.clone(),
        None => {
            state.history.push(HistoryEntry::Error {
                message: "Failed to create thread.".to_string(),
            });
            return Ok(());
        }
    };

    let arguments = if cmd.arguments.is_empty() {
        serde_json::Value::Null
    } else {
        serde_json::json!(cmd.arguments)
    };
    let result: wire::types::CommandExecuteResult = wire
        .request(
            "command/execute",
            serde_json::json!({
                "threadId": thread_id,
                "command": cmd.name,
                "arguments": arguments
            }),
        )
        .await?;

    if let Some(message) = result.message.clone().filter(|m| !m.trim().is_empty()) {
        let _is_markdown = result.is_markdown;
        state.history.push(HistoryEntry::SystemInfo { message });
    }

    if result.session_reset.unwrap_or(false) {
        state.history.clear();
        state.plan = None;
        state.subagent_entries.clear();
        state.streaming.clear();
        state.token_tracker.reset();
        state.current_turn_id = None;
        state.current_model_override = None;
        state.pending_model_override = None;
        state.current_reasoning = None;
        state.pending_reasoning = None;
        state.current_goal = None;
        if let Some(thread) = result.thread {
            state.current_thread_id = Some(thread.id);
            state.current_thread_name = thread.display_name;
        } else {
            state.current_thread_id = None;
            state.current_thread_name = None;
        }
    }

    if result.handled {
        if let Some(expanded_prompt) = result.expanded_prompt.filter(|p| !p.trim().is_empty()) {
            state.streaming.clear();
            submit_turn(wire, state, expanded_prompt).await?;
        }
    } else {
        state.history.push(HistoryEntry::Error {
            message: format!("Command not handled: {}", cmd.name),
        });
    }

    Ok(())
}

fn expire_notifications(state: &mut AppState) {
    let now_ms = chrono::Utc::now().timestamp_millis();
    state.notifications.retain(|n| n.dismiss_at_ms > now_ms);
}

// ── Draw ──────────────────────────────────────────────────────────────────

fn draw(terminal: &mut Term, state: &AppState, theme: &Theme, strings: &Strings) -> Result<()> {
    terminal::prepare_frame(terminal);
    terminal.draw(|frame| {
        let area = frame.area();
        let has_active_turn = state.turn_status == TurnStatus::Running
            || state.turn_status == TurnStatus::WaitingApproval
            || state.turn_status == TurnStatus::WaitingInput;
        let show_status_zone = has_active_turn || state.system_status.is_some();
        let has_pending = !state.pending_input.is_empty();
        let user_input_active = matches!(state.active_overlay, Some(OverlayKind::UserInput))
            && state.pending_user_input.is_some();
        let input_h = if user_input_active {
            state
                .pending_user_input
                .as_ref()
                .map(|request| UserInputOverlay::preferred_height(request, area.width))
                .unwrap_or(3)
        } else {
            InputEditor::preferred_height(state, area.width)
        };
        let status_h = StatusIndicator::preferred_height(state);
        let command_popup_h = if user_input_active {
            0
        } else if let Some(popup) = &state.command_popup {
            CommandPopup::preferred_height(popup)
        } else if let Some(popup) = &state.skill_popup {
            SkillPopup::preferred_height(popup)
        } else {
            0
        };
        let transcript_h = ChatView::preferred_height(state, theme, strings, area.width);
        let footer_h = if user_input_active {
            0
        } else {
            FooterLine::preferred_height(state)
        };
        let zones = layout::compute(
            area,
            transcript_h,
            show_status_zone,
            has_pending,
            input_h,
            status_h,
            command_popup_h,
            footer_h,
        );

        // ChatView: pass actual available width for correct markdown wrap.
        let chat_width = zones.chat_view.width;

        // ── Base UI ───────────────────────────────────────────────────────
        frame.render_widget(
            ChatView::new(state, theme, strings).with_width(chat_width),
            zones.chat_view,
        );

        if let Some(si_area) = zones.status_indicator {
            frame.render_widget(StatusIndicator::new(state, theme, strings), si_area);
        }

        // Pending input preview (between StatusIndicator and InputEditor).
        if let Some(pp_area) = zones.pending_preview {
            if let Some(queued) = state.pending_input.first() {
                use ratatui::{
                    text::{Line, Span},
                    widgets::{Paragraph, Widget},
                };
                let preview = format!("  ┄ {}: \"{queued}\"", strings.pending_queued_prefix);
                Paragraph::new(Line::from(Span::styled(preview, theme.dim)))
                    .render(pp_area, frame.buffer_mut());
            }
        }

        if user_input_active {
            if let Some(request) = &state.pending_user_input {
                frame.render_widget(UserInputOverlay::new(request, theme), zones.input_editor);
            }
        } else {
            frame.render_widget(InputEditor::new(state, theme, strings), zones.input_editor);
        }

        if let (Some(popup_state), Some(popup_area)) = (&state.command_popup, zones.command_popup) {
            frame.render_widget(CommandPopup::new(popup_state, theme), popup_area);
        } else if let (Some(popup_state), Some(popup_area)) =
            (&state.skill_popup, zones.command_popup)
        {
            frame.render_widget(SkillPopup::new(popup_state, theme, strings), popup_area);
        }

        if let Some(footer_area) = zones.footer {
            frame.render_widget(FooterLine::new(state, theme, strings), footer_area);
        }

        // Keep the caret visible whenever the input editor has focus and no overlay is active.
        if user_input_active {
            if let Some(request) = &state.pending_user_input {
                if let Some(pos) =
                    UserInputOverlay::new(request, theme).cursor_position(zones.input_editor)
                {
                    frame.set_cursor_position(pos);
                }
            }
        } else if state.focus == crate::app::state::FocusTarget::InputEditor
            && state.active_overlay.is_none()
        {
            // 2 = gutter width ("❯ " / "✎ ")
            let inner_w = zones.input_editor.width.saturating_sub(2);
            let (row, col) =
                ui::input_editor::offset_to_2d(&state.input_text, state.input_cursor, inner_w);
            let cursor_x = zones.input_editor.x + 2 + col.min(inner_w.saturating_sub(1));
            let cursor_y =
                zones.input_editor.y + row.min(zones.input_editor.height.saturating_sub(1));
            frame.set_cursor_position((cursor_x, cursor_y));
        }

        // ── Notification toast (non-modal, top-right) ─────────────────────
        if !state.notifications.is_empty() {
            frame.render_widget(NotificationToast::new(state, theme, strings), area);
        }

        // ── Modal overlays (render last, on top) ──────────────────────────
        match &state.active_overlay {
            Some(OverlayKind::Approval) => {
                if let Some(approval) = &state.pending_approval {
                    frame.render_widget(ApprovalOverlay::new(approval, theme, strings), area);
                }
            }
            Some(OverlayKind::UserInput) => {}
            Some(OverlayKind::ThreadPicker) => {
                if let Some(picker) = &state.thread_picker {
                    frame.render_widget(ThreadPicker::new(picker, theme, strings), area);
                }
            }
            Some(OverlayKind::ModelPicker) => {
                if let Some(picker) = &state.model_picker {
                    frame.render_widget(
                        ModelPicker::new(picker, state.tick_count, theme, strings),
                        area,
                    );
                }
            }
            Some(OverlayKind::SkillsPicker) => {
                if let Some(picker) = &state.skills_picker {
                    frame.render_widget(SkillsPicker::new(picker, theme, strings), area);
                }
            }
            Some(OverlayKind::PermissionsPicker) => {
                if let Some(picker) = &state.permissions_picker {
                    frame.render_widget(PermissionsPicker::new(picker, theme, strings), area);
                }
            }
            None => {}
        }
    })?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crossterm::event::{KeyCode, KeyEvent, KeyModifiers};

    fn skill(name: &str) -> wire::types::SkillInfo {
        wire::types::SkillInfo {
            name: name.to_string(),
            description: "Skill description".to_string(),
            display_name: Some(name.to_string()),
            short_description: None,
            source: "builtin".to_string(),
            plugin_id: None,
            plugin_display_name: None,
            available: true,
            unavailable_reason: None,
            enabled: true,
            path: format!("/skills/{name}/SKILL.md"),
            has_variant: None,
            default_prompt: None,
            metadata: None,
        }
    }

    fn key(code: KeyCode) -> CrosstermEvent {
        CrosstermEvent::Key(KeyEvent::new(code, KeyModifiers::NONE))
    }

    #[test]
    fn disconnected_text_input_edits_draft() {
        let mut state = AppState::new("workspace".to_string());

        let quit = handle_disconnected_terminal_event(&mut state, key(KeyCode::Char('h')));

        assert!(!quit);
        assert_eq!(state.input_text, "h");
        assert_eq!(state.input_cursor, 1);
    }

    #[test]
    fn disconnected_enter_keeps_draft_and_does_not_submit() {
        let mut state = AppState::new("workspace".to_string());
        state.input_text = "hello".to_string();
        state.input_cursor = state.input_text.len();

        let quit = handle_disconnected_terminal_event(&mut state, key(KeyCode::Enter));

        assert!(!quit);
        assert_eq!(state.input_text, "hello");
        assert_eq!(state.input_cursor, 5);
        assert!(state.input_history.is_empty());
        assert!(state.history.is_empty());
    }

    #[test]
    fn replay_thread_history_restores_dynamic_tool_calls() {
        let mut state = AppState::new("workspace".to_string());
        let data = serde_json::json!({
            "thread": {
                "displayName": "Board review",
                "turns": [
                    {
                        "items": [
                            {
                                "type": "dynamicToolCall",
                                "payload": {
                                    "namespace": "oratorio",
                                    "toolName": "ListBoardItems",
                                    "callId": "dynamic-call-1",
                                    "arguments": { "status": "todo" },
                                    "contentItems": [
                                        { "type": "text", "text": "2 board items" },
                                        { "type": "image", "mediaType": "image/png", "dataBase64": "abc123" }
                                    ],
                                    "success": true
                                }
                            }
                        ]
                    }
                ]
            }
        });

        replay_thread_history(&mut state, &data);

        assert!(matches!(
            state.history.last(),
            Some(HistoryEntry::ToolCall {
                call_id,
                name,
                args,
                result,
                success,
                ..
            }) if call_id == "dynamic-call-1"
                && name == "ListBoardItems"
                && args.contains("\"status\"")
                && result.as_deref() == Some("2 board items\n[image: image/png]")
                && *success
        ));
    }

    #[test]
    fn turn_input_parts_materialize_skill_refs() {
        let mut state = AppState::new("workspace".to_string());
        state.skill_cache = SkillCacheState::Ready(vec![skill("browser")]);

        let parts = build_turn_input_parts(&state, "Use $browser please");

        assert_eq!(parts.len(), 3);
        assert_eq!(
            parts[0],
            serde_json::json!({ "type": "text", "text": "Use " })
        );
        assert_eq!(
            parts[1],
            serde_json::json!({ "type": "skillRef", "name": "browser" })
        );
        assert_eq!(
            parts[2],
            serde_json::json!({ "type": "text", "text": " please" })
        );
    }

    #[test]
    fn turn_input_parts_leave_embedded_dollar_text_alone() {
        let mut state = AppState::new("workspace".to_string());
        state.skill_cache = SkillCacheState::Ready(vec![skill("browser")]);

        let parts = build_turn_input_parts(&state, "cost$browser");

        assert_eq!(
            parts,
            vec![serde_json::json!({
                "type": "text",
                "text": "cost$browser"
            })]
        );
    }

    #[test]
    fn parse_model_catalog_keeps_reasoning_metadata() {
        let payload = serde_json::json!({
            "success": true,
            "models": [
                {
                    "id": "claude-opus-4-7",
                    "reasoning": {
                        "supportsDisable": false,
                        "defaultEffort": "extraHigh",
                        "supportedEfforts": [
                            { "effort": "high", "label": "High" },
                            { "effort": "extraHigh", "label": "Extra High" }
                        ]
                    }
                }
            ]
        });

        let (models, error) = parse_model_catalog(&payload);

        assert!(error.is_none());
        let reasoning = models[0].reasoning.as_ref().expect("reasoning metadata");
        assert!(!reasoning.supports_disable);
        assert_eq!(reasoning.default_effort, ReasoningQuickValue::ExtraHigh);
        assert_eq!(
            reasoning
                .supported_efforts
                .iter()
                .map(|option| option.value)
                .collect::<Vec<_>>(),
            vec![ReasoningQuickValue::High, ReasoningQuickValue::ExtraHigh]
        );
    }

    #[test]
    fn reasoning_payload_default_removes_override_and_off_preserves_output() {
        let current = ReasoningConfigState {
            enabled: true,
            effort: ReasoningQuickValue::High,
            output: "summary".to_string(),
        };

        assert!(build_reasoning_payload(ReasoningQuickValue::Default, current.clone()).is_none());

        let payload =
            build_reasoning_payload(ReasoningQuickValue::Off, current).expect("off payload");
        assert_eq!(
            payload,
            serde_json::json!({
                "enabled": false,
                "effort": "high",
                "output": "summary"
            })
        );
    }
}
