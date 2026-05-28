// SkillPopup widget — `$skill` mention completion below the input.

use crate::{
    app::state::{SkillPopupState, SkillSuggestion},
    i18n::Strings,
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

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SkillMentionToken<'a> {
    pub start: usize,
    pub end: usize,
    pub query: &'a str,
}

pub fn skill_mention_filter(input: &str, cursor: usize) -> Option<SkillMentionToken<'_>> {
    let cursor = cursor.min(input.len());
    if !input.is_char_boundary(cursor) || cursor == 0 {
        return None;
    }

    let line_start = input[..cursor].rfind('\n').map(|idx| idx + 1).unwrap_or(0);
    let token_start = input[line_start..cursor]
        .rfind(char::is_whitespace)
        .map(|idx| line_start + idx + 1)
        .unwrap_or(line_start);
    if input.as_bytes().get(token_start) != Some(&b'$') {
        return None;
    }

    let token_end = input[cursor..]
        .find(char::is_whitespace)
        .map(|idx| cursor + idx)
        .unwrap_or(input.len());
    let query_start = token_start + 1;
    if cursor < query_start {
        return None;
    }
    Some(SkillMentionToken {
        start: token_start,
        end: token_end,
        query: &input[query_start..cursor],
    })
}

pub fn filter_skills_for_input(
    input: &str,
    cursor: usize,
    skills: Vec<SkillSuggestion>,
) -> Vec<SkillSuggestion> {
    let Some(token) = skill_mention_filter(input, cursor) else {
        return Vec::new();
    };
    let query = token.query.to_ascii_lowercase();
    let mut filtered: Vec<SkillSuggestion> = skills
        .into_iter()
        .filter(|skill| {
            query.is_empty()
                || skill.name.to_ascii_lowercase().contains(&query)
                || skill.display_name.to_ascii_lowercase().contains(&query)
                || skill.description.to_ascii_lowercase().contains(&query)
        })
        .collect();
    filtered.sort_by(|a, b| a.display_name.cmp(&b.display_name));
    filtered
}

pub struct SkillPopup<'a> {
    popup_state: &'a SkillPopupState,
    theme: &'a Theme,
    strings: &'a Strings,
}

impl<'a> SkillPopup<'a> {
    pub fn new(popup_state: &'a SkillPopupState, theme: &'a Theme, strings: &'a Strings) -> Self {
        Self {
            popup_state,
            theme,
            strings,
        }
    }

    pub fn preferred_height(popup_state: &SkillPopupState) -> u16 {
        let rows = popup_state.items.len().min(MAX_POPUP_ROWS);
        if rows == 0 {
            0
        } else {
            (rows + 2) as u16
        }
    }
}

impl Widget for SkillPopup<'_> {
    fn render(self, area: Rect, buf: &mut Buffer) {
        Clear.render(area, buf);
        if area.height == 0 || area.width < 8 || self.popup_state.items.is_empty() {
            return;
        }

        let list_h = area.height.saturating_sub(2).max(1);
        let list_area = Rect {
            height: list_h,
            ..area
        };
        let hint_area = Rect {
            y: area.y + list_h + 1,
            height: area.height.saturating_sub(list_h + 1).min(1),
            ..area
        };

        let visible_rows = list_area.height as usize;
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

        let name_width = self
            .popup_state
            .items
            .iter()
            .skip(start)
            .take(visible_rows)
            .map(|item| item.display_name.width())
            .max()
            .unwrap_or(0)
            .min(24);
        let meta_col = name_width + 3;
        let content_width = list_area.width as usize;

        let lines: Vec<Line> = visible
            .map(|(idx, item)| {
                let selected = idx == self.popup_state.selected;
                let name_style = if selected {
                    self.theme.input_border_plan.add_modifier(Modifier::BOLD)
                } else {
                    self.theme.agent_message
                };
                let desc_style = if selected {
                    self.theme.agent_message
                } else {
                    self.theme.dim
                };
                let name = truncate_display_width(&item.display_name, name_width);
                let padding = meta_col.saturating_sub(name.width() + 1);
                let desc_width = content_width.saturating_sub(meta_col);
                let desc = format!("{} {}", item.source_label, item.description);
                Line::from(vec![
                    Span::raw("  "),
                    Span::styled(name, name_style),
                    Span::styled(" ".repeat(padding), self.theme.dim),
                    Span::styled(truncate_display_width(&desc, desc_width), desc_style),
                ])
            })
            .collect();

        Paragraph::new(lines).render(list_area, buf);
        if hint_area.height > 0 {
            Paragraph::new(Line::from(Span::styled(
                format!("  {}", self.strings.skill_popup_insert_hint),
                self.theme.dim,
            )))
            .render(hint_area, buf);
        }
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

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn skill_mention_detects_current_token() {
        let token = skill_mention_filter("Use $browser now", 12).expect("token");
        assert_eq!(token.start, 4);
        assert_eq!(token.end, 12);
        assert_eq!(token.query, "browser");
        assert!(skill_mention_filter("Use $browser now", 13).is_none());
    }

    #[test]
    fn filters_skills_by_name_display_or_description() {
        let skills = vec![
            SkillSuggestion {
                name: "browser".to_string(),
                display_name: "Browser".to_string(),
                description: "Web automation".to_string(),
                source_label: "[Skill]".to_string(),
                enabled: true,
                available: true,
            },
            SkillSuggestion {
                name: "docs".to_string(),
                display_name: "Documents".to_string(),
                description: "Word files".to_string(),
                source_label: "[Skill]".to_string(),
                enabled: true,
                available: true,
            },
        ];

        let filtered = filter_skills_for_input("$web", 4, skills);
        assert_eq!(filtered.len(), 1);
        assert_eq!(filtered[0].name, "browser");
    }
}
