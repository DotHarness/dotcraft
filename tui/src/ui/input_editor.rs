// InputEditor widget (§8.4.3 of specs/clients/tui-client.md).
// No top separator — hints moved to FooterLine below.
// Mode gutter prefix on the left; placeholder when empty.

use crate::{
    app::state::{AgentMode, AppState},
    i18n::Strings,
    theme::Theme,
};
use ratatui::{
    buffer::Buffer,
    layout::Rect,
    text::{Line, Span},
    widgets::{Paragraph, Widget, Wrap},
};
use unicode_width::{UnicodeWidthChar, UnicodeWidthStr};

/// Width of the mode gutter prefix ("❯ " or "✎ ").
const GUTTER_COLS: u16 = 2;

pub struct InputEditor<'a> {
    state: &'a AppState,
    theme: &'a Theme,
    strings: &'a Strings,
}

impl<'a> InputEditor<'a> {
    pub fn new(state: &'a AppState, theme: &'a Theme, strings: &'a Strings) -> Self {
        Self {
            state,
            theme,
            strings,
        }
    }

    /// Preferred height accounting for visual line wrapping and CJK wide characters.
    /// `area_width` is the full terminal width; GUTTER_COLS are subtracted internally.
    /// Min 1 row, max 10 rows.
    pub fn preferred_height(state: &AppState, area_width: u16) -> u16 {
        let inner_w = area_width.saturating_sub(GUTTER_COLS).max(1) as usize;
        let text = &state.input_text;
        let mut visual_rows: usize = 0;
        // split('\n') correctly yields a trailing empty segment for a trailing newline,
        // unlike str::lines() which strips it.
        for logical_line in text.split('\n') {
            let w = logical_line.width();
            visual_rows += if w == 0 {
                1
            } else {
                (w + inner_w - 1) / inner_w
            };
        }
        (visual_rows.max(1) as u16).clamp(1, 10)
    }
}

impl Widget for InputEditor<'_> {
    fn render(self, area: Rect, buf: &mut Buffer) {
        if area.height == 0 || area.width < 4 {
            return;
        }

        let mode_style = match self.state.mode {
            AgentMode::Agent => self.theme.input_border_agent,
            AgentMode::Plan => self.theme.input_border_plan,
        };

        // Mode gutter on the first text line.
        let gutter_str = match self.state.mode {
            AgentMode::Agent => "❯ ",
            AgentMode::Plan => "✎ ",
        };
        buf.set_string(area.x, area.y, gutter_str, mode_style);

        // Continuation gutter for lines 2+.
        for row in 1..area.height {
            buf.set_string(area.x, area.y + row, "  ", self.theme.dim);
        }

        // Inner rect for text content (after gutter).
        let inner = Rect {
            x: area.x + GUTTER_COLS,
            y: area.y,
            width: area.width.saturating_sub(GUTTER_COLS),
            height: area.height,
        };

        if self.state.input_text.is_empty() {
            Paragraph::new(Line::from(Span::styled(
                self.strings.placeholder,
                self.theme.input_placeholder,
            )))
            .render(inner, buf);
        } else {
            let lines: Vec<Line> = self
                .state
                .input_text
                .lines()
                .map(|l| Line::from(l.to_string()))
                .collect();
            let lines = if lines.is_empty() {
                vec![Line::default()]
            } else {
                lines
            };
            Paragraph::new(lines)
                .wrap(Wrap { trim: false })
                .render(inner, buf);
        }
    }
}

/// Compute 2D cursor position (row, col) from a flat byte offset in a multi-line string,
/// accounting for visual line wrapping at `inner_width` display columns.
///
/// `inner_width` is the text area width after subtracting the gutter (GUTTER_COLS).
/// CJK and other wide characters count as 2 display columns each.
pub fn offset_to_2d(text: &str, byte_offset: usize, inner_width: u16) -> (u16, u16) {
    let iw = inner_width.max(1) as usize;
    let safe_offset = byte_offset.min(text.len());
    let mut row: usize = 0;
    let mut col_width: usize = 0;

    for (i, c) in text.char_indices() {
        if i >= safe_offset {
            break;
        }
        if c == '\n' {
            row += 1;
            col_width = 0;
        } else {
            let cw = UnicodeWidthChar::width(c).unwrap_or(0);
            if col_width + cw > iw {
                // Character would overflow this visual row; wrap to next.
                row += 1;
                col_width = cw;
            } else {
                col_width += cw;
            }
        }
    }

    (row as u16, col_width as u16)
}
