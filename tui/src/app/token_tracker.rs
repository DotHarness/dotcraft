// Cumulative token accounting for the current turn.
// Resets between turns. Driven by item/usage/delta notifications.

#[derive(Debug, Clone, Default)]
pub struct TokenTracker {
    pub input_tokens: i64,
    pub output_tokens: i64,
}

impl TokenTracker {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn add(&mut self, input_delta: i64, output_delta: i64) {
        self.input_tokens += input_delta;
        self.output_tokens += output_delta;
    }

    pub fn reset(&mut self) {
        self.input_tokens = 0;
        self.output_tokens = 0;
    }

    /// Format as a compact status string, e.g. "↑1.2k ↓350".
    /// Format as a compact status string, e.g. "↑1.2k ↓350".
    /// Returns empty string when both counts are zero (omitted per spec).
    pub fn format_compact(&self) -> String {
        if self.input_tokens == 0 && self.output_tokens == 0 {
            return String::new();
        }
        format!(
            "↑{} ↓{}",
            format_token_count(self.input_tokens),
            format_token_count(self.output_tokens),
        )
    }
}

pub fn format_token_count(n: i64) -> String {
    if n >= 1_000_000 {
        format!("{:.1}M", n as f64 / 1_000_000.0)
    } else if n >= 1_000 {
        format!("{:.1}k", n as f64 / 1_000.0)
    } else {
        n.to_string()
    }
}
