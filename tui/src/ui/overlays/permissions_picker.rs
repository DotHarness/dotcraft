// PermissionsPicker overlay — `/permissions` preset chooser.

use crate::{app::state::PermissionsPickerState, i18n::Strings, theme::Theme};
use ratatui::{
    buffer::Buffer,
    layout::Rect,
    style::Modifier,
    text::{Line, Span},
    widgets::{Block, Borders, Clear, Paragraph, Widget},
};
use unicode_width::{UnicodeWidthChar, UnicodeWidthStr};

pub struct PermissionsPicker<'a> {
    picker: &'a PermissionsPickerState,
    theme: &'a Theme,
    strings: &'a Strings,
}

impl<'a> PermissionsPicker<'a> {
    pub fn new(picker: &'a PermissionsPickerState, theme: &'a Theme, strings: &'a Strings) -> Self {
        Self {
            picker,
            theme,
            strings,
        }
    }
}

impl Widget for PermissionsPicker<'_> {
    fn render(self, area: Rect, buf: &mut Buffer) {
        let width = area.width.min(86);
        let rows = self.picker.options.len().max(1) as u16;
        let height = (rows + 5).min(area.height.saturating_sub(2).max(1));
        let x = area.x + area.width.saturating_sub(width) / 2;
        let y = area.y + area.height.saturating_sub(height) / 3;
        let popup = Rect {
            x,
            y,
            width,
            height,
        };

        Clear.render(popup, buf);
        let block = Block::default()
            .borders(Borders::ALL)
            .title(" /permissions ")
            .border_style(self.theme.dim);
        let inner = block.inner(popup);
        block.render(popup, buf);

        let mut lines = Vec::new();
        lines.push(Line::from(vec![
            Span::styled(
                self.strings.permissions_picker_title.to_string(),
                self.theme.agent_message.add_modifier(Modifier::BOLD),
            ),
            Span::styled(
                format!("  {}", self.strings.permissions_picker_subtitle),
                self.theme.dim,
            ),
        ]));
        lines.push(Line::default());

        if let Some(error) = &self.picker.error {
            lines.push(Line::from(Span::styled(
                format!("  {error}"),
                self.theme.error,
            )));
        }

        for (idx, option) in self.picker.options.iter().enumerate() {
            let selected = idx == self.picker.selected;
            let prefix = if selected { "›" } else { " " };
            let style = if selected {
                self.theme.input_border_plan.add_modifier(Modifier::BOLD)
            } else {
                self.theme.agent_message
            };
            let left = format!("  {prefix} {}", option.label);
            let desc_w = inner.width as usize;
            lines.push(Line::from(vec![
                Span::styled(truncate_display_width(&left, 26), style),
                Span::styled("  ", self.theme.dim),
                Span::styled(
                    truncate_display_width(&option.description, desc_w.saturating_sub(28)),
                    self.theme.dim,
                ),
            ]));
        }

        lines.push(Line::default());
        lines.push(Line::from(vec![
            Span::styled("  ↑/↓", self.theme.agent_message),
            Span::styled(
                format!(" {} · ", self.strings.picker_navigate_hint),
                self.theme.dim,
            ),
            Span::styled("enter", self.theme.agent_message),
            Span::styled(
                format!(" {} · ", self.strings.picker_apply_hint),
                self.theme.dim,
            ),
            Span::styled("esc", self.theme.agent_message),
            Span::styled(
                format!(" {}", self.strings.picker_close_hint),
                self.theme.dim,
            ),
        ]));

        Paragraph::new(lines).render(inner, buf);
    }
}

fn truncate_display_width(text: &str, max_width: usize) -> String {
    if max_width == 0 {
        return String::new();
    }
    if text.width() <= max_width {
        return text.to_string();
    }
    if max_width <= 1 {
        return "…".to_string();
    }
    let mut out = String::new();
    let mut used = 0usize;
    let limit = max_width - 1;
    for ch in text.chars() {
        let ch_width = UnicodeWidthChar::width(ch).unwrap_or(0);
        if used + ch_width > limit {
            break;
        }
        out.push(ch);
        used += ch_width;
    }
    out.push('…');
    out
}
