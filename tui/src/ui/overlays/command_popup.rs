// CommandPopup widget — slash command completion list below the input editor.
// Shown while the caret is editing the leading slash command token.

use crate::{
    app::state::{CommandPopupState, SlashCommandDescriptor},
    theme::Theme,
};
use ratatui::{
    buffer::Buffer,
    layout::Rect,
    style::Modifier,
    text::{Line, Span},
    widgets::{Clear, Paragraph, Widget},
};
use unicode_width::{UnicodeWidthChar, UnicodeWidthStr};

pub const MAX_POPUP_ROWS: usize = 8;

/// Filter commands by prefix and return matching (command, description) pairs.
pub fn filter_commands(input: &str, commands: &[SlashCommandDescriptor]) -> Vec<(String, String)> {
    let prefix = input.trim_start_matches('/');
    filter_commands_by_prefix(prefix, commands)
}

pub fn filter_commands_for_input(
    input: &str,
    cursor: usize,
    commands: &[SlashCommandDescriptor],
) -> Vec<(String, String)> {
    slash_command_filter(input, cursor)
        .map(|prefix| filter_commands_by_prefix(prefix, commands))
        .unwrap_or_default()
}

/// Returns the active slash command token if the caret is still inside it.
pub fn slash_command_filter(input: &str, cursor: usize) -> Option<&str> {
    let cursor = cursor.min(input.len());
    if !input.is_char_boundary(cursor) || cursor == 0 {
        return None;
    }

    let first_line_end = input.find('\n').unwrap_or(input.len());
    if cursor > first_line_end {
        return None;
    }

    let first_line = &input[..first_line_end];
    if !first_line.starts_with('/') {
        return None;
    }

    let after_slash = &first_line[1..];
    let token_len = after_slash
        .find(|ch: char| ch.is_whitespace())
        .unwrap_or(after_slash.len());
    let token_end = 1 + token_len;
    if cursor > token_end {
        return None;
    }

    Some(&after_slash[..token_len])
}

fn filter_commands_by_prefix(
    prefix: &str,
    commands: &[SlashCommandDescriptor],
) -> Vec<(String, String)> {
    commands
        .iter()
        .filter(|cmd| {
            let name = cmd.name.trim_start_matches('/');
            name.starts_with(prefix)
        })
        .map(|cmd| (cmd.name.clone(), cmd.description.clone()))
        .collect()
}

pub struct CommandPopup<'a> {
    pub popup_state: &'a CommandPopupState,
    pub theme: &'a Theme,
}

#[cfg(test)]
mod tests {
    use super::*;
    use ratatui::buffer::Buffer;

    #[test]
    fn filters_dynamic_commands_by_prefix() {
        let commands = vec![
            SlashCommandDescriptor::new("/skills", "Manage skills", "local-ui"),
            SlashCommandDescriptor::new("/code-review", "Custom review", "custom"),
            SlashCommandDescriptor::new("/cron", "List cron", "builtin"),
        ];
        let filtered = filter_commands("/co", &commands);
        assert_eq!(
            filtered,
            vec![("/code-review".to_string(), "Custom review".to_string())]
        );
    }

    #[test]
    fn slash_filter_hides_after_command_name() {
        assert_eq!(slash_command_filter("/", 1), Some(""));
        assert_eq!(slash_command_filter("/agent", 6), Some("agent"));
        assert_eq!(slash_command_filter("/agent ", 7), None);
        assert_eq!(slash_command_filter("hello /agent", 12), None);
        assert_eq!(slash_command_filter("/agent\nnext", 8), None);
    }

    #[test]
    fn render_uses_available_width_for_descriptions() {
        let theme = Theme::dark();
        let popup_state = CommandPopupState::new(vec![(
            "/create-hooks".to_string(),
            "Create lifecycle hooks without losing this important detail".to_string(),
        )]);
        let area = Rect::new(0, 0, 78, 1);
        let mut buf = Buffer::empty(area);

        CommandPopup::new(&popup_state, &theme).render(area, &mut buf);

        let text = row_text(&buf, area, 0);
        assert!(text.contains("/create-hooks"));
        assert!(text.contains("Create lifecycle hooks"));
        assert!(text.contains("important detail"));
    }

    fn row_text(buf: &Buffer, area: Rect, row: u16) -> String {
        (area.x..area.x + area.width)
            .map(|x| buf[(x, area.y + row)].symbol())
            .collect::<String>()
    }
}

impl<'a> CommandPopup<'a> {
    pub fn new(popup_state: &'a CommandPopupState, theme: &'a Theme) -> Self {
        Self { popup_state, theme }
    }

    pub fn preferred_height(popup_state: &CommandPopupState) -> u16 {
        popup_state.items.len().min(MAX_POPUP_ROWS) as u16
    }
}

impl Widget for CommandPopup<'_> {
    fn render(self, area: Rect, buf: &mut Buffer) {
        Clear.render(area, buf);

        if area.height == 0 || area.width < 5 || self.popup_state.items.is_empty() {
            return;
        }

        let visible_rows = area.height as usize;
        let start = self
            .popup_state
            .scroll_offset
            .min(self.popup_state.items.len().saturating_sub(1));
        let visible = self
            .popup_state
            .items
            .iter()
            .enumerate()
            .skip(start)
            .take(visible_rows);

        let command_width = self
            .popup_state
            .items
            .iter()
            .skip(start)
            .take(visible_rows)
            .map(|(cmd, _)| cmd.width())
            .max()
            .unwrap_or(0)
            .min(22);
        let desc_col = command_width + 3;
        let content_width = area.width as usize;

        let lines: Vec<Line> = visible
            .map(|(i, (cmd, desc))| {
                let is_selected = i == self.popup_state.selected;
                let command_style = if is_selected {
                    self.theme.input_border_plan.add_modifier(Modifier::BOLD)
                } else {
                    self.theme.agent_message
                };
                let desc_style = if is_selected {
                    self.theme.agent_message
                } else {
                    self.theme.dim
                };
                let cmd_text = truncate_display_width(cmd, command_width);
                let padding = desc_col.saturating_sub(cmd_text.width() + 1);
                let desc_width = content_width.saturating_sub(desc_col);
                let desc_text = truncate_display_width(desc, desc_width);
                Line::from(vec![
                    Span::raw(" "),
                    Span::styled(cmd_text, command_style),
                    Span::styled(" ".repeat(padding), self.theme.dim),
                    Span::styled(desc_text, desc_style),
                ])
            })
            .collect();

        Paragraph::new(lines).render(area, buf);
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
