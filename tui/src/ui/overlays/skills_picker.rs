// SkillsPicker overlay — `/skills` enable/disable list.

use crate::{app::state::SkillsPickerState, i18n::Strings, theme::Theme, wire::types::SkillInfo};
use ratatui::{
    buffer::Buffer,
    layout::Rect,
    style::Modifier,
    text::{Line, Span},
    widgets::{Block, Borders, Clear, Paragraph, Widget},
};
use unicode_width::{UnicodeWidthChar, UnicodeWidthStr};

const MAX_ROWS: usize = 10;

pub struct SkillsPicker<'a> {
    picker: &'a SkillsPickerState,
    theme: &'a Theme,
    strings: &'a Strings,
}

impl<'a> SkillsPicker<'a> {
    pub fn new(picker: &'a SkillsPickerState, theme: &'a Theme, strings: &'a Strings) -> Self {
        Self {
            picker,
            theme,
            strings,
        }
    }
}

impl Widget for SkillsPicker<'_> {
    fn render(self, area: Rect, buf: &mut Buffer) {
        let width = area.width.min(96);
        let filtered = filtered_skill_indices(self.picker);
        let rows = filtered.len().min(MAX_ROWS).max(1) as u16;
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
            .title(" /skills ")
            .border_style(self.theme.dim);
        let inner = block.inner(popup);
        block.render(popup, buf);

        let mut lines: Vec<Line<'static>> = Vec::new();
        lines.push(Line::from(vec![
            Span::styled(
                format!("  {}", self.strings.skills_picker_title_line),
                self.theme.agent_message.add_modifier(Modifier::BOLD),
            ),
            Span::styled("  ", self.theme.dim),
            Span::styled(
                self.strings.skills_picker_search_hint.to_string(),
                self.theme.dim,
            ),
        ]));
        lines.push(Line::from(Span::styled(
            format!("  > {}", self.picker.search),
            self.theme.input_border_plan,
        )));

        if self.picker.loading {
            lines.push(Line::from(Span::styled(
                format!("  {}", self.strings.skills_picker_loading),
                self.theme.dim,
            )));
        } else if let Some(error) = &self.picker.error {
            lines.push(Line::from(Span::styled(
                format!("  {error}"),
                self.theme.error,
            )));
        } else if filtered.is_empty() {
            lines.push(Line::from(Span::styled(
                format!("  {}", self.strings.skills_picker_empty),
                self.theme.dim,
            )));
        } else {
            let start = self
                .picker
                .scroll_offset
                .min(filtered.len().saturating_sub(1));
            for (visible_idx, actual_idx) in filtered
                .iter()
                .copied()
                .enumerate()
                .skip(start)
                .take(MAX_ROWS)
            {
                let skill = &self.picker.skills[actual_idx];
                lines.push(skill_row(
                    skill,
                    visible_idx == self.picker.selected,
                    inner.width as usize,
                    self.theme,
                ));
            }
        }

        lines.push(Line::default());
        lines.push(Line::from(vec![
            Span::styled("  ↑/↓", self.theme.agent_message),
            Span::styled(
                format!(" {} · ", self.strings.picker_navigate_hint),
                self.theme.dim,
            ),
            Span::styled("space/enter", self.theme.agent_message),
            Span::styled(
                format!(" {} · ", self.strings.picker_toggle_hint),
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

fn skill_row(skill: &SkillInfo, selected: bool, width: usize, theme: &Theme) -> Line<'static> {
    let marker = if skill.enabled { "x" } else { " " };
    let prefix = if selected { "›" } else { " " };
    let name = skill.display_name.as_deref().unwrap_or(skill.name.as_str());
    let status = if skill.available {
        format!("[{marker}]")
    } else {
        "[!]".to_string()
    };
    let source = skill
        .plugin_display_name
        .as_deref()
        .filter(|value| !value.trim().is_empty())
        .map(|plugin| format!("[Plugin] {plugin}"))
        .unwrap_or_else(|| format!("[{}]", skill.source));
    let description = skill
        .short_description
        .as_deref()
        .filter(|value| !value.trim().is_empty())
        .unwrap_or(skill.description.as_str());
    let left = format!("  {prefix} {status} {name}");
    let desc = format!("{source} {description}");
    let left_w = left.width();
    let desc_w = width.saturating_sub(left_w + 2);
    let style = if selected {
        theme.input_border_plan.add_modifier(Modifier::BOLD)
    } else {
        theme.agent_message
    };
    Line::from(vec![
        Span::styled(truncate_display_width(&left, width), style),
        Span::styled("  ", theme.dim),
        Span::styled(truncate_display_width(&desc, desc_w), theme.dim),
    ])
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
