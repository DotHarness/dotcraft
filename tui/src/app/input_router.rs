// Maps terminal key/paste/resize events to AppState mutations.
// Phase 2: Shift+Enter newline, Tab reasoning toggle, PageUp/Down, Home/End.
// Phase 3: Approval overlay key handling, ApprovalDecision action.
// Phase 4: ThreadPicker overlay and command/skill popups.

use crate::{
    app::state::{
        AppState, CommandPopupState, FocusTarget, PermissionOption, SkillCacheState,
        SkillPopupState, SkillSuggestion, SkillsPickerState, TurnStatus,
    },
    ui::overlays::{command_popup, skill_popup},
};

/// Actions available in the ThreadPicker overlay.
#[derive(Debug)]
pub enum ThreadPickerOp {
    Resume,
    Archive,
    Delete,
    Close,
}

#[derive(Debug)]
pub enum ModelPickerOp {
    Apply,
    Close,
}

#[derive(Debug)]
pub enum SkillsPickerOp {
    Toggle,
    Close,
}

#[derive(Debug)]
pub enum PermissionsPickerOp {
    Apply,
    Close,
}

/// Returned by key handlers to communicate the required action to the event loop.
#[derive(Debug)]
pub enum InputAction {
    /// Submit the current input text as a new turn.
    SubmitTurn(String),
    /// Send a turn interrupt request (Ctrl+C). Contributes to double-press quit within 1s.
    Interrupt,
    /// Send a turn interrupt request (Esc). Does not contribute to double-press quit.
    SoftInterrupt,
    /// Quit the TUI.
    Quit,
    /// User chose a decision for an approval overlay.
    ApprovalDecision(String),
    /// User submitted or dismissed a model-initiated input request.
    UserInputResponse(serde_json::Value),
    /// User performed an action in the thread-picker overlay.
    ThreadPickerAction(ThreadPickerOp),
    /// User performed an action in the model-picker overlay.
    ModelPickerAction(ModelPickerOp),
    /// Dismiss the current non-approval overlay.
    CloseOverlay,
    /// Toggle Agent/Plan mode (Shift+Tab).
    ToggleMode,
    /// Force a full terminal redraw (Ctrl+L).
    ForceRedraw,
    /// User performed an action in the skills picker overlay.
    SkillsPickerAction(SkillsPickerOp),
    /// User performed an action in the permissions picker overlay.
    PermissionsPickerAction(PermissionsPickerOp),
    /// No action needed beyond the AppState mutation already applied.
    None,
}

/// Process a crossterm key event and return the action to perform.
pub fn handle_key(state: &mut AppState, key: crossterm::event::KeyEvent) -> InputAction {
    use crossterm::event::{KeyCode, KeyModifiers};

    // Global bindings (regardless of focus).
    if key.modifiers == KeyModifiers::CONTROL {
        match key.code {
            KeyCode::Char('c') => return InputAction::Interrupt,
            KeyCode::Char('d') => return InputAction::Quit,
            KeyCode::Char('l') => return InputAction::ForceRedraw,
            _ => {}
        }
    }

    // Global: Esc interrupts a running or approval/input-waiting turn.
    if key.code == KeyCode::Esc
        && (state.turn_status == TurnStatus::Running
            || state.turn_status == TurnStatus::WaitingApproval
            || state.turn_status == TurnStatus::WaitingInput)
    {
        return InputAction::SoftInterrupt;
    }

    // Shift+Tab (BackTab) toggles Agent/Plan mode from any focus.
    if key.code == KeyCode::BackTab {
        return InputAction::ToggleMode;
    }

    match state.focus {
        FocusTarget::InputEditor => handle_input_editor(state, key),
        FocusTarget::ChatView => handle_chat_view(state, key),
    }
}

fn handle_input_editor(state: &mut AppState, key: crossterm::event::KeyEvent) -> InputAction {
    use crossterm::event::{KeyCode, KeyModifiers};

    // ── Command popup interception ──────────────────────────────────────
    if state.command_popup.is_some() {
        if key.modifiers == KeyModifiers::CONTROL && matches!(key.code, KeyCode::Char('p')) {
            if let Some(popup) = state.command_popup.as_mut() {
                popup.move_up(command_popup::MAX_POPUP_ROWS);
            }
            return InputAction::None;
        }
        if key.modifiers == KeyModifiers::CONTROL && matches!(key.code, KeyCode::Char('n')) {
            if let Some(popup) = state.command_popup.as_mut() {
                popup.move_down(command_popup::MAX_POPUP_ROWS);
            }
            return InputAction::None;
        }

        match key.code {
            KeyCode::Tab | KeyCode::Enter if key.modifiers == KeyModifiers::NONE => {
                if let Some(popup) = state.command_popup.take() {
                    if let Some((cmd, _)) = popup.items.get(popup.selected) {
                        exit_input_history_recall(state);
                        state.input_text = format!("{cmd} ");
                        state.input_cursor = state.input_text.len();
                    }
                }
                return InputAction::None;
            }
            KeyCode::Up if key.modifiers == KeyModifiers::NONE => {
                if let Some(popup) = state.command_popup.as_mut() {
                    popup.move_up(command_popup::MAX_POPUP_ROWS);
                }
                return InputAction::None;
            }
            KeyCode::Down if key.modifiers == KeyModifiers::NONE => {
                if let Some(popup) = state.command_popup.as_mut() {
                    popup.move_down(command_popup::MAX_POPUP_ROWS);
                }
                return InputAction::None;
            }
            KeyCode::Esc => {
                state.command_popup = None;
                return InputAction::None;
            }
            _ => {
                // Fall through to normal editing; popup will be updated after.
                state.command_popup = None;
            }
        }
    }

    // ── Skill mention popup interception ────────────────────────────────
    if state.skill_popup.is_some() {
        if key.modifiers == KeyModifiers::CONTROL && matches!(key.code, KeyCode::Char('p')) {
            if let Some(popup) = state.skill_popup.as_mut() {
                popup.move_up(skill_popup::MAX_POPUP_ROWS);
            }
            return InputAction::None;
        }
        if key.modifiers == KeyModifiers::CONTROL && matches!(key.code, KeyCode::Char('n')) {
            if let Some(popup) = state.skill_popup.as_mut() {
                popup.move_down(skill_popup::MAX_POPUP_ROWS);
            }
            return InputAction::None;
        }

        match key.code {
            KeyCode::Tab | KeyCode::Enter if key.modifiers == KeyModifiers::NONE => {
                accept_selected_skill(state);
                return InputAction::None;
            }
            KeyCode::Up if key.modifiers == KeyModifiers::NONE => {
                if let Some(popup) = state.skill_popup.as_mut() {
                    popup.move_up(skill_popup::MAX_POPUP_ROWS);
                }
                return InputAction::None;
            }
            KeyCode::Down if key.modifiers == KeyModifiers::NONE => {
                if let Some(popup) = state.skill_popup.as_mut() {
                    popup.move_down(skill_popup::MAX_POPUP_ROWS);
                }
                return InputAction::None;
            }
            KeyCode::Esc => {
                state.skill_popup = None;
                return InputAction::None;
            }
            _ => {
                // Fall through to normal editing; popup will be updated after.
                state.skill_popup = None;
            }
        }
    }

    // ── Normal input handling ────────────────────────────────────────────
    let action = match key.code {
        // Ctrl+V → paste from system clipboard
        KeyCode::Char('v') if key.modifiers == KeyModifiers::CONTROL => {
            exit_input_history_recall(state);
            if let Ok(text) = crate::clipboard::read_text() {
                state.input_text.insert_str(state.input_cursor, &text);
                state.input_cursor += text.len();
            }
            InputAction::None
        }

        // Enter (no shift) → submit
        KeyCode::Enter if key.modifiers == KeyModifiers::NONE => {
            let text = std::mem::take(&mut state.input_text);
            state.input_cursor = 0;
            state.input_history_pos = None;
            state.command_popup = None;
            if !text.is_empty() {
                state.input_history.push(text.clone());
            }
            InputAction::SubmitTurn(text)
        }

        // Shift+Enter → insert newline at cursor
        KeyCode::Enter if key.modifiers == KeyModifiers::SHIFT => {
            exit_input_history_recall(state);
            state.input_text.insert(state.input_cursor, '\n');
            state.input_cursor += 1;
            InputAction::None
        }

        KeyCode::Backspace => {
            exit_input_history_recall(state);
            if state.input_cursor > 0 {
                let before = &state.input_text[..state.input_cursor];
                let char_start = before
                    .char_indices()
                    .next_back()
                    .map(|(i, _)| i)
                    .unwrap_or(0);
                state.input_text.remove(char_start);
                state.input_cursor = char_start;
            }
            InputAction::None
        }

        KeyCode::Delete => {
            exit_input_history_recall(state);
            if state.input_cursor < state.input_text.len() {
                state.input_text.remove(state.input_cursor);
            }
            InputAction::None
        }

        KeyCode::Left => {
            if state.input_cursor > 0 {
                let before = &state.input_text[..state.input_cursor];
                if let Some((i, _)) = before.char_indices().next_back() {
                    state.input_cursor = i;
                }
            }
            InputAction::None
        }

        KeyCode::Right => {
            if state.input_cursor < state.input_text.len() {
                let c = state.input_text[state.input_cursor..]
                    .chars()
                    .next()
                    .unwrap();
                state.input_cursor += c.len_utf8();
            }
            InputAction::None
        }

        KeyCode::Char('a') if key.modifiers == KeyModifiers::CONTROL => {
            let before = &state.input_text[..state.input_cursor];
            let line_start = before.rfind('\n').map(|i| i + 1).unwrap_or(0);
            state.input_cursor = line_start;
            InputAction::None
        }

        KeyCode::Char('e') if key.modifiers == KeyModifiers::CONTROL => {
            let after = &state.input_text[state.input_cursor..];
            let line_end = after
                .find('\n')
                .map(|i| state.input_cursor + i)
                .unwrap_or(state.input_text.len());
            state.input_cursor = line_end;
            InputAction::None
        }

        // Page keys enter transcript browsing directly without requiring Esc first.
        KeyCode::PageUp => {
            state.focus = FocusTarget::ChatView;
            scroll_page_up(state);
            InputAction::None
        }
        KeyCode::PageDown => {
            state.focus = FocusTarget::ChatView;
            scroll_page_down(state);
            InputAction::None
        }
        KeyCode::Home => {
            state.focus = FocusTarget::ChatView;
            scroll_home(state);
            InputAction::None
        }
        KeyCode::End => {
            state.focus = FocusTarget::ChatView;
            scroll_end(state);
            InputAction::None
        }

        // Up → cycle backward through input history
        KeyCode::Up => {
            if state.input_text.is_empty() || state.input_history_pos.is_some() {
                let hist_len = state.input_history.len();
                if hist_len > 0 {
                    let pos = match state.input_history_pos {
                        None => hist_len - 1,
                        Some(p) if p > 0 => p - 1,
                        Some(p) => p,
                    };
                    state.input_history_pos = Some(pos);
                    state.input_text = state.input_history[pos].clone();
                    state.input_cursor = state.input_text.len();
                }
            }
            InputAction::None
        }

        // Down → cycle forward through input history
        KeyCode::Down => {
            if state.input_text.is_empty() || state.input_history_pos.is_some() {
                match state.input_history_pos {
                    None => {}
                    Some(p) if p + 1 < state.input_history.len() => {
                        let pos = p + 1;
                        state.input_history_pos = Some(pos);
                        state.input_text = state.input_history[pos].clone();
                        state.input_cursor = state.input_text.len();
                    }
                    Some(_) => {
                        state.input_history_pos = None;
                        state.input_text.clear();
                        state.input_cursor = 0;
                    }
                }
            }
            InputAction::None
        }

        KeyCode::Tab => {
            // Open command/skill popup if the caret is inside a trigger token.
            if command_popup::slash_command_filter(&state.input_text, state.input_cursor).is_some()
                || skill_popup::skill_mention_filter(&state.input_text, state.input_cursor)
                    .is_some()
            {
                sync_inline_popups(state);
            } else if !state.input_text.is_empty() && state.turn_status != TurnStatus::Idle {
                // Queue follow-up text while a turn is running; drained on turn completion.
                let text = std::mem::take(&mut state.input_text);
                state.input_cursor = 0;
                exit_input_history_recall(state);
                state.command_popup = None;
                state.skill_popup = None;
                state.pending_input.push(text);
            }
            InputAction::None
        }

        KeyCode::Char(c) => {
            exit_input_history_recall(state);
            state.input_text.insert(state.input_cursor, c);
            state.input_cursor += c.len_utf8();
            InputAction::None
        }

        KeyCode::Esc => {
            state.focus = FocusTarget::ChatView;
            InputAction::None
        }

        _ => InputAction::None,
    };

    sync_inline_popups(state);

    action
}

fn exit_input_history_recall(state: &mut AppState) {
    state.input_history_pos = None;
}

fn sync_inline_popups(state: &mut AppState) {
    let filtered = command_popup::filter_commands_for_input(
        &state.input_text,
        state.input_cursor,
        &state.command_catalog,
    );
    if !filtered.is_empty() {
        state.skill_popup = None;
        if let Some(popup) = state.command_popup.as_mut() {
            popup.replace_items(filtered, command_popup::MAX_POPUP_ROWS);
        } else {
            let mut popup = CommandPopupState::new(filtered);
            popup.ensure_visible(command_popup::MAX_POPUP_ROWS);
            state.command_popup = Some(popup);
        }
        return;
    }
    state.command_popup = None;

    let filtered = skill_popup::filter_skills_for_input(
        &state.input_text,
        state.input_cursor,
        skill_suggestions(state),
    );
    if filtered.is_empty() {
        state.skill_popup = None;
        return;
    }

    if let Some(popup) = state.skill_popup.as_mut() {
        popup.replace_items(filtered, skill_popup::MAX_POPUP_ROWS);
    } else {
        let mut popup = SkillPopupState::new(filtered);
        popup.ensure_visible(skill_popup::MAX_POPUP_ROWS);
        state.skill_popup = Some(popup);
    }
}

fn skill_suggestions(state: &AppState) -> Vec<SkillSuggestion> {
    let SkillCacheState::Ready(skills) = &state.skill_cache else {
        return Vec::new();
    };

    let mut out: Vec<SkillSuggestion> = skills
        .iter()
        .filter(|skill| skill.available && skill.enabled)
        .map(|skill| {
            let display_name = skill
                .display_name
                .clone()
                .unwrap_or_else(|| skill.name.clone());
            let description = skill
                .short_description
                .clone()
                .filter(|value| !value.trim().is_empty())
                .unwrap_or_else(|| skill.description.clone());
            let source_label = skill
                .plugin_display_name
                .as_ref()
                .filter(|value| !value.trim().is_empty())
                .map(|plugin| format!("[Plugin] {plugin}"))
                .unwrap_or_else(|| format!("[{}]", skill.source));
            SkillSuggestion {
                name: skill.name.clone(),
                display_name,
                description,
                source_label,
                enabled: skill.enabled,
                available: skill.available,
            }
        })
        .collect();
    out.sort_by(|a, b| a.display_name.cmp(&b.display_name));
    out
}

fn accept_selected_skill(state: &mut AppState) {
    let Some(popup) = state.skill_popup.take() else {
        return;
    };
    let Some(skill) = popup.items.get(popup.selected) else {
        return;
    };
    let Some(token) = skill_popup::skill_mention_filter(&state.input_text, state.input_cursor)
    else {
        return;
    };
    let start = token.start;
    let end = token.end;

    exit_input_history_recall(state);
    let insert = format!("${} ", skill.name);
    state.input_text.replace_range(start..end, &insert);
    state.input_cursor = start + insert.len();
}

fn filtered_skill_indices(picker: &SkillsPickerState) -> Vec<usize> {
    let filter = picker.search.trim().to_ascii_lowercase();
    let mut indices: Vec<usize> = picker
        .skills
        .iter()
        .enumerate()
        .filter_map(|(idx, skill)| {
            if filter.is_empty() {
                return Some(idx);
            }
            let display = skill
                .display_name
                .as_deref()
                .unwrap_or(skill.name.as_str())
                .to_ascii_lowercase();
            let description = skill.description.to_ascii_lowercase();
            (skill.name.to_ascii_lowercase().contains(&filter)
                || display.contains(&filter)
                || description.contains(&filter))
            .then_some(idx)
        })
        .collect();
    indices.sort_by(|a, b| {
        let a_skill = &picker.skills[*a];
        let b_skill = &picker.skills[*b];
        let a_name = a_skill
            .display_name
            .as_deref()
            .unwrap_or(a_skill.name.as_str());
        let b_name = b_skill
            .display_name
            .as_deref()
            .unwrap_or(b_skill.name.as_str());
        a_name.cmp(b_name)
    });
    indices
}

fn clamp_skills_picker(picker: &mut SkillsPickerState) {
    let len = filtered_skill_indices(picker).len();
    if len == 0 {
        picker.selected = 0;
        picker.scroll_offset = 0;
        return;
    }

    picker.selected = picker.selected.min(len - 1);
    let visible_rows = skill_popup::MAX_POPUP_ROWS.min(len).max(1);
    if picker.selected < picker.scroll_offset {
        picker.scroll_offset = picker.selected;
    } else {
        let bottom = picker.scroll_offset + visible_rows - 1;
        if picker.selected > bottom {
            picker.scroll_offset = picker.selected + 1 - visible_rows;
        }
    }
    picker.scroll_offset = picker.scroll_offset.min(len.saturating_sub(visible_rows));
}

fn move_skills_picker_up(picker: &mut SkillsPickerState) {
    let len = filtered_skill_indices(picker).len();
    if len == 0 {
        picker.selected = 0;
    } else {
        picker.selected = if picker.selected == 0 {
            len - 1
        } else {
            picker.selected - 1
        };
    }
    clamp_skills_picker(picker);
}

fn move_skills_picker_down(picker: &mut SkillsPickerState) {
    let len = filtered_skill_indices(picker).len();
    if len == 0 {
        picker.selected = 0;
    } else {
        picker.selected = if picker.selected + 1 >= len {
            0
        } else {
            picker.selected + 1
        };
    }
    clamp_skills_picker(picker);
}

fn handle_chat_view(state: &mut AppState, key: crossterm::event::KeyEvent) -> InputAction {
    use crossterm::event::{KeyCode, KeyModifiers};

    match key.code {
        // Idle: Esc exits transcript browse back to input editor.
        // Running/WaitingApproval Esc is handled by the global SoftInterrupt branch.
        KeyCode::Esc => {
            state.focus = FocusTarget::InputEditor;
            InputAction::None
        }

        KeyCode::Up => {
            scroll_line_up(state);
            InputAction::None
        }

        KeyCode::Down => {
            scroll_line_down(state);
            InputAction::None
        }

        KeyCode::PageUp => {
            scroll_page_up(state);
            InputAction::None
        }

        KeyCode::PageDown => {
            scroll_page_down(state);
            InputAction::None
        }

        // Home → jump to top of chat history
        KeyCode::Home => {
            scroll_home(state);
            InputAction::None
        }

        // End → jump to bottom of chat history
        KeyCode::End => {
            scroll_end(state);
            InputAction::None
        }

        // Tab → toggle reasoning visibility
        KeyCode::Tab => {
            state.show_reasoning = !state.show_reasoning;
            InputAction::None
        }

        KeyCode::Char('e') => {
            // Tool call results are always visible in the new design;
            // this key binding is kept as a no-op for backwards compatibility.
            InputAction::None
        }

        // y → yank (copy) last agent message to system clipboard
        KeyCode::Char('y') => {
            let last_msg = state.history.iter().rev().find_map(|entry| {
                if let crate::app::state::HistoryEntry::AgentMessage { text } = entry {
                    Some(text.clone())
                } else {
                    None
                }
            });
            if let Some(text) = last_msg {
                let _ = crate::clipboard::write_text(&text);
            }
            InputAction::None
        }

        KeyCode::Enter | KeyCode::Char('i') => {
            state.focus = FocusTarget::InputEditor;
            InputAction::None
        }
        KeyCode::Char(c)
            if key.modifiers == KeyModifiers::NONE || key.modifiers == KeyModifiers::SHIFT =>
        {
            state.focus = FocusTarget::InputEditor;
            state.input_text.insert(state.input_cursor, c);
            state.input_cursor += c.len_utf8();
            state.input_history_pos = None;
            InputAction::None
        }

        _ => InputAction::None,
    }
}

pub fn enter_transcript_browse(state: &mut AppState) {
    state.focus = FocusTarget::ChatView;
}

pub fn scroll_line_up(state: &mut AppState) {
    state.scroll_offset = state.scroll_offset.saturating_add(1);
    state.at_bottom = false;
}

pub fn scroll_line_down(state: &mut AppState) {
    if state.scroll_offset > 0 {
        state.scroll_offset -= 1;
    }
    if state.scroll_offset == 0 {
        state.at_bottom = true;
    }
}

fn scroll_page_up(state: &mut AppState) {
    let page = page_step(state);
    state.scroll_offset = state.scroll_offset.saturating_add(page);
    state.at_bottom = false;
}

fn scroll_page_down(state: &mut AppState) {
    let page = page_step(state);
    if state.scroll_offset >= page {
        state.scroll_offset -= page;
    } else {
        state.scroll_offset = 0;
        state.at_bottom = true;
    }
}

fn scroll_home(state: &mut AppState) {
    state.scroll_offset = usize::MAX / 2; // Large value; will be clamped in ChatView
    state.at_bottom = false;
}

fn scroll_end(state: &mut AppState) {
    state.scroll_offset = 0;
    state.at_bottom = true;
}

fn page_step(state: &AppState) -> usize {
    const MIN_PAGE_STEP: usize = 10;
    state.last_viewport_height.get().max(MIN_PAGE_STEP)
}

/// Handle key events when the ApprovalOverlay is active.
/// Returns `ApprovalDecision(decision_str)` when the user confirms, or `None` for navigation.
pub fn handle_approval_overlay(
    state: &mut AppState,
    key: crossterm::event::KeyEvent,
) -> InputAction {
    use crossterm::event::KeyCode;

    let selected = match state.pending_approval.as_ref() {
        Some(a) => a.selected,
        None => return InputAction::None,
    };
    let decision_count = 5usize;

    match key.code {
        // Navigate up
        KeyCode::Up | KeyCode::Char('k') => {
            if let Some(a) = state.pending_approval.as_mut() {
                if a.selected > 0 {
                    a.selected -= 1;
                }
            }
            InputAction::None
        }

        // Navigate down
        KeyCode::Down | KeyCode::Char('j') => {
            if let Some(a) = state.pending_approval.as_mut() {
                if a.selected + 1 < decision_count {
                    a.selected += 1;
                }
            }
            InputAction::None
        }

        // Confirm current selection
        KeyCode::Enter => {
            let decision = crate::ui::overlays::approval::DECISIONS[selected].to_string();
            InputAction::ApprovalDecision(decision)
        }

        // Direct key shortcuts
        KeyCode::Char('a') => InputAction::ApprovalDecision("accept".to_string()),
        KeyCode::Char('s') => InputAction::ApprovalDecision("acceptForSession".to_string()),
        KeyCode::Char('!') => InputAction::ApprovalDecision("acceptAlways".to_string()),
        KeyCode::Char('d') => InputAction::ApprovalDecision("decline".to_string()),
        KeyCode::Char('c') | KeyCode::Esc => InputAction::ApprovalDecision("cancel".to_string()),

        _ => InputAction::None,
    }
}

pub fn handle_user_input_overlay(
    state: &mut AppState,
    key: crossterm::event::KeyEvent,
) -> InputAction {
    use crossterm::event::{KeyCode, KeyModifiers};

    let Some(request) = state.pending_user_input.as_mut() else {
        return InputAction::None;
    };
    if request.questions.is_empty() {
        return InputAction::UserInputResponse(serde_json::json!({ "answers": {} }));
    }

    let current = request.current_question.min(request.questions.len() - 1);
    request.current_question = current;
    let option_count =
        request.questions[current].options.len() + usize::from(request.questions[current].is_other);
    let option_count = option_count.max(1);
    let other_index = request.questions[current].options.len();
    let is_other_input_selected =
        request.questions[current].is_other && request.selected[current] == other_index;

    match key.code {
        KeyCode::Esc => InputAction::UserInputResponse(serde_json::json!({ "answers": {} })),
        KeyCode::Left => {
            request.current_question = request.current_question.saturating_sub(1);
            InputAction::None
        }
        KeyCode::Right => {
            request.current_question =
                (request.current_question + 1).min(request.questions.len() - 1);
            InputAction::None
        }
        KeyCode::Char('h') if key.modifiers == KeyModifiers::NONE && !is_other_input_selected => {
            request.current_question = request.current_question.saturating_sub(1);
            InputAction::None
        }
        KeyCode::Char('l') if key.modifiers == KeyModifiers::NONE && !is_other_input_selected => {
            request.current_question =
                (request.current_question + 1).min(request.questions.len() - 1);
            InputAction::None
        }
        KeyCode::Up | KeyCode::Char('k') => {
            let selected = request.selected.get_mut(current).expect("selection exists");
            *selected = selected.saturating_sub(1);
            InputAction::None
        }
        KeyCode::Down | KeyCode::Char('j') => {
            let selected = request.selected.get_mut(current).expect("selection exists");
            *selected = (*selected + 1).min(option_count - 1);
            InputAction::None
        }
        KeyCode::Char(c) if c.is_ascii_digit() && key.modifiers == KeyModifiers::NONE => {
            if let Some(index) = c.to_digit(10).and_then(|d| d.checked_sub(1)) {
                let index = index as usize;
                if index < option_count {
                    request.selected[current] = index;
                }
            }
            InputAction::None
        }
        KeyCode::Char(c)
            if key.modifiers == KeyModifiers::NONE || key.modifiers == KeyModifiers::SHIFT =>
        {
            if is_other_input_selected {
                request.other_text[current].push(c);
            }
            InputAction::None
        }
        KeyCode::Backspace => {
            if is_other_input_selected {
                request.other_text[current].pop();
            }
            InputAction::None
        }
        KeyCode::Enter => {
            if current + 1 < request.questions.len() {
                request.current_question += 1;
                InputAction::None
            } else {
                InputAction::UserInputResponse(build_user_input_response(request))
            }
        }
        _ => InputAction::None,
    }
}

fn build_user_input_response(
    request: &crate::app::state::UserInputRequestState,
) -> serde_json::Value {
    let mut answers = serde_json::Map::new();
    for (idx, question) in request.questions.iter().enumerate() {
        let selected = request.selected.get(idx).copied().unwrap_or(0);
        let mut values = Vec::new();
        if let Some(option) = question.options.get(selected) {
            values.push(option.label.clone());
        } else if question.is_other {
            let text = request.other_text.get(idx).map(|s| s.trim()).unwrap_or("");
            if text.is_empty() {
                values.push("Other".to_string());
            } else {
                values.push(format!("user_note: {text}"));
            }
        }
        answers.insert(
            question.id.clone(),
            serde_json::json!({ "answers": values }),
        );
    }
    serde_json::json!({ "answers": answers })
}

/// Handle key events when the ThreadPicker overlay is active.
pub fn handle_thread_picker(state: &mut AppState, key: crossterm::event::KeyEvent) -> InputAction {
    use crossterm::event::KeyCode;

    let thread_count = state
        .thread_picker
        .as_ref()
        .map(|p| p.threads.len())
        .unwrap_or(0);

    match key.code {
        KeyCode::Up | KeyCode::Char('k') => {
            if let Some(picker) = state.thread_picker.as_mut() {
                if picker.selected > 0 {
                    picker.selected -= 1;
                }
            }
            InputAction::None
        }

        KeyCode::Down | KeyCode::Char('j') => {
            if let Some(picker) = state.thread_picker.as_mut() {
                if thread_count > 0 && picker.selected + 1 < thread_count {
                    picker.selected += 1;
                }
            }
            InputAction::None
        }

        KeyCode::Enter => InputAction::ThreadPickerAction(ThreadPickerOp::Resume),
        KeyCode::Char('a') => InputAction::ThreadPickerAction(ThreadPickerOp::Archive),
        KeyCode::Char('d') => InputAction::ThreadPickerAction(ThreadPickerOp::Delete),
        KeyCode::Esc | KeyCode::Char('q') => InputAction::ThreadPickerAction(ThreadPickerOp::Close),

        _ => InputAction::None,
    }
}

/// Handle key events when the ModelPicker overlay is active.
pub fn handle_model_picker(state: &mut AppState, key: crossterm::event::KeyEvent) -> InputAction {
    use crossterm::event::KeyCode;

    let model_count = state
        .model_picker
        .as_ref()
        .map(|p| p.reasoning_options.len() + p.models.len())
        .unwrap_or(0);

    match key.code {
        KeyCode::Up | KeyCode::Char('k') => {
            if let Some(picker) = state.model_picker.as_mut() {
                if picker.selected > 0 {
                    picker.selected -= 1;
                }
            }
            InputAction::None
        }
        KeyCode::Down | KeyCode::Char('j') => {
            if let Some(picker) = state.model_picker.as_mut() {
                if model_count > 0 && picker.selected + 1 < model_count {
                    picker.selected += 1;
                }
            }
            InputAction::None
        }
        KeyCode::Enter => InputAction::ModelPickerAction(ModelPickerOp::Apply),
        KeyCode::Esc | KeyCode::Char('q') => InputAction::ModelPickerAction(ModelPickerOp::Close),
        _ => InputAction::None,
    }
}

pub fn handle_skills_picker(state: &mut AppState, key: crossterm::event::KeyEvent) -> InputAction {
    use crossterm::event::{KeyCode, KeyModifiers};

    let Some(picker) = state.skills_picker.as_mut() else {
        return InputAction::None;
    };

    match key.code {
        KeyCode::Up if key.modifiers == KeyModifiers::NONE => {
            move_skills_picker_up(picker);
            InputAction::None
        }
        KeyCode::Down if key.modifiers == KeyModifiers::NONE => {
            move_skills_picker_down(picker);
            InputAction::None
        }
        KeyCode::Char('p') if key.modifiers == KeyModifiers::CONTROL => {
            move_skills_picker_up(picker);
            InputAction::None
        }
        KeyCode::Char('n') if key.modifiers == KeyModifiers::CONTROL => {
            move_skills_picker_down(picker);
            InputAction::None
        }
        KeyCode::Backspace => {
            picker.search.pop();
            clamp_skills_picker(picker);
            InputAction::None
        }
        KeyCode::Char('u') if key.modifiers == KeyModifiers::CONTROL => {
            picker.search.clear();
            clamp_skills_picker(picker);
            InputAction::None
        }
        KeyCode::Char(c)
            if key.modifiers == KeyModifiers::NONE || key.modifiers == KeyModifiers::SHIFT =>
        {
            picker.search.push(c);
            clamp_skills_picker(picker);
            InputAction::None
        }
        KeyCode::Enter | KeyCode::Char(' ') => {
            InputAction::SkillsPickerAction(SkillsPickerOp::Toggle)
        }
        KeyCode::Esc | KeyCode::Char('q') => InputAction::SkillsPickerAction(SkillsPickerOp::Close),
        _ => InputAction::None,
    }
}

pub fn selected_skill_name(state: &AppState) -> Option<String> {
    let picker = state.skills_picker.as_ref()?;
    let indices = filtered_skill_indices(picker);
    let actual_idx = indices.get(picker.selected)?;
    picker
        .skills
        .get(*actual_idx)
        .map(|skill| skill.name.clone())
}

pub fn handle_permissions_picker(
    state: &mut AppState,
    key: crossterm::event::KeyEvent,
) -> InputAction {
    use crossterm::event::{KeyCode, KeyModifiers};

    let Some(picker) = state.permissions_picker.as_mut() else {
        return InputAction::None;
    };
    let len = picker.options.len();

    match key.code {
        KeyCode::Up if key.modifiers == KeyModifiers::NONE => {
            if len > 0 {
                picker.selected = if picker.selected == 0 {
                    len - 1
                } else {
                    picker.selected - 1
                };
            }
            InputAction::None
        }
        KeyCode::Down if key.modifiers == KeyModifiers::NONE => {
            if len > 0 {
                picker.selected = if picker.selected + 1 >= len {
                    0
                } else {
                    picker.selected + 1
                };
            }
            InputAction::None
        }
        KeyCode::Char('p') if key.modifiers == KeyModifiers::CONTROL => {
            if len > 0 {
                picker.selected = if picker.selected == 0 {
                    len - 1
                } else {
                    picker.selected - 1
                };
            }
            InputAction::None
        }
        KeyCode::Char('n') if key.modifiers == KeyModifiers::CONTROL => {
            if len > 0 {
                picker.selected = if picker.selected + 1 >= len {
                    0
                } else {
                    picker.selected + 1
                };
            }
            InputAction::None
        }
        KeyCode::Enter | KeyCode::Char(' ') => {
            InputAction::PermissionsPickerAction(PermissionsPickerOp::Apply)
        }
        KeyCode::Esc | KeyCode::Char('q') => {
            InputAction::PermissionsPickerAction(PermissionsPickerOp::Close)
        }
        _ => InputAction::None,
    }
}

pub fn selected_permission_option(state: &AppState) -> Option<PermissionOption> {
    let picker = state.permissions_picker.as_ref()?;
    picker.options.get(picker.selected).cloned()
}

#[cfg(test)]
mod tests {
    use super::*;
    use crossterm::event::{KeyCode, KeyEvent, KeyModifiers};

    fn skill(name: &str, display: &str, description: &str) -> crate::wire::types::SkillInfo {
        crate::wire::types::SkillInfo {
            name: name.to_string(),
            description: description.to_string(),
            display_name: Some(display.to_string()),
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

    fn key(code: KeyCode) -> KeyEvent {
        KeyEvent::new(code, KeyModifiers::NONE)
    }

    fn ctrl(ch: char) -> KeyEvent {
        KeyEvent::new(KeyCode::Char(ch), KeyModifiers::CONTROL)
    }

    fn state_with_commands(count: usize) -> AppState {
        let mut state = AppState::new("workspace".to_string());
        state.command_catalog = (0..count)
            .map(|idx| {
                crate::app::state::SlashCommandDescriptor::new(
                    format!("/cmd{idx:02}"),
                    format!("Command {idx}"),
                    "test",
                )
            })
            .collect();
        state
    }

    fn state_with_user_input() -> AppState {
        let mut state = AppState::new("workspace".to_string());
        state.pending_user_input = Some(crate::app::state::UserInputRequestState {
            request_id: serde_json::json!("req-1"),
            request_id_text: "req-1".to_string(),
            questions: vec![crate::app::state::UserInputQuestion {
                id: "provider_id_handling".to_string(),
                header: "Provider ID".to_string(),
                question: "Should users handle the provider id directly?".to_string(),
                is_other: true,
                options: vec![
                    crate::app::state::UserInputOption {
                        label: "Auto-generate (Recommended)".to_string(),
                        description: "DotCraft creates a stable id.".to_string(),
                    },
                    crate::app::state::UserInputOption {
                        label: "Required".to_string(),
                        description: "Users must type it explicitly.".to_string(),
                    },
                ],
            }],
            current_question: 0,
            selected: vec![0],
            other_text: vec![String::new()],
        });
        state
    }

    fn state_with_multi_user_input() -> AppState {
        let mut state = state_with_user_input();
        let request = state.pending_user_input.as_mut().expect("pending input");
        request
            .questions
            .push(crate::app::state::UserInputQuestion {
                id: "activity".to_string(),
                header: "Activity".to_string(),
                question: "What should DotCraft do next?".to_string(),
                is_other: true,
                options: vec![
                    crate::app::state::UserInputOption {
                        label: "Write tests (Recommended)".to_string(),
                        description: "Add coverage.".to_string(),
                    },
                    crate::app::state::UserInputOption {
                        label: "Refactor".to_string(),
                        description: "Clean up the implementation.".to_string(),
                    },
                ],
            });
        request.selected.push(0);
        request.other_text.push(String::new());
        state
    }

    #[test]
    fn command_popup_scrolls_selected_item_into_view() {
        let mut state = state_with_commands(12);
        handle_key(&mut state, key(KeyCode::Char('/')));

        for _ in 0..command_popup::MAX_POPUP_ROWS {
            handle_key(&mut state, key(KeyCode::Down));
        }

        let popup = state.command_popup.expect("command popup");
        assert_eq!(popup.selected, command_popup::MAX_POPUP_ROWS);
        assert!(popup.scroll_offset > 0);
        assert!(popup.selected < popup.scroll_offset + command_popup::MAX_POPUP_ROWS);
    }

    #[test]
    fn command_popup_supports_ctrl_p_and_ctrl_n() {
        let mut state = state_with_commands(3);
        handle_key(&mut state, key(KeyCode::Char('/')));

        handle_key(&mut state, ctrl('n'));
        assert_eq!(state.command_popup.as_ref().expect("popup").selected, 1);

        handle_key(&mut state, ctrl('p'));
        assert_eq!(state.command_popup.as_ref().expect("popup").selected, 0);
    }

    #[test]
    fn command_popup_hides_after_command_name_token() {
        let mut state = state_with_commands(1);
        state.command_catalog[0].name = "/agent".to_string();

        for ch in "/agent ".chars() {
            handle_key(&mut state, key(KeyCode::Char(ch)));
        }

        assert!(state.command_popup.is_none());
        assert_eq!(state.input_text, "/agent ");
    }

    #[test]
    fn command_popup_clamps_selection_when_filter_changes() {
        let mut state = state_with_commands(12);
        handle_key(&mut state, key(KeyCode::Char('/')));
        for _ in 0..command_popup::MAX_POPUP_ROWS {
            handle_key(&mut state, key(KeyCode::Down));
        }

        for ch in "cmd1".chars() {
            handle_key(&mut state, key(KeyCode::Char(ch)));
        }

        let popup = state.command_popup.expect("command popup");
        assert!(popup.selected < popup.items.len());
        assert!(popup.scroll_offset < popup.items.len());
    }

    #[test]
    fn skill_popup_opens_and_inserts_selected_skill() {
        let mut state = AppState::new("workspace".to_string());
        state.skill_cache =
            SkillCacheState::Ready(vec![skill("browser", "Browser", "Web automation")]);

        handle_key(&mut state, key(KeyCode::Char('$')));
        assert!(state.skill_popup.is_some());

        handle_key(&mut state, key(KeyCode::Enter));

        assert_eq!(state.input_text, "$browser ");
        assert_eq!(state.input_cursor, "$browser ".len());
        assert!(state.skill_popup.is_none());
    }

    #[test]
    fn user_input_overlay_clamps_arrow_selection() {
        let mut state = state_with_user_input();

        assert!(matches!(
            handle_user_input_overlay(&mut state, key(KeyCode::Up)),
            InputAction::None
        ));
        assert_eq!(state.pending_user_input.as_ref().unwrap().selected[0], 0);

        handle_user_input_overlay(&mut state, key(KeyCode::Down));
        handle_user_input_overlay(&mut state, key(KeyCode::Down));
        handle_user_input_overlay(&mut state, key(KeyCode::Down));
        assert_eq!(state.pending_user_input.as_ref().unwrap().selected[0], 2);
    }

    #[test]
    fn user_input_overlay_supports_digit_selection_other_text_and_submit() {
        let mut state = state_with_user_input();

        handle_user_input_overlay(&mut state, key(KeyCode::Char('3')));
        handle_user_input_overlay(&mut state, key(KeyCode::Char('u')));
        handle_user_input_overlay(&mut state, key(KeyCode::Char('s')));
        handle_user_input_overlay(&mut state, key(KeyCode::Char('e')));

        let response = match handle_user_input_overlay(&mut state, key(KeyCode::Enter)) {
            InputAction::UserInputResponse(value) => value,
            other => panic!("expected user input response, got {other:?}"),
        };

        assert_eq!(
            response["answers"]["provider_id_handling"]["answers"][0],
            "user_note: use"
        );
    }

    #[test]
    fn user_input_overlay_navigates_questions_with_left_right_and_h_l() {
        let mut state = state_with_multi_user_input();

        handle_user_input_overlay(&mut state, key(KeyCode::Right));
        assert_eq!(
            state.pending_user_input.as_ref().unwrap().current_question,
            1
        );
        handle_user_input_overlay(&mut state, key(KeyCode::Right));
        assert_eq!(
            state.pending_user_input.as_ref().unwrap().current_question,
            1
        );
        handle_user_input_overlay(&mut state, key(KeyCode::Left));
        assert_eq!(
            state.pending_user_input.as_ref().unwrap().current_question,
            0
        );
        handle_user_input_overlay(&mut state, key(KeyCode::Char('l')));
        assert_eq!(
            state.pending_user_input.as_ref().unwrap().current_question,
            1
        );
        handle_user_input_overlay(&mut state, key(KeyCode::Char('h')));
        assert_eq!(
            state.pending_user_input.as_ref().unwrap().current_question,
            0
        );
    }

    #[test]
    fn user_input_overlay_preserves_per_question_state() {
        let mut state = state_with_multi_user_input();

        handle_user_input_overlay(&mut state, key(KeyCode::Down));
        handle_user_input_overlay(&mut state, key(KeyCode::Right));
        handle_user_input_overlay(&mut state, key(KeyCode::Char('3')));
        handle_user_input_overlay(&mut state, key(KeyCode::Char('写')));
        handle_user_input_overlay(&mut state, key(KeyCode::Char('代')));
        handle_user_input_overlay(&mut state, key(KeyCode::Backspace));
        handle_user_input_overlay(&mut state, key(KeyCode::Left));

        let request = state.pending_user_input.as_ref().unwrap();
        assert_eq!(request.current_question, 0);
        assert_eq!(request.selected[0], 1);
        assert_eq!(request.selected[1], 2);
        assert_eq!(request.other_text[1], "写");
    }

    #[test]
    fn user_input_overlay_submits_unicode_other_text_after_backspace() {
        let mut state = state_with_user_input();

        handle_user_input_overlay(&mut state, key(KeyCode::Char('3')));
        handle_user_input_overlay(&mut state, key(KeyCode::Char('写')));
        handle_user_input_overlay(&mut state, key(KeyCode::Char('代')));
        handle_user_input_overlay(&mut state, key(KeyCode::Char('码')));
        handle_user_input_overlay(&mut state, key(KeyCode::Backspace));

        let response = match handle_user_input_overlay(&mut state, key(KeyCode::Enter)) {
            InputAction::UserInputResponse(value) => value,
            other => panic!("expected user input response, got {other:?}"),
        };

        assert_eq!(
            response["answers"]["provider_id_handling"]["answers"][0],
            "user_note: 写代"
        );
    }

    #[test]
    fn user_input_overlay_esc_returns_empty_answers() {
        let mut state = state_with_user_input();

        let response = match handle_user_input_overlay(&mut state, key(KeyCode::Esc)) {
            InputAction::UserInputResponse(value) => value,
            other => panic!("expected user input response, got {other:?}"),
        };

        assert_eq!(response, serde_json::json!({ "answers": {} }));
    }

    #[test]
    fn model_picker_navigation_counts_reasoning_and_model_rows() {
        let mut state = AppState::new("workspace".to_string());
        state.model_picker = Some(crate::app::state::ModelPickerState {
            models: vec![crate::app::state::ModelCatalogItem {
                id: "claude-opus-4-7".to_string(),
                reasoning: None,
            }],
            reasoning_options: vec![
                crate::app::state::ReasoningPickerOption {
                    value: crate::app::state::ReasoningQuickValue::Default,
                    label: "Default".to_string(),
                    disabled: false,
                },
                crate::app::state::ReasoningPickerOption {
                    value: crate::app::state::ReasoningQuickValue::High,
                    label: "High".to_string(),
                    disabled: false,
                },
            ],
            selected: 0,
            loading: false,
            error: None,
        });

        handle_model_picker(&mut state, key(KeyCode::Down));
        handle_model_picker(&mut state, key(KeyCode::Down));

        assert_eq!(state.model_picker.as_ref().expect("picker").selected, 2);
    }
}
