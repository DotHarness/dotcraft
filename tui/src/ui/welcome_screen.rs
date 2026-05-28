// WelcomeScreen widget (§8.1 of specs/clients/tui-client.md).
// Compact startup/status surface shown while the Wire Protocol handshake runs.

use crate::{i18n::Strings, theme::Theme};
use ratatui::{
    buffer::Buffer,
    layout::Rect,
    style::Modifier,
    text::{Line, Span},
    widgets::{Paragraph, Widget, Wrap},
};
use unicode_width::{UnicodeWidthChar, UnicodeWidthStr};

const SESSION_HEADER_MAX_INNER_WIDTH: usize = 68;

/// Braille spinner frames.
const SPINNER: &[char] = &['⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏'];

pub struct WelcomeScreen<'a> {
    pub version: &'a str,
    pub workspace: &'a str,
    pub current_model: Option<&'a str>,
    pub current_thread: Option<&'a str>,
    pub connected: bool,
    pub connection_error: Option<&'a str>,
    pub tick_count: u64,
    pub theme: &'a Theme,
    pub strings: &'a Strings,
}

impl<'a> WelcomeScreen<'a> {
    pub fn new(
        version: &'a str,
        workspace: &'a str,
        current_model: Option<&'a str>,
        current_thread: Option<&'a str>,
        connected: bool,
        connection_error: Option<&'a str>,
        tick_count: u64,
        theme: &'a Theme,
        strings: &'a Strings,
    ) -> Self {
        Self {
            version,
            workspace,
            current_model,
            current_thread,
            connected,
            connection_error,
            tick_count,
            theme,
            strings,
        }
    }
}

impl Widget for WelcomeScreen<'_> {
    fn render(self, area: Rect, buf: &mut Buffer) {
        let mut lines: Vec<Line<'static>> = Vec::new();
        lines.push(Line::default());
        lines.extend(session_header_lines(
            self.version,
            self.workspace,
            self.current_model,
            self.current_thread,
            self.connected,
            self.connection_error,
            self.tick_count,
            self.theme,
            self.strings,
            area.width,
        ));
        lines.push(Line::default());
        lines.push(tip_line(self.strings, self.theme));

        Paragraph::new(lines)
            .wrap(Wrap { trim: false })
            .render(area, buf);
    }
}

pub fn session_header_lines(
    version: &str,
    workspace: &str,
    current_model: Option<&str>,
    current_thread: Option<&str>,
    connected: bool,
    connection_error: Option<&str>,
    tick_count: u64,
    theme: &Theme,
    strings: &Strings,
    width: u16,
) -> Vec<Line<'static>> {
    if width < 16 {
        return Vec::new();
    }

    let content_width = width
        .saturating_sub(6)
        .max(10)
        .min(SESSION_HEADER_MAX_INNER_WIDTH as u16) as usize;
    let border = "─".repeat(content_width + 2);

    let labels = [
        strings.welcome_model_label,
        strings.welcome_workspace_label,
        strings.welcome_thread_label,
        strings.welcome_status_label,
    ];
    let label_width = labels
        .iter()
        .map(|label| format!("{label}:").width())
        .max()
        .unwrap_or(0);

    let mut lines = Vec::new();
    lines.push(Line::from(Span::styled(format!("  ╭{border}╮"), theme.dim)));

    lines.push(card_line(
        vec![
            Span::styled(">_ ", theme.dim),
            Span::styled(
                "DotCraft".to_string(),
                theme.welcome_brand.add_modifier(Modifier::BOLD),
            ),
            Span::styled(" ".to_string(), theme.dim),
            Span::styled(format!("(v{version})"), theme.dim),
        ],
        content_width,
        theme,
    ));
    lines.push(card_line(Vec::new(), content_width, theme));

    let model = current_model.unwrap_or(strings.model_default_label);
    lines.push(model_line(
        strings.welcome_model_label,
        model,
        label_width,
        content_width,
        theme,
        strings,
    ));

    let workspace_display = if workspace.is_empty() {
        "(none)"
    } else {
        workspace
    };
    lines.push(value_line(
        strings.welcome_workspace_label,
        workspace_display,
        label_width,
        content_width,
        theme,
        theme.agent_message,
    ));

    if let Some(thread) = current_thread.filter(|thread| !thread.is_empty()) {
        lines.push(value_line(
            strings.welcome_thread_label,
            thread,
            label_width,
            content_width,
            theme,
            theme.agent_message,
        ));
    }

    let (status, status_style) =
        status_text(connected, connection_error, tick_count, theme, strings);
    lines.push(value_line(
        strings.welcome_status_label,
        &status,
        label_width,
        content_width,
        theme,
        status_style,
    ));

    lines.push(Line::from(Span::styled(format!("  ╰{border}╯"), theme.dim)));
    lines
}

pub fn tip_line(strings: &Strings, theme: &Theme) -> Line<'static> {
    Line::from(vec![
        Span::raw("  "),
        Span::styled(strings.welcome_tip_prefix.to_string(), theme.agent_message),
        Span::raw(" "),
        Span::styled(strings.welcome_hint_start.to_string(), theme.dim),
        Span::styled(" · ".to_string(), theme.dim),
        Span::styled(strings.welcome_hint_commands.to_string(), theme.dim),
    ])
}

fn model_line(
    label: &str,
    model: &str,
    label_width: usize,
    content_width: usize,
    theme: &Theme,
    strings: &Strings,
) -> Line<'static> {
    let hint_text = format!("   /model {}", strings.welcome_model_change_hint);
    let label_text = padded_label(label, label_width);
    let label_and_gap = label_text.width() + 1;
    let hint_width = hint_text.width();
    let model_width = content_width.saturating_sub(label_and_gap + hint_width);
    let model_text = truncate_display_width(model, model_width.max(8));

    let mut spans = vec![
        Span::styled(label_text, theme.dim),
        Span::raw(" "),
        Span::styled(model_text, theme.agent_message),
    ];
    if line_width(&spans) + hint_width <= content_width {
        spans.push(Span::styled("   ".to_string(), theme.dim));
        spans.push(Span::styled("/model".to_string(), theme.input_border_plan));
        spans.push(Span::styled(
            format!(" {}", strings.welcome_model_change_hint),
            theme.dim,
        ));
    }

    card_line(spans, content_width, theme)
}

fn value_line(
    label: &str,
    value: &str,
    label_width: usize,
    content_width: usize,
    theme: &Theme,
    value_style: ratatui::style::Style,
) -> Line<'static> {
    let label_text = padded_label(label, label_width);
    let value_width = content_width.saturating_sub(label_text.width() + 1);
    card_line(
        vec![
            Span::styled(label_text, theme.dim),
            Span::raw(" "),
            Span::styled(truncate_display_width(value, value_width), value_style),
        ],
        content_width,
        theme,
    )
}

fn status_text(
    connected: bool,
    connection_error: Option<&str>,
    tick_count: u64,
    theme: &Theme,
    strings: &Strings,
) -> (String, ratatui::style::Style) {
    if let Some(error) = connection_error {
        let detail = error
            .strip_prefix("Connection failed: ")
            .unwrap_or(error)
            .trim();
        (
            format!("{}: {detail}", strings.welcome_connection_failed),
            theme.error,
        )
    } else if connected {
        (strings.connected.to_string(), theme.success)
    } else {
        let frame = SPINNER[tick_count as usize % SPINNER.len()];
        (format!("{frame} {}", strings.welcome_connecting), theme.dim)
    }
}

fn card_line(mut spans: Vec<Span<'static>>, content_width: usize, theme: &Theme) -> Line<'static> {
    let used_width = line_width(&spans);
    if used_width < content_width {
        spans.push(Span::styled(
            " ".repeat(content_width - used_width),
            theme.dim,
        ));
    }

    let mut out = Vec::with_capacity(spans.len() + 2);
    out.push(Span::styled("  │ ".to_string(), theme.dim));
    out.extend(spans);
    out.push(Span::styled(" │".to_string(), theme.dim));
    Line::from(out)
}

fn padded_label(label: &str, label_width: usize) -> String {
    let text = format!("{label}:");
    let padding = label_width.saturating_sub(text.width());
    format!("{text}{}", " ".repeat(padding))
}

fn line_width(spans: &[Span<'static>]) -> usize {
    spans.iter().map(|span| span.content.as_ref().width()).sum()
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
