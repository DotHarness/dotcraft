// NotificationToast widget for system/jobResult notifications.
// Non-modal: renders in the top-right corner, does not capture input.
// Auto-dismissed by expire_notifications() in lib.rs after 10 seconds.

use crate::{app::state::AppState, i18n::Strings, theme::Theme};
use ratatui::{
    buffer::Buffer,
    layout::Rect,
    text::{Line, Span},
    widgets::{Block, Borders, Clear, Paragraph, Widget},
};

pub struct NotificationToast<'a> {
    state: &'a AppState,
    theme: &'a Theme,
    strings: &'a Strings,
}

impl<'a> NotificationToast<'a> {
    pub fn new(state: &'a AppState, theme: &'a Theme, strings: &'a Strings) -> Self {
        Self {
            state,
            theme,
            strings,
        }
    }

    /// Top-right corner area: width 42, height = notifications + 2 (borders).
    pub fn toast_area(full: Rect, count: usize) -> Rect {
        let w = 42u16.min(full.width);
        let h = (count as u16 + 2).min(full.height / 3).max(3);
        let x = full.x + full.width.saturating_sub(w);
        let y = full.y + 1; // below the status bar
        Rect {
            x,
            y,
            width: w,
            height: h,
        }
    }
}

impl Widget for NotificationToast<'_> {
    fn render(self, area: Rect, buf: &mut Buffer) {
        let count = self.state.notifications.len();
        if count == 0 {
            return;
        }

        let toast = Self::toast_area(area, count);
        Clear.render(toast, buf);

        let block = Block::default()
            .borders(Borders::ALL)
            .border_style(self.theme.dim)
            .title(Span::styled(
                format!(" {} ", self.strings.notification_job_result),
                self.theme.dim,
            ));

        let inner = block.inner(toast);
        block.render(toast, buf);

        let lines: Vec<Line> = self
            .state
            .notifications
            .iter()
            .map(|n| {
                let label = n.job_name.as_deref().unwrap_or("job");
                if let Some(err) = &n.error {
                    Line::from(vec![
                        Span::styled(format!("  ✗ {label}: "), self.theme.error),
                        Span::styled(truncate(err, 28), self.theme.error),
                    ])
                } else {
                    let result = n
                        .result
                        .as_deref()
                        .unwrap_or(self.strings.notification_success);
                    Line::from(vec![
                        Span::styled(format!("  ✓ {label}: "), self.theme.tool_completed),
                        Span::styled(truncate(result, 28), self.theme.dim),
                    ])
                }
            })
            .collect();

        Paragraph::new(lines).render(inner, buf);
    }
}

fn truncate(s: &str, max: usize) -> String {
    let chars: Vec<char> = s.chars().collect();
    if chars.len() > max {
        let mut out: String = chars[..max.saturating_sub(1)].iter().collect();
        out.push('…');
        out
    } else {
        s.to_string()
    }
}
