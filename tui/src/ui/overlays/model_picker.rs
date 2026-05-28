use crate::{app::state::ModelPickerState, i18n::Strings, theme::Theme};
use ratatui::{
    buffer::Buffer,
    layout::{Alignment, Constraint, Direction, Layout, Rect},
    style::Modifier,
    text::{Line, Span},
    widgets::{Block, Borders, Clear, Paragraph, Widget},
};

const SPINNER: &[char] = &['⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏'];

pub struct ModelPicker<'a> {
    pub picker: &'a ModelPickerState,
    pub tick_count: u64,
    pub theme: &'a Theme,
    pub strings: &'a Strings,
}

impl<'a> ModelPicker<'a> {
    pub fn new(
        picker: &'a ModelPickerState,
        tick_count: u64,
        theme: &'a Theme,
        strings: &'a Strings,
    ) -> Self {
        Self {
            picker,
            tick_count,
            theme,
            strings,
        }
    }

    pub fn popup_area(full: Rect) -> Rect {
        let popup_width = (full.width * 60 / 100).max(44).min(full.width);
        let popup_height = (full.height * 60 / 100)
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

impl Widget for ModelPicker<'_> {
    fn render(self, area: Rect, buf: &mut Buffer) {
        let popup = Self::popup_area(area);
        Clear.render(popup, buf);

        let block = Block::default()
            .borders(Borders::ALL)
            .border_style(self.theme.approval_border)
            .title(Line::from(Span::styled(
                format!(" {} ", self.strings.model_picker_title),
                self.theme.approval_border,
            )));

        let inner = block.inner(popup);
        block.render(popup, buf);
        if inner.height < 3 {
            return;
        }

        let chunks = Layout::default()
            .direction(Direction::Vertical)
            .constraints([Constraint::Min(1), Constraint::Length(1)])
            .split(inner);
        let list_area = chunks[0];
        let hint_area = chunks[1];

        Paragraph::new(Line::from(vec![
            Span::styled(
                format!(" {}  ", self.strings.model_picker_select_hint),
                self.theme.dim,
            ),
            Span::styled(self.strings.model_picker_close_hint, self.theme.dim),
        ]))
        .render(hint_area, buf);

        if self.picker.loading {
            let frame = SPINNER[self.tick_count as usize % SPINNER.len()];
            Paragraph::new(Span::styled(
                format!("{frame} {}", self.strings.model_picker_loading),
                self.theme.dim,
            ))
            .alignment(Alignment::Center)
            .render(list_area, buf);
            return;
        }

        if let Some(err) = &self.picker.error {
            Paragraph::new(Span::styled(err.as_str(), self.theme.tool_error))
                .alignment(Alignment::Center)
                .render(list_area, buf);
            return;
        }

        let selectable_count = self.picker.reasoning_options.len() + self.picker.models.len();
        if selectable_count == 0 {
            Paragraph::new(Span::styled(
                self.strings.model_picker_empty,
                self.theme.dim,
            ))
            .alignment(Alignment::Center)
            .render(list_area, buf);
            return;
        }

        let mut rows: Vec<PickerRow> = Vec::new();
        if !self.picker.reasoning_options.is_empty() {
            rows.push(PickerRow::Heading("Thinking".to_string()));
            rows.extend(
                self.picker
                    .reasoning_options
                    .iter()
                    .enumerate()
                    .map(|(idx, option)| PickerRow::Option {
                        selected_index: idx,
                        label: option.label.clone(),
                        disabled: option.disabled,
                    }),
            );
        }
        if !self.picker.models.is_empty() {
            if !rows.is_empty() {
                rows.push(PickerRow::Spacer);
            }
            rows.push(PickerRow::Heading("Model".to_string()));
            let offset = self.picker.reasoning_options.len();
            rows.extend(self.picker.models.iter().enumerate().map(|(idx, model)| {
                let label = if model.id.eq_ignore_ascii_case("default") {
                    self.strings.model_default_label.to_string()
                } else {
                    model.id.clone()
                };
                PickerRow::Option {
                    selected_index: offset + idx,
                    label,
                    disabled: false,
                }
            }));
        }

        let visible_rows = list_area.height as usize;
        let selected = self.picker.selected.min(selectable_count.saturating_sub(1));
        let selected_row = rows
            .iter()
            .position(|row| matches!(row, PickerRow::Option { selected_index, .. } if *selected_index == selected))
            .unwrap_or(0);
        let scroll_top = if selected_row >= visible_rows {
            selected_row - visible_rows + 1
        } else {
            0
        };

        let lines: Vec<Line> = rows
            .iter()
            .skip(scroll_top)
            .take(visible_rows)
            .map(|row| match row {
                PickerRow::Heading(label) => Line::from(Span::styled(
                    format!("  {label}"),
                    self.theme.dim.add_modifier(Modifier::BOLD),
                )),
                PickerRow::Spacer => Line::default(),
                PickerRow::Option {
                    selected_index,
                    label,
                    disabled,
                } => {
                    let is_selected = *selected_index == selected;
                    let prefix = if is_selected { "► " } else { "  " };
                    let style = if *disabled {
                        self.theme.dim
                    } else if is_selected {
                        self.theme.approval_border
                    } else {
                        self.theme.agent_message
                    };
                    Line::from(vec![
                        Span::styled(prefix, style),
                        Span::styled(label.clone(), style),
                    ])
                }
            })
            .collect();

        Paragraph::new(lines).render(list_area, buf);
    }
}

enum PickerRow {
    Heading(String),
    Spacer,
    Option {
        selected_index: usize,
        label: String,
        disabled: bool,
    },
}
