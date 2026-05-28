// Markdown-to-styled-terminal-text renderer.
// Uses pulldown-cmark for parsing and syntect (via ui::highlight) for code blocks.
// Supports full table rendering with Unicode box-drawing characters.

use crate::{theme::Theme, ui::highlight};
use pulldown_cmark::{Alignment, Event, HeadingLevel, Options, Parser, Tag, TagEnd};
use ratatui::{
    style::{Modifier, Style},
    text::{Line, Span},
};
use unicode_width::{UnicodeWidthChar, UnicodeWidthStr};

/// Render a markdown string into a list of styled ratatui Lines.
/// `width` controls word-wrap and table layout. `theme` provides all styles.
pub fn render(input: &str, width: u16, theme: &Theme) -> Vec<Line<'static>> {
    let mut writer = MarkdownWriter::new(width, theme);
    let options = Options::all();
    // into_static() clones all borrowed CowStr slices into owned Strings so
    // the events are no longer tied to the lifetime of `input`.
    let parser = Parser::new_ext(input, options).map(|e| e.into_static());
    writer.process(parser);
    writer.finish()
}

/// Render a short markdown snippet inline (no block structure) and return styled Spans.
/// Used for single-line contexts like notification messages.
pub fn render_inline(input: &str, theme: &Theme) -> Vec<Span<'static>> {
    let mut spans = Vec::new();
    let parser = Parser::new(input).map(|e| e.into_static());
    for event in parser {
        match event {
            Event::Text(text) => spans.push(Span::raw(text.to_string())),
            Event::Code(code) => {
                spans.push(Span::styled(code.to_string(), theme.inline_code));
            }
            Event::SoftBreak | Event::HardBreak => spans.push(Span::raw(" ")),
            _ => {}
        }
    }
    spans
}

// ── MarkdownWriter ────────────────────────────────────────────────────────

struct MarkdownWriter<'t> {
    width: u16,
    theme: &'t Theme,
    lines: Vec<Line<'static>>,
    // Current line being assembled
    current_spans: Vec<Span<'static>>,
    // Current text style stack
    emphasis: bool,
    strong: bool,
    strikethrough: bool,
    // Code blocks
    in_code_block: bool,
    code_lang: String,
    code_buffer: String,
    // Lists
    list_stack: Vec<ListState>,
    // Blockquotes
    blockquote_depth: u32,
    // Headings
    in_heading: Option<HeadingLevel>,
    // Tables
    in_table: bool,
    table_alignments: Vec<Alignment>,
    table_rows: Vec<Vec<Vec<Span<'static>>>>,
    current_table_row: Vec<Vec<Span<'static>>>,
    current_cell_spans: Vec<Span<'static>>,
    table_header_done: bool,
    // Links
    in_link: bool,
    // Task list
    task_item_prefix: Option<&'static str>,
}

#[derive(Clone)]
struct ListState {
    ordered: bool,
    item_index: u64,
    indent: u16,
}

impl<'t> MarkdownWriter<'t> {
    fn new(width: u16, theme: &'t Theme) -> Self {
        Self {
            width,
            theme,
            lines: Vec::new(),
            current_spans: Vec::new(),
            emphasis: false,
            strong: false,
            strikethrough: false,
            in_code_block: false,
            code_lang: String::new(),
            code_buffer: String::new(),
            list_stack: Vec::new(),
            blockquote_depth: 0,
            in_heading: None,
            in_table: false,
            table_alignments: Vec::new(),
            table_rows: Vec::new(),
            current_table_row: Vec::new(),
            current_cell_spans: Vec::new(),
            table_header_done: false,
            in_link: false,
            task_item_prefix: None,
        }
    }

    fn process(&mut self, parser: impl Iterator<Item = Event<'static>>) {
        for event in parser {
            self.handle_event(event);
        }
    }

    fn finish(self) -> Vec<Line<'static>> {
        let mut out = self.lines;
        // Flush any trailing partial line.
        if !self.current_spans.is_empty() {
            out.push(Line::from(self.current_spans));
        }
        out
    }

    // ── Event dispatch ────────────────────────────────────────────────────

    fn handle_event(&mut self, event: Event<'static>) {
        match event {
            Event::Start(tag) => self.handle_start(tag),
            Event::End(tag) => self.handle_end(tag),
            Event::Text(text) => self.handle_text(text.into_string()),
            Event::Code(code) => self.handle_inline_code(code.into_string()),
            Event::SoftBreak => {
                if self.in_code_block || self.in_table {
                    return;
                }
                self.push_span(Span::raw(" "));
            }
            Event::HardBreak => {
                if !self.in_code_block {
                    self.flush_line();
                }
            }
            Event::Rule => self.render_rule(),
            Event::TaskListMarker(checked) => {
                self.task_item_prefix = Some(if checked { "☑ " } else { "☐ " });
            }
            _ => {}
        }
    }

    fn handle_start(&mut self, tag: Tag<'static>) {
        match tag {
            Tag::Paragraph => {}
            Tag::Heading { level, .. } => {
                self.in_heading = Some(level);
            }
            Tag::BlockQuote(_) => {
                self.blockquote_depth += 1;
            }
            Tag::CodeBlock(kind) => {
                self.in_code_block = true;
                self.code_lang = match kind {
                    pulldown_cmark::CodeBlockKind::Fenced(lang) => {
                        // Strip attributes like `rust,no_run` or `rust title="x"`.
                        lang.split(|c: char| c == ',' || c == ' ')
                            .next()
                            .unwrap_or("")
                            .trim()
                            .to_string()
                    }
                    pulldown_cmark::CodeBlockKind::Indented => String::new(),
                };
                self.code_buffer.clear();
            }
            Tag::List(start_index) => {
                let indent = (self.list_stack.len() as u16) * 2;
                self.list_stack.push(ListState {
                    ordered: start_index.is_some(),
                    item_index: start_index.unwrap_or(1),
                    indent,
                });
            }
            Tag::Item => {
                // Will render prefix on first text event.
            }
            Tag::Table(alignments) => {
                self.in_table = true;
                self.table_alignments = alignments;
                self.table_rows.clear();
                self.table_header_done = false;
            }
            Tag::TableHead => {}
            Tag::TableRow => {
                self.current_table_row.clear();
            }
            Tag::TableCell => {
                self.current_cell_spans.clear();
            }
            Tag::Emphasis => {
                self.emphasis = true;
            }
            Tag::Strong => {
                self.strong = true;
            }
            Tag::Strikethrough => {
                self.strikethrough = true;
            }
            Tag::Link { .. } => {
                self.in_link = true;
            }
            Tag::Image { .. } => {
                // Phase 1: show alt text.
            }
            _ => {}
        }
    }

    fn handle_end(&mut self, tag: TagEnd) {
        match tag {
            TagEnd::Paragraph => {
                self.flush_line();
                self.push_blank();
            }
            TagEnd::Heading(_) => {
                self.flush_line();
                self.push_blank();
                self.in_heading = None;
            }
            TagEnd::BlockQuote(_) => {
                if self.blockquote_depth > 0 {
                    self.blockquote_depth -= 1;
                }
                self.push_blank();
            }
            TagEnd::CodeBlock => {
                self.render_code_block();
                self.in_code_block = false;
                self.push_blank();
            }
            TagEnd::List(_) => {
                self.list_stack.pop();
                if self.list_stack.is_empty() {
                    self.push_blank();
                }
            }
            TagEnd::Item => {
                self.flush_line();
                self.task_item_prefix = None;
            }
            TagEnd::Table => {
                self.render_table();
                self.in_table = false;
                self.push_blank();
            }
            TagEnd::TableHead => {
                self.table_header_done = true;
            }
            TagEnd::TableRow => {
                self.table_rows.push(self.current_table_row.clone());
                self.current_table_row.clear();
            }
            TagEnd::TableCell => {
                self.current_table_row.push(self.current_cell_spans.clone());
                self.current_cell_spans.clear();
            }
            TagEnd::Emphasis => {
                self.emphasis = false;
            }
            TagEnd::Strong => {
                self.strong = false;
            }
            TagEnd::Strikethrough => {
                self.strikethrough = false;
            }
            TagEnd::Link => {
                self.in_link = false;
            }
            _ => {}
        }
    }

    fn handle_text(&mut self, text: String) {
        if self.in_code_block {
            self.code_buffer.push_str(&text);
            return;
        }

        if self.in_table {
            let style = self.current_style();
            self.current_cell_spans.push(Span::styled(text, style));
            return;
        }

        // List item prefix
        if let Some(prefix) = self.task_item_prefix.take() {
            let indent = self.list_indent();
            let style = self.theme.dim;
            self.push_span(Span::raw(" ".repeat(indent as usize)));
            self.push_span(Span::styled(prefix, style));
        } else if !self.list_stack.is_empty() && self.current_spans.is_empty() {
            let (prefix, indent) = self.list_prefix();
            self.push_span(Span::raw(" ".repeat(indent as usize)));
            self.push_span(Span::styled(prefix, self.theme.dim));
        }

        // Apply word wrapping for long paragraphs (not headings/blockquotes).
        let style = self.current_style();
        let available = self
            .width
            .saturating_sub(self.blockquote_depth as u16 * 2)
            .saturating_sub(self.list_indent());
        let available = if available < 20 { 20 } else { available };

        if self.in_heading.is_some() || available == 0 {
            self.push_span(Span::styled(text, style));
            return;
        }

        // Word-wrap: split into segments respecting the available width.
        // Use display width so CJK characters (2 cols each) wrap correctly.
        let words: Vec<&str> = text.split_whitespace().collect();
        for word in words {
            let current_len: usize = self.current_spans.iter().map(|s| s.content.width()).sum();
            let word_len = word.width();
            if current_len > 0 && current_len + 1 + word_len > available as usize {
                self.flush_line_with_prefix();
            } else if current_len > 0 {
                self.push_span(Span::raw(" "));
            }
            self.push_span(Span::styled(word.to_string(), style));
        }
    }

    fn handle_inline_code(&mut self, code: String) {
        if self.in_table {
            self.current_cell_spans
                .push(Span::styled(code, self.theme.inline_code));
            return;
        }
        self.push_span(Span::styled(code, self.theme.inline_code));
    }

    // ── Render helpers ────────────────────────────────────────────────────

    fn current_style(&self) -> Style {
        let base = if let Some(level) = self.in_heading {
            match level {
                HeadingLevel::H1 => self.theme.heading1,
                HeadingLevel::H2 => self.theme.heading2,
                HeadingLevel::H3 => self.theme.heading3,
                _ => self.theme.agent_message.add_modifier(Modifier::BOLD),
            }
        } else if self.blockquote_depth > 0 {
            self.theme.blockquote_text
        } else {
            self.theme.agent_message
        };

        let mut s = base;
        if self.emphasis {
            s = s.add_modifier(Modifier::ITALIC);
        }
        if self.strong {
            s = s.add_modifier(Modifier::BOLD);
        }
        if self.strikethrough {
            s = s.add_modifier(Modifier::CROSSED_OUT);
        }
        if self.in_link {
            s = s.patch(self.theme.link);
        }
        s
    }

    fn push_span(&mut self, span: Span<'static>) {
        self.current_spans.push(span);
    }

    fn flush_line(&mut self) {
        let spans = std::mem::take(&mut self.current_spans);
        let prefix = self.line_prefix();
        if prefix.is_empty() {
            self.lines.push(Line::from(spans));
        } else {
            let mut all = vec![Span::styled(prefix, self.theme.blockquote_border)];
            all.extend(spans);
            self.lines.push(Line::from(all));
        }
    }

    // Same as flush_line but also re-emits list/blockquote prefix for continuation lines.
    fn flush_line_with_prefix(&mut self) {
        self.flush_line();
        // Re-emit indent for continuation of wrapped list items.
        if !self.list_stack.is_empty() {
            let indent = self.list_indent();
            self.push_span(Span::raw(" ".repeat(indent as usize + 2)));
        }
    }

    fn push_blank(&mut self) {
        if !self.current_spans.is_empty() {
            self.flush_line();
        }
        // Avoid consecutive blank lines.
        if self
            .lines
            .last()
            .map(|l| l.spans.is_empty())
            .unwrap_or(false)
        {
            return;
        }
        self.lines.push(Line::default());
    }

    fn line_prefix(&self) -> String {
        if self.blockquote_depth > 0 {
            "│ ".repeat(self.blockquote_depth as usize)
        } else {
            String::new()
        }
    }

    fn list_indent(&self) -> u16 {
        self.list_stack.last().map(|s| s.indent).unwrap_or(0)
    }

    fn list_prefix(&mut self) -> (String, u16) {
        if let Some(state) = self.list_stack.last_mut() {
            let indent = state.indent;
            let prefix = if state.ordered {
                let idx = state.item_index;
                state.item_index += 1;
                format!("{idx}. ")
            } else {
                "• ".to_string()
            };
            (prefix, indent)
        } else {
            (String::new(), 0)
        }
    }

    fn render_rule(&mut self) {
        if !self.current_spans.is_empty() {
            self.flush_line();
        }
        let w = self.width.saturating_sub(2) as usize;
        self.lines
            .push(Line::from(Span::styled("─".repeat(w), self.theme.dim)));
        self.push_blank();
    }

    fn render_code_block(&mut self) {
        let code = std::mem::take(&mut self.code_buffer);
        let lang = self.code_lang.clone();

        // Top border
        let label = if lang.is_empty() {
            String::new()
        } else {
            format!(" {lang} ")
        };
        let top_width = self.width.saturating_sub(2) as usize;
        let top_line = if label.is_empty() {
            format!("┌{}┐", "─".repeat(top_width))
        } else {
            // Use display width for label (language names are ASCII so this is identical,
            // but consistent with the rest of the width logic).
            let dashes = top_width.saturating_sub(label.width());
            let left = dashes / 2;
            let right = dashes - left;
            format!("┌{}{}{}┐", "─".repeat(left), label, "─".repeat(right))
        };
        self.lines.push(Line::from(Span::styled(
            top_line,
            self.theme.code_block_border,
        )));

        // Highlighted code lines
        let highlighted = if lang.is_empty() {
            code.lines()
                .map(|l| Line::from(l.to_string()))
                .collect::<Vec<_>>()
        } else {
            highlight::highlight_code(&code, &lang, self.theme)
        };

        for line in highlighted {
            let mut spans = vec![Span::styled("│ ", self.theme.code_block_border)];
            spans.extend(line.spans);
            self.lines.push(Line::from(spans));
        }

        // Bottom border
        self.lines.push(Line::from(Span::styled(
            format!("└{}┘", "─".repeat(top_width)),
            self.theme.code_block_border,
        )));
    }

    // ── Table rendering ───────────────────────────────────────────────────

    fn render_table(&mut self) {
        if self.table_rows.is_empty() {
            return;
        }

        let cols = self.table_rows[0].len();
        if cols == 0 {
            return;
        }

        // Compute max content display-width per column (CJK chars = 2 cols).
        let mut col_widths: Vec<usize> = vec![1; cols];
        for row in &self.table_rows {
            for (i, cell) in row.iter().enumerate() {
                let w: usize = cell.iter().map(|s| s.content.width()).sum();
                if i < col_widths.len() && w > col_widths[i] {
                    col_widths[i] = w;
                }
            }
        }

        // Clamp widths to fit terminal.
        let available = self.width.saturating_sub(cols as u16 + 1) as usize;
        if available > 0 {
            let total: usize = col_widths.iter().sum();
            if total > available {
                let scale = available as f64 / total as f64;
                for w in &mut col_widths {
                    *w = ((*w as f64 * scale).floor() as usize).max(1);
                }
            }
        }

        let border = self.theme.table_border;
        let header_style = self.theme.table_header;

        // Top border: ┌─────┬─────┐
        let top = build_table_border('┌', '─', '┬', '┐', &col_widths);
        self.lines.push(Line::from(Span::styled(top, border)));

        for (row_idx, row) in self.table_rows.iter().enumerate() {
            // Cell row
            let mut spans: Vec<Span<'static>> = vec![Span::styled("│", border)];
            for (col_idx, cell) in row.iter().enumerate() {
                let w = col_widths.get(col_idx).copied().unwrap_or(1);
                let content: String = cell.iter().map(|s| s.content.as_ref()).collect();
                let content = truncate_or_pad(&content, w);
                let style = if row_idx == 0 {
                    header_style
                } else {
                    Style::default()
                };
                spans.push(Span::styled(format!(" {content} "), style));
                spans.push(Span::styled("│", border));
            }
            self.lines.push(Line::from(spans));

            // Separator after header row
            if row_idx == 0 {
                let sep = build_table_border('├', '─', '┼', '┤', &col_widths);
                self.lines.push(Line::from(Span::styled(sep, border)));
            }
        }

        // Bottom border: └─────┴─────┘
        let bottom = build_table_border('└', '─', '┴', '┘', &col_widths);
        self.lines.push(Line::from(Span::styled(bottom, border)));
    }
}

// ── Render entry point (owned input) ─────────────────────────────────────

/// Render a markdown string from an owned `String` (avoids the `Box::leak` approach).
/// Render from an owned String. Uses the same pipeline as `render()`.
pub fn render_owned(input: String, width: u16, theme: &Theme) -> Vec<Line<'static>> {
    render(&input, width, theme)
}

// ── Table builder helpers ─────────────────────────────────────────────────

fn build_table_border(
    left: char,
    fill: char,
    sep: char,
    right: char,
    col_widths: &[usize],
) -> String {
    let mut s = String::new();
    s.push(left);
    for (i, &w) in col_widths.iter().enumerate() {
        // +2 for the surrounding spaces in cell content
        s.push_str(&fill.to_string().repeat(w + 2));
        if i + 1 < col_widths.len() {
            s.push(sep);
        }
    }
    s.push(right);
    s
}

/// Truncate or right-pad `s` to exactly `width` display columns.
/// Uses `UnicodeWidthChar` so CJK characters (2 cols) are measured correctly.
fn truncate_or_pad(s: &str, width: usize) -> String {
    let display_width = s.width();
    if display_width > width {
        // Truncate by display width, appending '…'.
        let mut out = String::new();
        let mut w: usize = 0;
        for c in s.chars() {
            let cw = UnicodeWidthChar::width(c).unwrap_or(0);
            if w + cw > width.saturating_sub(1) {
                break;
            }
            out.push(c);
            w += cw;
        }
        out.push('…');
        out
    } else {
        let mut out = s.to_string();
        let mut w = display_width;
        while w < width {
            out.push(' ');
            w += 1;
        }
        out
    }
}
