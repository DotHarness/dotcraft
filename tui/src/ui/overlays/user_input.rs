use crate::{app::state::UserInputRequestState, theme::Theme};
use ratatui::{
    buffer::Buffer,
    layout::Rect,
    text::{Line, Span},
    widgets::{Block, Borders, Paragraph, Widget},
};
use unicode_width::UnicodeWidthStr;

const OTHER_PLACEHOLDER: &str = "No, and tell DotCraft what to do differently";

pub struct UserInputOverlay<'a> {
    pub request: &'a UserInputRequestState,
    pub theme: &'a Theme,
}

impl<'a> UserInputOverlay<'a> {
    pub fn new(request: &'a UserInputRequestState, theme: &'a Theme) -> Self {
        Self { request, theme }
    }

    pub fn preferred_height(request: &UserInputRequestState, width: u16) -> u16 {
        let Some(question) = request.questions.get(request.current_question) else {
            return 3;
        };
        let inner_width = width.saturating_sub(4).max(1);
        let question_rows = wrap_text(&question.question, inner_width).len().clamp(1, 2) as u16;
        let option_rows = question.options.len() as u16 + u16::from(question.is_other);
        // Border + title + question + options + footer.
        (2 + 1 + question_rows + option_rows + 1).clamp(5, 10)
    }

    pub fn cursor_position(&self, area: Rect) -> Option<(u16, u16)> {
        let question = self.request.questions.get(self.request.current_question)?;
        if !question.is_other {
            return None;
        }
        let selected = self
            .request
            .selected
            .get(self.request.current_question)
            .copied()
            .unwrap_or(0);
        let other_index = question.options.len();
        if selected != other_index {
            return None;
        }

        let inner = inner_area(area);
        if inner.width == 0 || inner.height == 0 {
            return None;
        }
        let question_rows = wrap_text(&question.question, inner.width).len().clamp(1, 2) as u16;
        let row = 1 + question_rows + other_index as u16;
        if row >= inner.height {
            return None;
        }
        let text = self
            .request
            .other_text
            .get(self.request.current_question)
            .map(String::as_str)
            .unwrap_or("");
        let prefix_width =
            UnicodeWidthStr::width(format!("› {}. ", other_index + 1).as_str()) as u16;
        let text_width = UnicodeWidthStr::width(text) as u16;
        let x = inner
            .x
            .saturating_add(prefix_width)
            .saturating_add(text_width)
            .min(inner.x + inner.width.saturating_sub(1));
        Some((x, inner.y + row))
    }
}

impl Widget for UserInputOverlay<'_> {
    fn render(self, area: Rect, buf: &mut Buffer) {
        if area.height == 0 || area.width < 8 {
            return;
        }

        let title = if self.request.questions.len() > 1 {
            format!(
                " Asking question {}/{} ",
                self.request.current_question + 1,
                self.request.questions.len()
            )
        } else {
            " Asking question ".to_string()
        };
        let block = Block::default()
            .borders(Borders::ALL)
            .border_style(self.theme.approval_border)
            .title(Line::from(Span::styled(title, self.theme.approval_border)));
        let inner = block.inner(area);
        block.render(area, buf);

        let Some(question) = self.request.questions.get(self.request.current_question) else {
            return;
        };
        if inner.height == 0 || inner.width == 0 {
            return;
        }

        let selected = self
            .request
            .selected
            .get(self.request.current_question)
            .copied()
            .unwrap_or(0);
        let option_count = question.options.len() + usize::from(question.is_other);
        let mut lines = Vec::new();
        let mut question_lines = wrap_text(&question.question, inner.width);
        question_lines.truncate(2);
        for line in question_lines {
            lines.push(Line::from(Span::styled(line, self.theme.agent_message)));
        }

        for (index, option) in question.options.iter().enumerate() {
            let is_selected = selected == index;
            lines.push(option_line(
                index,
                option.label.as_str(),
                option.description.as_str(),
                is_selected,
                selected == index && index > 0,
                selected == index && index + 1 < option_count,
                self.theme,
                inner.width,
            ));
        }

        if question.is_other {
            let text = self
                .request
                .other_text
                .get(self.request.current_question)
                .map(String::as_str)
                .unwrap_or("");
            let other_index = question.options.len();
            lines.push(other_line(
                other_index,
                text,
                selected == other_index,
                selected == other_index && other_index > 0,
                false,
                self.theme,
                inner.width,
            ));
        }

        let selected_other = question.is_other && selected == question.options.len();
        let footer = if self.request.questions.len() > 1 && selected_other {
            "Up/Down select  Left/Right question  1-4 choose  Enter submit  Esc dismiss"
        } else if self.request.questions.len() > 1 {
            "Up/Down select  Left/Right or h/l question  1-4 choose  Enter submit  Esc dismiss"
        } else {
            "Up/Down select  1-4 choose  Enter submit  Esc dismiss"
        };
        lines.push(Line::from(Span::styled(footer, self.theme.dim)));

        Paragraph::new(lines).render(inner, buf);
    }
}

fn option_line(
    index: usize,
    label: &str,
    description: &str,
    selected: bool,
    can_move_up: bool,
    can_move_down: bool,
    theme: &Theme,
    width: u16,
) -> Line<'static> {
    let marker = if selected { "›" } else { " " };
    let mut spans = vec![
        Span::styled(format!("{marker} {}. ", index + 1), theme.dim),
        Span::styled(
            label.to_string(),
            if selected {
                theme.approval_border
            } else {
                theme.agent_message
            },
        ),
    ];
    if !description.trim().is_empty() {
        spans.push(Span::styled("  ".to_string(), theme.dim));
        spans.push(Span::styled(description.trim().to_string(), theme.dim));
    }

    if selected {
        let current_width = spans
            .iter()
            .map(|span| UnicodeWidthStr::width(span.content.as_ref()))
            .sum::<usize>();
        let arrow_width = 3usize;
        let available = width as usize;
        let pad = available.saturating_sub(current_width + arrow_width).max(1);
        spans.push(Span::raw(" ".repeat(pad)));
        spans.push(Span::styled(
            "↑",
            if can_move_up {
                theme.approval_border
            } else {
                theme.dim
            },
        ));
        spans.push(Span::raw(" "));
        spans.push(Span::styled(
            "↓",
            if can_move_down {
                theme.approval_border
            } else {
                theme.dim
            },
        ));
    }

    Line::from(spans)
}

fn other_line(
    index: usize,
    text: &str,
    selected: bool,
    can_move_up: bool,
    can_move_down: bool,
    theme: &Theme,
    width: u16,
) -> Line<'static> {
    let marker = if selected { "›" } else { " " };
    let is_placeholder = text.is_empty();
    let shown = if is_placeholder {
        OTHER_PLACEHOLDER
    } else {
        text
    };
    let label_style = if is_placeholder {
        theme.dim
    } else if selected {
        theme.approval_border
    } else {
        theme.agent_message
    };
    let mut spans = vec![
        Span::styled(format!("{marker} {}. ", index + 1), theme.dim),
        Span::styled(shown.to_string(), label_style),
    ];

    if selected {
        let current_width = spans
            .iter()
            .map(|span| UnicodeWidthStr::width(span.content.as_ref()))
            .sum::<usize>();
        let arrow_width = 3usize;
        let available = width as usize;
        let pad = available.saturating_sub(current_width + arrow_width).max(1);
        spans.push(Span::raw(" ".repeat(pad)));
        spans.push(Span::styled(
            "↑",
            if can_move_up {
                theme.approval_border
            } else {
                theme.dim
            },
        ));
        spans.push(Span::raw(" "));
        spans.push(Span::styled(
            "↓",
            if can_move_down {
                theme.approval_border
            } else {
                theme.dim
            },
        ));
    }

    Line::from(spans)
}

fn inner_area(area: Rect) -> Rect {
    Rect {
        x: area.x.saturating_add(1),
        y: area.y.saturating_add(1),
        width: area.width.saturating_sub(2),
        height: area.height.saturating_sub(2),
    }
}

fn wrap_text(text: &str, width: u16) -> Vec<String> {
    let width = width.max(1) as usize;
    let mut lines = Vec::new();
    let mut current = String::new();
    let mut current_width = 0usize;
    for ch in text.chars() {
        let ch_width = UnicodeWidthStr::width(ch.to_string().as_str()).max(1);
        if current_width > 0 && current_width + ch_width > width {
            lines.push(current);
            current = String::new();
            current_width = 0;
        }
        current.push(ch);
        current_width += ch_width;
    }
    if current.is_empty() {
        lines.push(String::new());
    } else {
        lines.push(current);
    }
    lines
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::app::state::{UserInputOption, UserInputQuestion};
    use ratatui::buffer::Buffer;

    fn request() -> UserInputRequestState {
        UserInputRequestState {
            request_id: serde_json::json!("req"),
            request_id_text: "req".to_string(),
            questions: vec![UserInputQuestion {
                id: "provider".to_string(),
                header: "Provider".to_string(),
                question: "How should DotCraft handle the provider id?".to_string(),
                is_other: true,
                options: vec![
                    UserInputOption {
                        label: "Auto-generate (Recommended)".to_string(),
                        description: "DotCraft creates it.".to_string(),
                    },
                    UserInputOption {
                        label: "Required".to_string(),
                        description: "Users type it explicitly.".to_string(),
                    },
                ],
            }],
            current_question: 0,
            selected: vec![0],
            other_text: vec![String::new()],
        }
    }

    fn render_request(req: &UserInputRequestState, width: u16) -> String {
        let theme = Theme::dark();
        let area = Rect::new(0, 0, width, UserInputOverlay::preferred_height(req, width));
        let mut buf = Buffer::empty(area);

        UserInputOverlay::new(req, &theme).render(area, &mut buf);

        (area.y..area.y + area.height)
            .map(|y| {
                (0..area.width)
                    .map(|x| buf[(x, y)].symbol())
                    .collect::<String>()
            })
            .collect::<Vec<_>>()
            .join("\n")
    }

    #[test]
    fn preferred_height_fits_composer_zone() {
        let req = request();

        assert!((5..=10).contains(&UserInputOverlay::preferred_height(&req, 80)));
    }

    #[test]
    fn renders_inside_given_composer_area() {
        let req = request();
        let theme = Theme::dark();
        let area = Rect::new(0, 12, 80, UserInputOverlay::preferred_height(&req, 80));
        let mut buf = Buffer::empty(Rect::new(0, 0, 80, 24));

        UserInputOverlay::new(&req, &theme).render(area, &mut buf);

        let rendered = (area.y..area.y + area.height)
            .map(|y| {
                (0..area.width)
                    .map(|x| buf[(x, y)].symbol())
                    .collect::<String>()
            })
            .collect::<Vec<_>>()
            .join("\n");
        assert!(rendered.contains("Asking question"));
        assert!(rendered.contains("Auto-generate"));
        assert!(rendered.contains("How should DotCraft"));
    }

    #[test]
    fn other_row_renders_direct_input_placeholder() {
        let mut req = request();
        req.selected[0] = 2;

        let rendered = render_request(&req, 100);

        assert!(rendered.contains(OTHER_PLACEHOLDER));
        assert!(!rendered.contains("Other..."));
    }

    #[test]
    fn multi_question_footer_shows_question_navigation() {
        let mut req = request();
        req.questions.push(UserInputQuestion {
            id: "next".to_string(),
            header: "Next".to_string(),
            question: "What next?".to_string(),
            is_other: true,
            options: vec![
                UserInputOption {
                    label: "Continue (Recommended)".to_string(),
                    description: "Keep going.".to_string(),
                },
                UserInputOption {
                    label: "Stop".to_string(),
                    description: "Pause.".to_string(),
                },
            ],
        });
        req.selected.push(0);
        req.other_text.push(String::new());

        let rendered = render_request(&req, 120);

        assert!(rendered.contains("Left/Right or h/l question"));
    }
}
