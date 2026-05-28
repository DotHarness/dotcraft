// ThreadPicker overlay — shown when the user runs /sessions.
// Displays a scrollable list of threads with resume/archive/delete actions.

use crate::{app::state::ThreadPickerState, i18n::Strings, theme::Theme};
use ratatui::{
    buffer::Buffer,
    layout::{Alignment, Constraint, Direction, Layout, Rect},
    text::{Line, Span},
    widgets::{Block, Borders, Clear, Paragraph, Widget},
};
use unicode_width::UnicodeWidthChar;

pub struct ThreadPicker<'a> {
    pub picker: &'a ThreadPickerState,
    pub theme: &'a Theme,
    pub strings: &'a Strings,
}

impl<'a> ThreadPicker<'a> {
    pub fn new(picker: &'a ThreadPickerState, theme: &'a Theme, strings: &'a Strings) -> Self {
        Self {
            picker,
            theme,
            strings,
        }
    }

    /// Centered popup: 70% width, 80% height, min 50×10.
    pub fn popup_area(full: Rect) -> Rect {
        let popup_width = (full.width * 70 / 100).max(50).min(full.width);
        let popup_height = (full.height * 80 / 100)
            .max(10)
            .min(full.height.saturating_sub(2));
        let x = full.x + (full.width.saturating_sub(popup_width)) / 2;
        let y = full.y + (full.height.saturating_sub(popup_height)) / 2;
        Rect {
            x,
            y,
            width: popup_width,
            height: popup_height,
        }
    }
}

impl Widget for ThreadPicker<'_> {
    fn render(self, area: Rect, buf: &mut Buffer) {
        let popup = Self::popup_area(area);

        // Clear behind modal.
        Clear.render(popup, buf);

        let block = Block::default()
            .borders(Borders::ALL)
            .border_style(self.theme.approval_border)
            .title(Line::from(Span::styled(
                format!(" {} ", self.strings.sessions_title),
                self.theme.approval_border,
            )));

        let inner = block.inner(popup);
        block.render(popup, buf);

        if inner.height < 3 {
            return;
        }

        // Split inner into list area and footer hint bar (1 row).
        let chunks = Layout::default()
            .direction(Direction::Vertical)
            .constraints([Constraint::Min(1), Constraint::Length(1)])
            .split(inner);
        let list_area = chunks[0];
        let hint_area = chunks[1];

        // ── Footer hint bar ───────────────────────────────────────────────
        let hints = Line::from(vec![
            Span::styled(
                format!(" {}  ", self.strings.sessions_resume_hint),
                self.theme.dim,
            ),
            Span::styled(
                format!("{}  ", self.strings.sessions_archive_hint),
                self.theme.dim,
            ),
            Span::styled(
                format!("{}  ", self.strings.sessions_delete_hint),
                self.theme.dim,
            ),
            Span::styled(self.strings.sessions_close_hint, self.theme.dim),
        ]);
        Paragraph::new(hints).render(hint_area, buf);

        // ── Thread list ───────────────────────────────────────────────────
        if self.picker.loading {
            Paragraph::new(Span::styled(self.strings.sessions_loading, self.theme.dim))
                .render(list_area, buf);
            return;
        }

        if let Some(err) = &self.picker.error {
            Paragraph::new(Span::styled(err.as_str(), self.theme.tool_error))
                .render(list_area, buf);
            return;
        }

        if self.picker.threads.is_empty() {
            Paragraph::new(Span::styled(self.strings.sessions_empty, self.theme.dim))
                .alignment(Alignment::Center)
                .render(list_area, buf);
            return;
        }

        // Compute visible window: scroll to keep selected row in view.
        let visible_rows = list_area.height as usize;
        let selected = self
            .picker
            .selected
            .min(self.picker.threads.len().saturating_sub(1));
        let scroll_top = if selected >= visible_rows {
            selected - visible_rows + 1
        } else {
            0
        };

        let list_width = list_area.width as usize;
        let mut lines: Vec<Line> = Vec::new();

        for (i, thread) in self
            .picker
            .threads
            .iter()
            .enumerate()
            .skip(scroll_top)
            .take(visible_rows)
        {
            let is_selected = i == selected;

            let name = thread.display_name.as_deref().unwrap_or(thread.id.as_str());

            // Status badge: active/archived/deleted
            let status_badge = match thread.status.as_str() {
                "active" => Span::styled(" [active] ", self.theme.tool_completed),
                "archived" => Span::styled(" [archived] ", self.theme.dim),
                other => Span::styled(format!(" [{other}] "), self.theme.dim),
            };

            // Truncate name to fit: width - badge(~12) - prefix(~2) - date(~10)
            let max_name = list_width.saturating_sub(24);
            let display_name = truncate_display_width(name, max_name);

            // Time — first 10 chars of ISO timestamp (the date portion).
            // Use char-boundary-safe truncation to avoid panics on non-ASCII timestamps.
            let time_str: String = thread.last_active_at.chars().take(10).collect();

            let prefix = if is_selected { "► " } else { "  " };
            let name_style = if is_selected {
                self.theme.approval_border
            } else {
                self.theme.agent_message
            };

            lines.push(Line::from(vec![
                Span::styled(prefix, name_style),
                Span::styled(display_name, name_style),
                status_badge,
                Span::styled(time_str, self.theme.dim),
            ]));
        }

        Paragraph::new(lines).render(list_area, buf);
    }
}

/// Truncate `s` to at most `max_cols` display columns, appending '…' if truncated.
/// Uses per-character display width so CJK and other wide characters are handled safely.
fn truncate_display_width(s: &str, max_cols: usize) -> String {
    if max_cols == 0 {
        return String::new();
    }
    let total_width: usize = s
        .chars()
        .map(|c| UnicodeWidthChar::width(c).unwrap_or(0))
        .sum();
    if total_width <= max_cols {
        return s.to_string();
    }
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
