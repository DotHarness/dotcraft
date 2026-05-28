// ChatView widget — scrollable conversation history (§8.3 of specs/clients/tui-client.md).
// Design: Calling/Called tool format (skipped for standalone sentences e.g. WebSearch), elapsed time, adaptive wrapping,
// default-visible result summaries, inline SubAgent block, inline Plan block.

use crate::{
    app::state::{
        ActiveToolCall, AppState, HistoryEntry, ReasoningConfigState, SubAgentEntry, TurnStatus,
    },
    app::token_tracker::format_token_count,
    i18n::Strings,
    theme::Theme,
    ui::markdown,
    ui::tool_format::{
        extract_partial_json_string_value, format_active_invocation_display,
        format_completed_invocation_display_with_result, format_invocation_display_with_plan,
        format_result_summary_with_args, invocation_needs_calling_called_prefix_with_plan,
    },
    ui::welcome_screen::{session_header_lines, tip_line},
};
use ratatui::{
    buffer::Buffer,
    layout::Rect,
    style::{Modifier, Style},
    text::{Line, Span},
    widgets::{Block, Borders, Paragraph, Widget, Wrap},
};
use std::time::Duration;
use unicode_width::UnicodeWidthChar;

/// Maximum result/output lines shown below a completed tool call.
const TOOL_CALL_MAX_LINES: usize = 5;
/// Maximum preview lines for in-flight file editing tools.
const TOOL_ACTIVE_FILE_PREVIEW_MAX_LINES: usize = 8;
/// Maximum preview lines for in-flight shell output.
const TOOL_ACTIVE_SHELL_PREVIEW_MAX_LINES: usize = 8;

pub struct ChatView<'a> {
    state: &'a AppState,
    theme: &'a Theme,
    strings: &'a Strings,
    /// Available width (for markdown rendering and scroll tracking).
    width: u16,
}

impl<'a> ChatView<'a> {
    pub fn new(state: &'a AppState, theme: &'a Theme, strings: &'a Strings) -> Self {
        Self {
            state,
            theme,
            strings,
            width: 80,
        }
    }

    pub fn with_width(mut self, w: u16) -> Self {
        self.width = w;
        self
    }

    pub fn preferred_height(state: &AppState, theme: &Theme, strings: &Strings, width: u16) -> u16 {
        if width == 0 {
            return 0;
        }

        let view = ChatView::new(state, theme, strings).with_width(width);
        let render_width = width.saturating_sub(4).max(20);
        let lines = view.build_lines(render_width, width);
        Paragraph::new(lines)
            .block(Block::default().borders(Borders::NONE))
            .wrap(Wrap { trim: false })
            .line_count(width)
            .min(u16::MAX as usize) as u16
    }

    fn build_lines(&self, render_width: u16, area_width: u16) -> Vec<Line<'static>> {
        let mut lines: Vec<Line<'static>> = Vec::new();
        let mut subagent_block_rendered = false;

        let has_conversation_content = self.state.history.iter().any(|entry| {
            matches!(
                entry,
                HistoryEntry::UserMessage { .. }
                    | HistoryEntry::AgentMessage { .. }
                    | HistoryEntry::ToolCall { .. }
            )
        });
        if !has_conversation_content
            && self.state.turn_status == TurnStatus::Idle
            && self.state.streaming.message_buffer.is_empty()
        {
            let connection_error = if self.state.connected {
                None
            } else {
                self.state
                    .history
                    .iter()
                    .rev()
                    .find_map(|entry| match entry {
                        HistoryEntry::Error { message } => Some(message.as_str()),
                        _ => None,
                    })
            };
            let thread_label = self
                .state
                .current_thread_name
                .as_deref()
                .or(self.state.current_thread_id.as_deref());
            let model_display = compact_model_reasoning_label(self.state, self.strings);
            lines.extend(session_header_lines(
                env!("CARGO_PKG_VERSION"),
                &self.state.workspace_path,
                Some(model_display.as_str()),
                thread_label,
                self.state.connected,
                connection_error,
                self.state.tick_count,
                self.theme,
                self.strings,
                area_width,
            ));
            lines.push(Line::default());
            lines.push(tip_line(self.strings, self.theme));
            lines.push(Line::default());
        }

        // ── Committed history entries ──────────────────────────────────────
        let history = &self.state.history;
        for (i, entry) in history.iter().enumerate() {
            // Emit a turn separator when transitioning from agent/tool → user.
            let prev_is_agent_or_tool = i > 0
                && matches!(
                    &history[i - 1],
                    HistoryEntry::AgentMessage { .. } | HistoryEntry::ToolCall { .. }
                );
            if prev_is_agent_or_tool && matches!(entry, HistoryEntry::UserMessage { .. }) {
                self.render_turn_separator(render_width, &mut lines);
            }

            self.render_history_entry(entry, render_width, &mut lines);

            if !subagent_block_rendered
                && Self::is_spawn_subagent_history_entry(entry)
                && !Self::next_history_entry_is_spawn_subagent(history, i)
            {
                let subagent_entries = self.current_inline_subagent_entries();
                if !subagent_entries.is_empty() {
                    self.render_inline_subagents(subagent_entries, render_width, &mut lines);
                    subagent_block_rendered = true;
                }
            }
        }

        // ── Active streaming section ───────────────────────────────────────
        if self.state.turn_status == TurnStatus::Running
            || self.state.turn_status == TurnStatus::WaitingApproval
            || self.state.turn_status == TurnStatus::WaitingInput
        {
            self.render_streaming(render_width, &mut lines);
        }

        // ── Active tool calls ──────────────────────────────────────────────
        let active_tools: Vec<&ActiveToolCall> = self
            .state
            .streaming
            .active_tools
            .iter()
            .filter(|tool| !tool.completed)
            .collect();
        for (idx, tool) in active_tools.iter().enumerate() {
            self.render_active_tool(tool, render_width, &mut lines);

            if !subagent_block_rendered
                && Self::is_spawn_subagent_tool_name(&tool.tool_name)
                && match active_tools.get(idx + 1) {
                    Some(next) => !Self::is_spawn_subagent_tool_name(&next.tool_name),
                    None => true,
                }
            {
                let subagent_entries = self.current_inline_subagent_entries();
                if !subagent_entries.is_empty() {
                    self.render_inline_subagents(subagent_entries, render_width, &mut lines);
                    subagent_block_rendered = true;
                }
            }
        }

        // ── Inline Plan block ─────────────────────────────────────────────
        if let Some(plan) = &self.state.plan {
            // Only render inline plan during active streaming or when it's the
            // last entry — otherwise it shows up as committed history.
            if self.state.turn_status == TurnStatus::Running {
                self.render_inline_plan_v2(plan, render_width, &mut lines);
            }
        }

        lines
    }
}

fn compact_model_reasoning_label(state: &AppState, strings: &Strings) -> String {
    let model = state
        .current_model_override
        .as_deref()
        .or(state.workspace_model.as_deref())
        .unwrap_or(strings.model_default_label);
    let reasoning = state
        .current_reasoning
        .as_ref()
        .or(state.pending_reasoning.as_ref())
        .or(state.workspace_reasoning.as_ref());
    match reasoning.map(reasoning_label) {
        Some(label) => format!("{model} {label}"),
        None => model.to_string(),
    }
}

fn reasoning_label(reasoning: &ReasoningConfigState) -> &'static str {
    if reasoning.enabled {
        reasoning.effort.label()
    } else {
        "Off"
    }
}

impl Widget for ChatView<'_> {
    fn render(self, area: Rect, buf: &mut Buffer) {
        // 2 cols for the left gutter, 2 for potential right margin.
        let render_width = area.width.saturating_sub(4).max(20);
        let mut lines = self.build_lines(render_width, area.width);

        // ── Compute scroll ────────────────────────────────────────────────
        // Use Paragraph::line_count() instead of lines.len() so that visual
        // rows produced by word-wrap are counted correctly. lines.len() only
        // counts Line objects; each Line that overflows the viewport width
        // wraps into additional visual rows that lines.len() misses, causing
        // max_scroll to be too small and content to be unreachable.
        let para = Paragraph::new(lines.clone())
            .block(Block::default().borders(Borders::NONE))
            .wrap(Wrap { trim: false });
        let total_visual_lines = para.line_count(area.width);

        // Record the viewport height so the input router can use it for
        // viewport-relative PageUp/PageDown scrolling. Cell allows mutation
        // through the shared &AppState reference.
        self.state.last_viewport_height.set(area.height as usize);

        let viewport = area.height as usize;
        let max_scroll = total_visual_lines.saturating_sub(viewport);
        let clamped_offset = self.state.scroll_offset.min(max_scroll);

        let scroll = if self.state.at_bottom {
            max_scroll
        } else {
            max_scroll.saturating_sub(clamped_offset)
        };

        // ── Scroll indicator ──────────────────────────────────────────────
        let visible_bottom = scroll + viewport;
        let lines_below = total_visual_lines.saturating_sub(visible_bottom);
        if lines_below > 0 {
            let indicator = Line::from(Span::styled(
                format!(" ↓ {lines_below} more lines "),
                self.theme.dim,
            ));
            lines.push(indicator);
        }

        // Clamp scroll to u16::MAX — Paragraph::scroll() takes (u16, u16) and
        // very long sessions could theoretically exceed that range.
        let scroll_u16 = scroll.min(u16::MAX as usize) as u16;

        Paragraph::new(lines)
            .block(Block::default().borders(Borders::NONE))
            .wrap(Wrap { trim: false })
            .scroll((scroll_u16, 0))
            .render(area, buf);
    }
}

impl ChatView<'_> {
    // ── Turn separator ──────────────────────────────────────────────────────

    fn render_turn_separator(&self, width: u16, out: &mut Vec<Line<'static>>) {
        let rule_width = width.saturating_sub(2) as usize;
        out.push(Line::from(Span::styled(
            format!("  {}", "─".repeat(rule_width)),
            self.theme.dim,
        )));
        out.push(Line::default());
    }

    // ── History entry rendering ──────────────────────────────────────────────

    fn render_history_entry(&self, entry: &HistoryEntry, width: u16, out: &mut Vec<Line<'static>>) {
        match entry {
            HistoryEntry::UserMessage { text } => {
                out.push(Line::default());
                let prefix_str = format!("{} ", self.strings.user_prefix);
                let prefix_width = display_width(&prefix_str);
                let indent = " ".repeat(prefix_width);

                for (i, line) in text.lines().enumerate() {
                    if i == 0 {
                        out.push(Line::from(vec![
                            Span::styled(prefix_str.clone(), self.theme.user_message),
                            Span::styled(line.to_string(), self.theme.user_message),
                        ]));
                    } else {
                        out.push(Line::from(vec![
                            Span::raw(indent.clone()),
                            Span::styled(line.to_string(), self.theme.user_message),
                        ]));
                    }
                }
                out.push(Line::default());
            }

            HistoryEntry::AgentMessage { text } => {
                let rendered = markdown::render_owned(text.clone(), width, self.theme);
                for (i, mut line) in rendered.into_iter().enumerate() {
                    let prefix = if i == 0 {
                        Span::styled("• ", self.theme.dim)
                    } else {
                        Span::raw("  ")
                    };
                    line.spans.insert(0, prefix);
                    out.push(line);
                }
                out.push(Line::default());
            }

            HistoryEntry::ToolCall {
                call_id,
                name,
                args,
                result,
                success,
                duration,
            } => {
                self.render_committed_tool(
                    call_id,
                    name,
                    args,
                    result.as_deref(),
                    *success,
                    *duration,
                    width,
                    out,
                );
            }

            HistoryEntry::Error { message } => {
                out.push(Line::from(vec![
                    Span::raw("  "),
                    Span::styled(format!("{} ", self.strings.error_prefix), self.theme.error),
                    Span::styled(message.clone(), self.theme.error),
                ]));
                out.push(Line::default());
            }

            HistoryEntry::SystemInfo { message } => {
                out.push(Line::from(vec![
                    Span::raw("  "),
                    Span::styled(message.clone(), self.theme.system_info),
                ]));
            }
        }
    }

    // ── Tool call rendering ────────────────────────────────────────────────

    /// Format tool call arguments as a compact inline invocation.
    fn format_invocation(&self, name: &str, args: &str) -> String {
        format_invocation_display_with_plan(
            name,
            args,
            self.state.plan.as_ref().map(|p| p.todos.as_slice()),
        )
    }

    /// Render an active (in-flight) tool call.
    fn render_active_tool(
        &self,
        tool: &crate::app::state::ActiveToolCall,
        width: u16,
        out: &mut Vec<Line<'static>>,
    ) {
        let invocation = format_active_invocation_display(
            &tool.tool_name,
            &tool.arguments,
            self.state.plan.as_ref().map(|p| p.todos.as_slice()),
        );
        let need_calling_prefix = invocation_needs_calling_called_prefix_with_plan(
            &tool.tool_name,
            &tool.arguments,
            self.state.plan.as_ref().map(|p| p.todos.as_slice()),
        );

        // "• Calling ToolName("arg")" or "• Searched "…"" (no second verb for standalone sentences).
        let prefix = if need_calling_prefix {
            format!("  • {} ", self.strings.calling)
        } else {
            "  • ".to_string()
        };
        let prefix_w = display_width(&prefix);
        let available = (width as usize).saturating_sub(prefix_w);

        if display_width(&invocation) <= available {
            // Fits on one line.
            out.push(Line::from(vec![
                Span::styled(prefix, self.theme.tool_active),
                Span::styled(invocation, self.theme.tool_active),
            ]));
        } else {
            // Overflow: header on one line, invocation wrapped on next with └.
            let header = if need_calling_prefix {
                format!("  • {}…", self.strings.calling)
            } else {
                "  • …".to_string()
            };
            out.push(Line::from(Span::styled(header, self.theme.tool_active)));
            out.push(Line::from(vec![
                Span::styled("    └ ".to_string(), self.theme.dim),
                Span::styled(
                    truncate(&invocation, width.saturating_sub(6) as usize),
                    self.theme.dim,
                ),
            ]));
        }

        if is_shell_tool_name(&tool.tool_name) {
            if let Some(output) = active_shell_output_text(self.state, tool) {
                if !output.is_empty() {
                    let output_lines: Vec<&str> = output.lines().collect();
                    let show_count = output_lines.len().min(TOOL_ACTIVE_SHELL_PREVIEW_MAX_LINES);
                    let last_idx = show_count.saturating_sub(1);
                    for (i, line) in output_lines.iter().take(show_count).enumerate() {
                        let is_last = i == last_idx;
                        let truncated_suffix = if is_last
                            && output_lines.len() > TOOL_ACTIVE_SHELL_PREVIEW_MAX_LINES
                        {
                            "…"
                        } else {
                            ""
                        };
                        out.push(Line::from(vec![
                            Span::styled("    │ ".to_string(), self.theme.dim),
                            Span::styled(
                                format!(
                                    "{}{}",
                                    truncate(line, width.saturating_sub(8) as usize),
                                    truncated_suffix
                                ),
                                self.theme.dim,
                            ),
                        ]));
                    }
                }
            }
        }

        // File tools: show user-friendly in-flight content preview instead of raw argument deltas.
        if let Some(preview) = active_file_preview_text(tool) {
            let preview_lines: Vec<&str> = preview.lines().collect();
            let show_count = preview_lines.len().min(TOOL_ACTIVE_FILE_PREVIEW_MAX_LINES);
            let last_idx = show_count.saturating_sub(1);
            for (i, line) in preview_lines.iter().take(show_count).enumerate() {
                let is_last = i == last_idx;
                let truncated_suffix =
                    if is_last && preview_lines.len() > TOOL_ACTIVE_FILE_PREVIEW_MAX_LINES {
                        "…"
                    } else {
                        ""
                    };
                out.push(Line::from(vec![
                    Span::styled("    │ ".to_string(), self.theme.dim),
                    Span::styled(
                        format!(
                            "{}{}",
                            truncate(line, width.saturating_sub(8) as usize),
                            truncated_suffix
                        ),
                        self.theme.dim,
                    ),
                ]));
            }
            if show_count == 0 {
                out.push(Line::from(vec![
                    Span::styled("    │ ".to_string(), self.theme.dim),
                    Span::styled("Waiting for content...".to_string(), self.theme.dim),
                ]));
            }
        }
    }

    /// Render a committed (completed or failed) tool call.
    fn render_committed_tool(
        &self,
        call_id: &str,
        name: &str,
        args: &str,
        result: Option<&str>,
        success: bool,
        duration: Option<Duration>,
        width: u16,
        out: &mut Vec<Line<'static>>,
    ) {
        let bullet = if success { "•" } else { "•" };
        let bullet_style = if success {
            self.theme.success
        } else {
            self.theme.tool_error
        };
        let verb = self.strings.called;

        let completed_invocation = result.and_then(|tool_result| {
            format_completed_invocation_display_with_result(
                name,
                args,
                tool_result,
                self.state.plan.as_ref().map(|p| p.todos.as_slice()),
            )
        });
        let has_completed_invocation = completed_invocation.is_some();
        let invocation = completed_invocation.unwrap_or_else(|| self.format_invocation(name, args));
        let need_called_prefix = !has_completed_invocation
            && invocation_needs_calling_called_prefix_with_plan(
                name,
                args,
                self.state.plan.as_ref().map(|p| p.todos.as_slice()),
            );
        let elapsed = self
            .committed_tool_elapsed(name, call_id, duration)
            .map(|d| format!(" ({:.1}s)", d.as_secs_f64()))
            .unwrap_or_default();

        // "• Called ToolName("arg") (0.3s)" or "• Searched "…" (0.3s)" (no "Called" for standalone).
        let header = if need_called_prefix {
            format!("  {bullet} {verb} ")
        } else {
            format!("  {bullet} ")
        };
        let header_w = display_width(&header);
        let suffix = elapsed.clone();
        let available = (width as usize).saturating_sub(header_w + suffix.len());

        let inline = display_width(&invocation) <= available;

        if inline {
            out.push(Line::from(vec![
                Span::raw("  "),
                Span::styled(format!("{bullet} "), bullet_style),
                Span::styled(
                    if need_called_prefix {
                        format!("{verb} ")
                    } else {
                        String::new()
                    },
                    self.theme.dim,
                ),
                Span::styled(invocation.clone(), self.theme.tool_completed),
                Span::styled(elapsed, self.theme.dim),
            ]));
        } else {
            // Header line
            out.push(Line::from(vec![
                Span::raw("  "),
                Span::styled(format!("{bullet} "), bullet_style),
                Span::styled(
                    if need_called_prefix {
                        verb.to_string()
                    } else {
                        String::new()
                    },
                    self.theme.dim,
                ),
                Span::styled(elapsed, self.theme.dim),
            ]));
            // Wrapped invocation
            out.push(Line::from(vec![
                Span::styled("    └ ".to_string(), self.theme.dim),
                Span::styled(
                    truncate(&invocation, width.saturating_sub(6) as usize),
                    self.theme.tool_completed,
                ),
            ]));
        }

        // Result summary (always visible, dimmed, max TOOL_CALL_MAX_LINES).
        if !is_todo_progress_tool(name) {
            if let Some(result_text) = result {
                if !result_text.is_empty() {
                    let result_lines: Vec<String> = if let Some(formatted) =
                        format_result_summary_with_args(name, Some(args), result_text)
                    {
                        formatted
                    } else {
                        result_text.lines().map(str::to_string).collect()
                    };
                    let show_count = result_lines.len().min(TOOL_CALL_MAX_LINES);
                    let last_shown = show_count.saturating_sub(1);
                    for (i, line) in result_lines.iter().take(show_count).enumerate() {
                        let is_last = i == last_shown;
                        let truncated_suffix =
                            if is_last && result_lines.len() > TOOL_CALL_MAX_LINES {
                                "…"
                            } else {
                                ""
                            };
                        let text = truncate(line, width.saturating_sub(8) as usize);
                        let prefix_str = if is_last && inline {
                            "    └ "
                        } else {
                            "    │ "
                        };
                        out.push(Line::from(vec![
                            Span::styled(prefix_str.to_string(), self.theme.dim),
                            Span::styled(format!("{text}{truncated_suffix}"), self.theme.dim),
                        ]));
                    }
                }
            }
        }
    }

    fn committed_tool_elapsed(
        &self,
        tool_name: &str,
        call_id: &str,
        committed_duration: Option<Duration>,
    ) -> Option<Duration> {
        if is_shell_tool_name(tool_name) {
            if let Some(exec) = self
                .state
                .streaming
                .active_command_executions
                .iter()
                .find(|exec| exec.call_id.as_deref() == Some(call_id) && !exec.completed)
            {
                return Some(exec.started_at.elapsed());
            }
        }
        committed_duration
    }

    // ── Streaming section ───────────────────────────────────────────────────

    fn render_streaming(&self, width: u16, out: &mut Vec<Line<'static>>) {
        // ── Reasoning block ────────────────────────────────────────────────
        if !self.state.streaming.reasoning_buffer.is_empty() {
            let header = if self.state.show_reasoning {
                format!("  {} ", self.strings.reasoning_header)
            } else {
                format!("  {} (Tab to expand)", self.strings.reasoning_header)
            };
            out.push(Line::from(Span::styled(
                header,
                self.theme.reasoning_header,
            )));

            if self.state.show_reasoning {
                for line in self.state.streaming.reasoning_buffer.lines() {
                    out.push(Line::from(vec![
                        Span::raw("    "),
                        Span::styled(line.to_string(), self.theme.reasoning),
                    ]));
                }
                out.push(Line::default());
            }
        }

        // ── Agent message (full re-render from message_buffer) ─────────────
        if !self.state.streaming.message_buffer.is_empty() {
            let rendered = markdown::render_owned(
                self.state.streaming.message_buffer.clone(),
                width,
                self.theme,
            );
            for (i, mut line) in rendered.into_iter().enumerate() {
                let prefix = if i == 0 {
                    Span::styled("• ", self.theme.dim)
                } else {
                    Span::raw("  ")
                };
                line.spans.insert(0, prefix);
                out.push(line);
            }
            // Streaming cursor indicator
            out.push(Line::from(vec![
                Span::raw("  "),
                Span::styled("▍", self.theme.agent_message),
            ]));
        } else if self.state.streaming.reasoning_buffer.is_empty()
            && self.state.streaming.active_tools.is_empty()
            && self.state.streaming.active_command_executions.is_empty()
        {
            // Nothing yet — the StatusIndicator above the input shows "Working",
            // so keep a static hint in the chat area instead of another spinner.
            out.push(Line::from(vec![
                Span::raw("  "),
                Span::styled("…", self.theme.dim),
            ]));
        }
    }

    // ── Inline SubAgent block ───────────────────────────────────────────────

    fn render_inline_subagents(
        &self,
        entries: &[SubAgentEntry],
        width: u16,
        out: &mut Vec<Line<'static>>,
    ) {
        if entries.is_empty() {
            return;
        }

        let active_count = entries.iter().filter(|e| !e.is_completed).count();
        let done_count = entries.iter().filter(|e| e.is_completed).count();

        // When all are complete, collapse to a summary line.
        if active_count == 0 {
            let total_in: i64 = entries.iter().map(|e| e.input_tokens).sum();
            let total_out: i64 = entries.iter().map(|e| e.output_tokens).sum();
            out.push(Line::from(vec![
                Span::raw("  "),
                Span::styled("✓ ", self.theme.success),
                Span::styled(
                    format!(
                        "{done_count} {} (↑{} ↓{})",
                        self.strings.subagents_complete,
                        format_token_count(total_in),
                        format_token_count(total_out),
                    ),
                    self.theme.dim,
                ),
            ]));
            return;
        }

        // Header
        let rule_w = width.saturating_sub(4) as usize;
        let header = format!("──── SubAgents ({active_count} active, {done_count} done)");
        let header = truncate(&header, rule_w);
        out.push(Line::from(Span::styled(header, self.theme.dim)));

        for entry in entries {
            let (status_span, name_style) = if entry.is_completed {
                (
                    Span::styled("•  ".to_string(), self.theme.success),
                    self.theme.dim,
                )
            } else if entry.current_tool.is_some() {
                (
                    Span::styled("•  ".to_string(), self.theme.tool_active),
                    self.theme.tool_active,
                )
            } else {
                (
                    Span::styled("•  ".to_string(), self.theme.dim),
                    self.theme.dim,
                )
            };

            let tool_text = if entry.is_completed {
                "Done".to_string()
            } else {
                entry
                    .current_tool
                    .clone()
                    .unwrap_or_else(|| "…".to_string())
            };

            let tokens = format!(
                "↑{} ↓{}",
                format_token_count(entry.input_tokens),
                format_token_count(entry.output_tokens),
            );

            // Layout: "  status  label  tool  tokens"
            let label_w = 16usize;
            let tool_w = 20usize;
            let label = truncate_pad(&entry.label, label_w);
            let tool = truncate_pad(&tool_text, tool_w);

            out.push(Line::from(vec![
                Span::raw("  "),
                status_span,
                Span::styled(label, name_style),
                Span::raw("  "),
                Span::styled(tool, self.theme.dim),
                Span::raw("  "),
                Span::styled(tokens, self.theme.dim),
            ]));
        }
    }

    fn current_inline_subagent_entries(&self) -> &[SubAgentEntry] {
        if self.state.turn_status == TurnStatus::Running
            || self.state.turn_status == TurnStatus::WaitingApproval
            || self.state.turn_status == TurnStatus::WaitingInput
        {
            return &self.state.subagent_entries;
        }

        if !self.state.last_subagent_entries.is_empty() {
            return &self.state.last_subagent_entries;
        }

        &self.state.subagent_entries
    }

    fn is_spawn_subagent_tool_name(tool_name: &str) -> bool {
        tool_name == "SpawnAgent"
    }

    fn is_spawn_subagent_history_entry(entry: &HistoryEntry) -> bool {
        match entry {
            HistoryEntry::ToolCall { name, .. } => Self::is_spawn_subagent_tool_name(name),
            _ => false,
        }
    }

    fn next_history_entry_is_spawn_subagent(history: &[HistoryEntry], index: usize) -> bool {
        history
            .get(index + 1)
            .is_some_and(Self::is_spawn_subagent_history_entry)
    }

    // ── Inline Plan block ────────────────────────────────────────────────────

    fn render_inline_plan_v2(
        &self,
        plan: &crate::app::state::PlanSnapshot,
        width: u16,
        out: &mut Vec<Line<'static>>,
    ) {
        out.extend(render_inline_plan_lines(
            plan,
            width,
            self.theme,
            self.strings,
        ));
        out.push(Line::default());
    }

    #[allow(dead_code)]
    fn render_inline_plan(
        &self,
        plan: &crate::app::state::PlanSnapshot,
        width: u16,
        out: &mut Vec<Line<'static>>,
    ) {
        let rule_w = width.saturating_sub(4) as usize;
        let header = format!("──── Plan: {}", plan.title);
        out.push(Line::from(Span::styled(
            truncate(&header, rule_w),
            self.theme.dim,
        )));

        for todo in &plan.todos {
            let (icon, style) = match todo.status.as_str() {
                "completed" => ("✅", self.theme.tool_completed),
                "in_progress" => ("🔄", self.theme.tool_active),
                "cancelled" => ("🚫", self.theme.dim),
                _ => ("⬜", self.theme.agent_message),
            };
            out.push(Line::from(vec![
                Span::raw("  "),
                Span::raw(format!("{icon}  ")),
                Span::styled(
                    truncate(&todo.content, width.saturating_sub(8) as usize),
                    style,
                ),
            ]));
        }
        out.push(Line::default());
    }
}

fn is_todo_progress_tool(tool_name: &str) -> bool {
    matches!(tool_name, "TodoWrite" | "UpdateTodos")
}

/// Truncate `s` to at most `max_cols` display columns. Appends '…' if truncated.
/// Truncate `s` to at most `max_cols` display columns. Appends '…' if truncated.
fn render_inline_plan_lines(
    plan: &crate::app::state::PlanSnapshot,
    width: u16,
    theme: &Theme,
    strings: &Strings,
) -> Vec<Line<'static>> {
    let mut lines = render_wrapped_prefixed_lines(
        &format!("{}{}", strings.plan_title_prefix, plan.title.trim()),
        "• ",
        "  ",
        theme.agent_message.add_modifier(Modifier::BOLD),
        width,
    );

    let overview = plan.overview.trim();
    if !overview.is_empty() {
        lines.extend(render_wrapped_prefixed_lines(
            overview,
            "  ",
            "  ",
            theme.dim.add_modifier(Modifier::ITALIC),
            width,
        ));
    }

    for todo in &plan.todos {
        let (prefix, style) = plan_todo_prefix_and_style(todo.status.as_str(), theme);
        lines.extend(render_wrapped_prefixed_lines(
            todo.content.trim(),
            &format!("  {prefix}"),
            "      ",
            style,
            width,
        ));
    }

    lines
}

fn plan_todo_prefix_and_style(status: &str, theme: &Theme) -> (&'static str, Style) {
    match status {
        "completed" => (
            "[x] ",
            theme
                .tool_completed
                .add_modifier(Modifier::DIM | Modifier::CROSSED_OUT),
        ),
        "in_progress" => ("[>] ", theme.tool_active.add_modifier(Modifier::BOLD)),
        "cancelled" => ("[-] ", theme.dim),
        _ => ("[ ] ", theme.agent_message),
    }
}

fn render_wrapped_prefixed_lines(
    text: &str,
    first_prefix: &str,
    rest_prefix: &str,
    style: Style,
    width: u16,
) -> Vec<Line<'static>> {
    let available = (width as usize)
        .saturating_sub(display_width(first_prefix))
        .max(1);

    wrap_text_display(text, available)
        .into_iter()
        .enumerate()
        .map(|(idx, segment)| {
            let prefix = if idx == 0 { first_prefix } else { rest_prefix };
            Line::from(vec![
                Span::styled(prefix.to_string(), style),
                Span::styled(segment, style),
            ])
        })
        .collect()
}

fn wrap_text_display(text: &str, max_cols: usize) -> Vec<String> {
    if max_cols == 0 {
        return vec![String::new()];
    }

    let mut out = Vec::new();
    for raw_line in text.lines() {
        let line = raw_line.trim();
        if line.is_empty() {
            out.push(String::new());
            continue;
        }

        let mut current = String::new();
        let mut current_width = 0usize;

        for word in line.split_whitespace() {
            let word_width = display_width(word);
            let separator_width = if current.is_empty() { 0 } else { 1 };

            if current_width + separator_width + word_width <= max_cols {
                if separator_width == 1 {
                    current.push(' ');
                }
                current.push_str(word);
                current_width += separator_width + word_width;
                continue;
            }

            if !current.is_empty() {
                out.push(current);
                current = String::new();
                current_width = 0;
            }

            if word_width <= max_cols {
                current.push_str(word);
                current_width = word_width;
            } else {
                out.extend(split_by_display_width(word, max_cols));
            }
        }

        if !current.is_empty() {
            out.push(current);
        }
    }

    if out.is_empty() {
        out.push(String::new());
    }

    out
}

fn split_by_display_width(text: &str, max_cols: usize) -> Vec<String> {
    let mut parts = Vec::new();
    let mut current = String::new();
    let mut width = 0usize;

    for ch in text.chars() {
        let ch_width = UnicodeWidthChar::width(ch).unwrap_or(0);
        if !current.is_empty() && width + ch_width > max_cols {
            parts.push(current);
            current = String::new();
            width = 0;
        }
        current.push(ch);
        width += ch_width;
    }

    if !current.is_empty() {
        parts.push(current);
    }

    if parts.is_empty() {
        parts.push(String::new());
    }

    parts
}

fn truncate(s: &str, max_cols: usize) -> String {
    if max_cols == 0 {
        return String::new();
    }
    // First check if the string fits without truncation.
    let total_width: usize = s
        .chars()
        .map(|c| UnicodeWidthChar::width(c).unwrap_or(0))
        .sum();
    if total_width <= max_cols {
        return s.to_string();
    }
    // Doesn't fit — truncate and append '…'.
    let mut width: usize = 0;
    let mut out = String::new();
    for c in s.chars() {
        let cw = UnicodeWidthChar::width(c).unwrap_or(0);
        if width + cw > max_cols.saturating_sub(1) {
            out.push('…');
            return out;
        }
        out.push(c);
        width += cw;
    }
    out
}

/// Truncate to `max_cols` and right-pad with spaces to reach exactly `max_cols`.
fn truncate_pad(s: &str, max_cols: usize) -> String {
    let truncated = truncate(s, max_cols);
    let w = display_width(&truncated);
    format!("{truncated}{}", " ".repeat(max_cols.saturating_sub(w)))
}

/// Total display width of a string (sum of per-char widths).
fn display_width(s: &str) -> usize {
    s.chars()
        .map(|c| UnicodeWidthChar::width(c).unwrap_or(0))
        .sum()
}

fn active_file_preview_text(tool: &crate::app::state::ActiveToolCall) -> Option<String> {
    match tool.tool_name.as_str() {
        "WriteFile" => extract_partial_json_string_value(&tool.arguments, "content"),
        "EditFile" => extract_partial_json_string_value(&tool.arguments, "newText")
            .or_else(|| extract_partial_json_string_value(&tool.arguments, "content")),
        _ => None,
    }
}

fn is_shell_tool_name(name: &str) -> bool {
    matches!(name, "Exec" | "RunCommand" | "BashCommand")
}

fn active_shell_output_text(
    state: &AppState,
    tool: &crate::app::state::ActiveToolCall,
) -> Option<String> {
    let call_id = tool.call_id.as_str();
    state
        .streaming
        .active_command_executions
        .iter()
        .find(|exec| exec.call_id.as_deref() == Some(call_id))
        .and_then(|exec| {
            if exec.aggregated_output.is_empty() {
                None
            } else {
                Some(exec.aggregated_output.clone())
            }
        })
        .or_else(|| tool.result.clone())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::{
        app::state::{ActiveCommandExecution, HistoryEntry, PlanSnapshot, PlanTodo},
        i18n::load,
    };

    fn plain_text(lines: &[Line<'static>]) -> Vec<String> {
        lines
            .iter()
            .map(|line| {
                line.spans
                    .iter()
                    .map(|span| span.content.as_ref())
                    .collect::<String>()
            })
            .collect()
    }

    #[test]
    fn inline_plan_renders_checklist_statuses() {
        let theme = Theme::default();
        let strings = load("en");
        let plan = PlanSnapshot {
            title: "Ship plan UI".to_string(),
            overview: String::new(),
            todos: vec![
                PlanTodo {
                    id: "1".to_string(),
                    content: "Done item".to_string(),
                    priority: "high".to_string(),
                    status: "completed".to_string(),
                },
                PlanTodo {
                    id: "2".to_string(),
                    content: "Current item".to_string(),
                    priority: "high".to_string(),
                    status: "in_progress".to_string(),
                },
                PlanTodo {
                    id: "3".to_string(),
                    content: "Next item".to_string(),
                    priority: "high".to_string(),
                    status: "pending".to_string(),
                },
                PlanTodo {
                    id: "4".to_string(),
                    content: "Dropped item".to_string(),
                    priority: "high".to_string(),
                    status: "cancelled".to_string(),
                },
            ],
        };

        let lines = render_inline_plan_lines(&plan, 60, &theme, &strings);
        let text = plain_text(&lines);

        assert_eq!(text[0], "• Plan: Ship plan UI");
        assert!(text.iter().any(|line| line == "  [x] Done item"));
        assert!(text.iter().any(|line| line == "  [>] Current item"));
        assert!(text.iter().any(|line| line == "  [ ] Next item"));
        assert!(text.iter().any(|line| line == "  [-] Dropped item"));

        let completed_style = &lines[1].spans[1].style;
        assert!(completed_style.add_modifier.contains(Modifier::CROSSED_OUT));
    }

    #[test]
    fn inline_plan_wraps_overview_and_todos_with_alignment() {
        let theme = Theme::default();
        let strings = load("en");
        let plan = PlanSnapshot {
            title: "A narrow terminal plan title".to_string(),
            overview: "This overview should wrap into aligned follow-up lines.".to_string(),
            todos: vec![PlanTodo {
                id: "1".to_string(),
                content: "Investigate rendering issues in very narrow terminal widths".to_string(),
                priority: "high".to_string(),
                status: "in_progress".to_string(),
            }],
        };

        let text = plain_text(&render_inline_plan_lines(&plan, 24, &theme, &strings));

        assert_eq!(text[0], "• Plan: A narrow");
        assert_eq!(text[1], "  terminal plan title");
        assert_eq!(text[2], "  This overview should");
        assert_eq!(text[3], "  wrap into aligned");
        assert_eq!(text[4], "  follow-up lines.");
        assert_eq!(text[5], "  [>] Investigate");
        assert_eq!(text[6], "      rendering issues");
        assert_eq!(text[7], "      in very narrow");
        assert_eq!(text[8], "      terminal widths");
    }

    #[test]
    fn wrap_text_breaks_long_tokens() {
        let wrapped = wrap_text_display("supercalifragilisticexpialidocious", 8);
        assert_eq!(
            wrapped,
            vec!["supercal", "ifragili", "sticexpi", "alidocio", "us"]
        );
    }

    #[test]
    fn committed_exec_uses_live_elapsed_when_command_still_running() {
        let theme = Theme::default();
        let strings = load("en");
        let mut state = AppState::new("workspace".to_string());
        state.history.push(HistoryEntry::ToolCall {
            call_id: "call-live".to_string(),
            name: "Exec".to_string(),
            args: r#"{"command":"echo hi"}"#.to_string(),
            result: Some("chunk".to_string()),
            success: true,
            duration: Some(Duration::from_millis(100)),
        });
        state
            .streaming
            .active_command_executions
            .push(ActiveCommandExecution {
                item_id: "cmd-live".to_string(),
                call_id: Some("call-live".to_string()),
                command: "echo hi".to_string(),
                working_directory: None,
                source: Some("host".to_string()),
                aggregated_output: "chunk".to_string(),
                completed: false,
                started_at: std::time::Instant::now() - Duration::from_secs(3),
                duration: None,
                exit_code: None,
                status: "inProgress".to_string(),
            });

        let view = ChatView::new(&state, &theme, &strings);
        let mut out: Vec<Line<'static>> = Vec::new();
        let entry = state.history.first().expect("tool history exists");
        view.render_history_entry(entry, 80, &mut out);
        let text = plain_text(&out).join("\n");

        assert!(text.contains("(3."));
        assert!(!text.contains("(0.1s)"));
    }

    #[test]
    fn committed_exec_falls_back_to_committed_duration_without_active_execution() {
        let theme = Theme::default();
        let strings = load("en");
        let mut state = AppState::new("workspace".to_string());
        state.history.push(HistoryEntry::ToolCall {
            call_id: "call-final".to_string(),
            name: "Exec".to_string(),
            args: r#"{"command":"echo hi"}"#.to_string(),
            result: Some("done".to_string()),
            success: true,
            duration: Some(Duration::from_secs(2)),
        });

        let view = ChatView::new(&state, &theme, &strings);
        let mut out: Vec<Line<'static>> = Vec::new();
        let entry = state.history.first().expect("tool history exists");
        view.render_history_entry(entry, 80, &mut out);
        let text = plain_text(&out).join("\n");

        assert!(text.contains("(2.0s)"));
    }

    #[test]
    fn committed_todo_tool_omits_fixed_result_text() {
        let theme = Theme::default();
        let strings = load("en");
        let mut state = AppState::new("workspace".to_string());
        state.history.push(HistoryEntry::ToolCall {
            call_id: "todo-call".to_string(),
            name: "TodoWrite".to_string(),
            args: r#"{"merge":false,"todos":[{"id":"t1","content":"Stabilize prompt cache","status":"pending"}]}"#.to_string(),
            result: Some("Plan updated".to_string()),
            success: true,
            duration: Some(Duration::from_millis(80)),
        });

        let view = ChatView::new(&state, &theme, &strings);
        let mut out: Vec<Line<'static>> = Vec::new();
        let entry = state.history.first().expect("tool history exists");
        view.render_history_entry(entry, 80, &mut out);
        let text = plain_text(&out).join("\n");

        assert!(text.contains("Create to-do Stabilize prompt cache"));
        assert!(!text.contains("Plan updated"));
    }
}
