// Streaming markdown collector.
// Implements incremental rendering of agent message deltas:
//   - Lines are committed only when a newline completes them.
//   - The partial (incomplete) last line is rendered dimmed as a "typing"
//     indicator so the user sees characters as they arrive.

use crate::{theme::Theme, ui::markdown};
use ratatui::{
    style::Modifier,
    text::{Line, Span},
};

pub struct StreamCollector {
    /// Accumulated raw markdown text.
    buffer: String,
    /// Byte offset in `buffer` up to which lines have been committed.
    committed_byte_offset: usize,
    /// Number of rendered lines already committed (for diff tracking).
    committed_line_count: usize,
    /// Render width passed to the markdown renderer.
    width: u16,
    /// Theme snapshot used for rendering.
    theme: Theme,
}

impl StreamCollector {
    pub fn new(width: u16, theme: Theme) -> Self {
        Self {
            buffer: String::new(),
            committed_byte_offset: 0,
            committed_line_count: 0,
            width,
            theme,
        }
    }

    /// Append a new delta from `item/agentMessage/delta`.
    pub fn push_delta(&mut self, delta: &str) {
        self.buffer.push_str(delta);
    }

    /// Render the buffer up to the last newline and return only the lines
    /// produced since the previous commit (i.e., newly completed lines).
    ///
    /// Lines without a trailing newline are not committed — they become the
    /// partial line returned by `partial_line()`.
    pub fn commit_complete_lines(&mut self) -> Vec<Line<'static>> {
        // Find the last newline in the buffer.
        let last_nl = match self.buffer[self.committed_byte_offset..].rfind('\n') {
            Some(rel) => self.committed_byte_offset + rel + 1, // include the '\n'
            None => return Vec::new(),                         // nothing to commit yet
        };

        // Render the committed portion.
        let committed_text = &self.buffer[..last_nl];
        let all_lines = markdown::render_owned(committed_text.to_string(), self.width, &self.theme);

        // Return only newly rendered lines.
        let new_lines: Vec<Line<'static>> = all_lines
            .into_iter()
            .skip(self.committed_line_count)
            .collect();

        self.committed_byte_offset = last_nl;
        self.committed_line_count += new_lines.len();

        new_lines
    }

    /// Render the text after the last committed newline as a dimmed partial line.
    /// Returns `None` if there is nothing past the last commit point.
    pub fn partial_line(&self) -> Option<Line<'static>> {
        let tail = &self.buffer[self.committed_byte_offset..];
        if tail.is_empty() {
            return None;
        }
        // Show the raw tail dimmed, with a blinking cursor indicator.
        let display = format!("{tail}▍");
        Some(Line::from(Span::styled(
            display,
            self.theme.agent_message.add_modifier(Modifier::DIM),
        )))
    }

    /// Force-commit everything remaining (including the partial line).
    /// Returns all lines not yet committed, then resets the collector.
    pub fn finalize(&mut self) -> Vec<Line<'static>> {
        if self.buffer.is_empty() {
            return Vec::new();
        }
        // Ensure the buffer ends with a newline so the last line is rendered.
        if !self.buffer.ends_with('\n') {
            self.buffer.push('\n');
        }

        let all_lines = markdown::render_owned(self.buffer.clone(), self.width, &self.theme);
        let new_lines: Vec<Line<'static>> = all_lines
            .into_iter()
            .skip(self.committed_line_count)
            .collect();

        self.buffer.clear();
        self.committed_byte_offset = 0;
        self.committed_line_count = 0;

        new_lines
    }

    /// Returns the full raw text accumulated so far (for history storage).
    pub fn full_text(&self) -> &str {
        &self.buffer
    }

    /// Update the render width (e.g. after a terminal resize).
    pub fn set_width(&mut self, width: u16) {
        self.width = width;
    }

    /// Reset to empty state without rendering.
    pub fn clear(&mut self) {
        self.buffer.clear();
        self.committed_byte_offset = 0;
        self.committed_line_count = 0;
    }
}
