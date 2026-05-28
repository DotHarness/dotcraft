// ApprovalOverlay widget (§10 of specs/clients/tui-client.md).
// Renders a centered modal that blocks all input until the user makes a decision.

use crate::{app::state::ApprovalState, i18n::Strings, theme::Theme};
use ratatui::{
    buffer::Buffer,
    layout::{Alignment, Rect},
    text::{Line, Span},
    widgets::{Block, Borders, Clear, Paragraph, Widget},
};

/// The 5 possible decisions in order (matches `selected` index in ApprovalState).
pub const DECISIONS: [&str; 5] = [
    "accept",
    "acceptForSession",
    "acceptAlways",
    "decline",
    "cancel",
];

pub struct ApprovalOverlay<'a> {
    pub approval: &'a ApprovalState,
    pub theme: &'a Theme,
    pub strings: &'a Strings,
}

impl<'a> ApprovalOverlay<'a> {
    pub fn new(approval: &'a ApprovalState, theme: &'a Theme, strings: &'a Strings) -> Self {
        Self {
            approval,
            theme,
            strings,
        }
    }

    /// Centered popup area: 60% width, up to 22 rows, vertically centered.
    pub fn popup_area(full: Rect) -> Rect {
        let popup_width = (full.width * 60 / 100).max(50).min(full.width);
        let popup_height = 22u16.min(full.height.saturating_sub(2));
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

impl Widget for ApprovalOverlay<'_> {
    fn render(self, area: Rect, buf: &mut Buffer) {
        let popup = Self::popup_area(area);

        // Clear the area behind the modal.
        Clear.render(popup, buf);

        let block = Block::default()
            .borders(Borders::ALL)
            .border_style(self.theme.approval_border)
            .title(Line::from(Span::styled(
                format!(" {} ", self.strings.approval_title),
                self.theme.approval_border,
            )));

        // Build content lines.
        let inner = block.inner(popup);
        block.render(popup, buf);

        // Type icon + label
        let (icon, type_label) = match self.approval.approval_type.as_str() {
            "file" => ("📄", self.strings.approval_file),
            _ => ("🔧", self.strings.approval_shell),
        };
        let mut lines: Vec<Line> = vec![
            Line::from(Span::styled(
                format!("{icon}  {type_label}"),
                self.theme.tool_active,
            )),
            Line::default(),
        ];

        // Operation / target / reason details
        let op_label = if self.approval.approval_type == "file" {
            self.strings.approval_operation_label
        } else {
            self.strings.approval_operation_label
        };
        lines.push(Line::from(vec![
            Span::styled(format!("  {op_label}: "), self.theme.dim),
            Span::raw(self.approval.operation.clone()),
        ]));
        if !self.approval.target.is_empty() {
            lines.push(Line::from(vec![
                Span::styled(
                    format!("  {}: ", self.strings.approval_target_label),
                    self.theme.dim,
                ),
                Span::raw(self.approval.target.clone()),
            ]));
        }
        if let Some(reason) = &self.approval.reason {
            if !reason.is_empty() {
                lines.push(Line::from(vec![
                    Span::styled(
                        format!("  {}: ", self.strings.approval_reason_label),
                        self.theme.dim,
                    ),
                    Span::raw(reason.clone()),
                ]));
            }
        }
        lines.push(Line::default());

        // Decision options
        let options: &[(&str, &str, &str)] = &[
            ("[a]", self.strings.approval_accept, "accept"),
            (
                "[s]",
                self.strings.approval_accept_session,
                "acceptForSession",
            ),
            ("[!]", self.strings.approval_accept_always, "acceptAlways"),
            ("[d]", self.strings.approval_decline, "decline"),
            ("[c]", self.strings.approval_cancel, "cancel"),
        ];

        for (i, (key, label, _decision)) in options.iter().enumerate() {
            let is_selected = i == self.approval.selected;
            let prefix = if is_selected { "► " } else { "  " };
            let line_style = if is_selected {
                self.theme.approval_border
            } else {
                self.theme.agent_message
            };
            let key_style = self.theme.dim;
            lines.push(Line::from(vec![
                Span::raw(prefix),
                Span::styled(key.to_string(), key_style),
                Span::raw(" "),
                Span::styled(label.to_string(), line_style),
            ]));
        }

        lines.push(Line::default());
        lines.push(Line::from(Span::styled(
            "  ↑/↓ to select  Enter to confirm",
            self.theme.dim,
        )));

        Paragraph::new(lines)
            .alignment(Alignment::Left)
            .render(inner, buf);
    }
}
