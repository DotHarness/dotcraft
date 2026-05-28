// FooterLine widget (§8.2 of specs/clients/tui-client.md).
// On-demand composer status line rendered below InputEditor.

use crate::{
    app::state::{AppState, HistoryEntry, TurnStatus},
    i18n::Strings,
    theme::Theme,
};
use ratatui::{
    buffer::Buffer,
    layout::Rect,
    text::{Line, Span},
    widgets::{Paragraph, Widget},
};
use unicode_width::{UnicodeWidthChar, UnicodeWidthStr};

pub struct FooterLine<'a> {
    state: &'a AppState,
    theme: &'a Theme,
    strings: &'a Strings,
}

impl<'a> FooterLine<'a> {
    pub fn new(state: &'a AppState, theme: &'a Theme, strings: &'a Strings) -> Self {
        Self {
            state,
            theme,
            strings,
        }
    }

    pub fn preferred_height(state: &AppState) -> u16 {
        if should_render_footer(state) {
            1
        } else {
            0
        }
    }
}

impl Widget for FooterLine<'_> {
    fn render(self, area: Rect, buf: &mut Buffer) {
        if area.height == 0 || area.width < 4 {
            return;
        }

        let width = area.width as usize;
        let Some((left_text, left_style, right)) =
            footer_content(self.state, self.theme, self.strings)
        else {
            return;
        };

        let right_width = right
            .as_ref()
            .map(|(text, _)| text.width().saturating_add(1))
            .unwrap_or(0);
        let left_width = width.saturating_sub(right_width);
        let left_text = truncate_display(&format!("  {left_text}"), left_width);
        if left_text.is_empty() && right.is_none() {
            return;
        }
        let left_w = left_text.width();
        let mut spans: Vec<Span<'static>> = Vec::new();
        spans.push(Span::styled(left_text, left_style));

        if let Some((right_text, right_style)) = right {
            let right_w = right_text.width();
            let pad = width.saturating_sub(left_w + right_w);
            spans.push(Span::raw(" ".repeat(pad)));
            spans.push(Span::styled(right_text, right_style));
        }

        Paragraph::new(Line::from(spans)).render(area, buf);
    }
}

fn should_render_footer(state: &AppState) -> bool {
    quit_pending(state)
        || state.command_popup.is_some()
        || state.skill_popup.is_some()
        || !state.connected
        || !state.input_text.is_empty()
        || is_turn_active(state)
        || !state.pending_input.is_empty()
}

fn footer_content(
    state: &AppState,
    theme: &Theme,
    strings: &Strings,
) -> Option<(
    String,
    ratatui::style::Style,
    Option<(String, ratatui::style::Style)>,
)> {
    let is_running = is_turn_active(state);
    let right_tokens = if is_running {
        let token_str = state.token_tracker.format_compact();
        if token_str.is_empty() {
            None
        } else {
            Some((token_str, theme.dim))
        }
    } else {
        None
    };

    if quit_pending(state) {
        return Some((
            strings.quit_confirm_hint.to_string(),
            theme.error,
            right_tokens,
        ));
    }

    if state.command_popup.is_some() {
        return Some((strings.command_popup_hint.to_string(), theme.dim, None));
    }

    if state.skill_popup.is_some() {
        return Some((strings.skill_popup_hint.to_string(), theme.dim, None));
    }

    if !state.connected {
        return Some((connection_status_text(state, strings), theme.error, None));
    }

    if !state.input_text.is_empty() && is_running {
        return Some((strings.tab_to_queue.to_string(), theme.dim, right_tokens));
    }

    if !state.input_text.is_empty() {
        return Some((strings.enter_to_send_hint.to_string(), theme.dim, None));
    }

    if is_running {
        return Some((
            strings.esc_to_interrupt.to_string(),
            theme.dim,
            right_tokens,
        ));
    }

    if !state.pending_input.is_empty() {
        return Some((
            format!(
                "{} {}",
                strings.pending_queued_prefix,
                state.pending_input.len()
            ),
            theme.dim,
            None,
        ));
    }

    None
}

fn is_turn_active(state: &AppState) -> bool {
    state.turn_status == TurnStatus::Running
        || state.turn_status == TurnStatus::WaitingApproval
        || state.turn_status == TurnStatus::WaitingInput
}

fn quit_pending(state: &AppState) -> bool {
    state
        .last_interrupt_at
        .is_some_and(|t| t.elapsed().as_secs_f32() < 1.0)
}

fn connection_status_text(state: &AppState, strings: &Strings) -> String {
    if let Some(message) = state.history.iter().rev().find_map(|entry| match entry {
        HistoryEntry::Error { message } => Some(message.as_str()),
        _ => None,
    }) {
        let detail = message
            .strip_prefix("Connection failed: ")
            .unwrap_or(message)
            .trim();
        return format!("{}: {detail}", strings.welcome_connection_failed);
    }

    strings.welcome_connecting.to_string()
}

fn truncate_display(text: &str, max_width: usize) -> String {
    if max_width == 0 {
        return String::new();
    }
    if text.width() <= max_width {
        return text.to_string();
    }
    if max_width <= 3 {
        return ".".repeat(max_width);
    }
    let mut out = String::new();
    let mut used = 0usize;
    let limit = max_width - 3;
    for ch in text.chars() {
        let cw = UnicodeWidthChar::width(ch).unwrap_or(0);
        if used + cw > limit {
            break;
        }
        out.push(ch);
        used += cw;
    }
    out.push_str("...");
    out
}

#[cfg(test)]
mod tests {
    use super::{truncate_display, FooterLine};
    use crate::app::state::{AppState, CommandPopupState, TurnStatus};

    #[test]
    fn truncate_display_keeps_short_text() {
        assert_eq!(truncate_display("thread_123", 20), "thread_123");
    }

    #[test]
    fn truncate_display_applies_ellipsis() {
        assert_eq!(
            truncate_display("thread_20260415_0ueyzv", 12),
            "thread_20..."
        );
    }

    #[test]
    fn preferred_height_is_zero_when_idle_connected_and_empty() {
        let mut state = AppState::new("workspace".to_string());
        state.connected = true;

        assert_eq!(FooterLine::preferred_height(&state), 0);
    }

    #[test]
    fn preferred_height_appears_for_contextual_states() {
        let disconnected = AppState::new("workspace".to_string());
        assert_eq!(FooterLine::preferred_height(&disconnected), 1);

        let mut draft = AppState::new("workspace".to_string());
        draft.connected = true;
        draft.input_text = "hello".to_string();
        assert_eq!(FooterLine::preferred_height(&draft), 1);

        let mut running = AppState::new("workspace".to_string());
        running.connected = true;
        running.turn_status = TurnStatus::Running;
        assert_eq!(FooterLine::preferred_height(&running), 1);

        let mut popup = AppState::new("workspace".to_string());
        popup.connected = true;
        popup.command_popup = Some(CommandPopupState::new(vec![(
            "/skills".to_string(),
            "Manage skills".to_string(),
        )]));
        assert_eq!(FooterLine::preferred_height(&popup), 1);
    }
}
