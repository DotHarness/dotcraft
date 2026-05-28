// Syntax highlighting via syntect.
// Converts fenced code block text into styled ratatui Lines.

use crate::theme::Theme;
use ratatui::{
    style::{Color, Modifier, Style},
    text::{Line, Span},
};
use std::sync::OnceLock;
use syntect::{
    easy::HighlightLines,
    highlighting::{FontStyle, ThemeSet},
    parsing::SyntaxSet,
    util::LinesWithEndings,
};

static SYNTAX_SET: OnceLock<SyntaxSet> = OnceLock::new();
static THEME_SET: OnceLock<ThemeSet> = OnceLock::new();

fn syntax_set() -> &'static SyntaxSet {
    SYNTAX_SET.get_or_init(SyntaxSet::load_defaults_newlines)
}

fn theme_set() -> &'static ThemeSet {
    THEME_SET.get_or_init(ThemeSet::load_defaults)
}

/// Guardrail limits to avoid hanging on huge code blocks.
const MAX_BYTES: usize = 256 * 1024; // 256 KB
const MAX_LINES: usize = 5_000;

/// Highlight `code` for the given `lang` (e.g., `"rust"`, `"python"`).
/// Falls back to plain monospace if the language is unknown or limits are exceeded.
/// Returns one `Line` per source line (trailing newlines stripped).
pub fn highlight_code(code: &str, lang: &str, theme: &Theme) -> Vec<Line<'static>> {
    if code.len() > MAX_BYTES {
        return plain_lines(code);
    }
    if code.lines().count() > MAX_LINES {
        return plain_lines(code);
    }

    let ss = syntax_set();
    let ts = theme_set();

    let syntax = resolve_language(lang, ss).unwrap_or_else(|| ss.find_syntax_plain_text());

    let syntect_theme = ts
        .themes
        .get(&theme.syntect_theme)
        .or_else(|| ts.themes.get("base16-ocean.dark"))
        .or_else(|| ts.themes.values().next())
        .expect("syntect must have at least one bundled theme");

    let mut h = HighlightLines::new(syntax, syntect_theme);
    let mut result = Vec::new();

    for line in LinesWithEndings::from(code) {
        let ranges = match h.highlight_line(line, ss) {
            Ok(r) => r,
            Err(_) => {
                result.push(Line::from(line.trim_end_matches('\n').to_string()));
                continue;
            }
        };

        let spans: Vec<Span<'static>> = ranges
            .into_iter()
            .filter_map(|(style, text)| {
                let text = text.trim_end_matches('\n');
                if text.is_empty() {
                    return None;
                }
                Some(Span::styled(text.to_string(), convert_style(&style)))
            })
            .collect();

        result.push(Line::from(spans));
    }

    result
}

/// Highlight a shell/bash snippet.
pub fn highlight_bash(code: &str, theme: &Theme) -> Vec<Line<'static>> {
    highlight_code(code, "bash", theme)
}

/// Resolve a language name/alias to a syntect SyntaxReference.
fn resolve_language<'a>(
    lang: &str,
    ss: &'a SyntaxSet,
) -> Option<&'a syntect::parsing::SyntaxReference> {
    let normalized = normalize_lang(lang);
    ss.find_syntax_by_name(&normalized)
        .or_else(|| ss.find_syntax_by_extension(&normalized))
        .or_else(|| ss.find_syntax_by_extension(lang))
        .or_else(|| ss.find_syntax_by_name(lang))
}

/// Normalize common language aliases to their canonical syntect names or extensions.
fn normalize_lang(lang: &str) -> String {
    let s = lang.to_lowercase();
    let s = s.trim_matches(|c: char| c == '`' || c == ' ');
    match s {
        "js" | "javascript" => "JavaScript",
        "ts" | "typescript" => "TypeScript",
        "py" | "python" => "Python",
        "rb" | "ruby" => "Ruby",
        "rs" | "rust" => "Rust",
        "go" | "golang" => "Go",
        "cpp" | "c++" => "C++",
        "csharp" | "c#" | "cs" => "C#",
        "sh" | "shell" | "zsh" | "fish" | "bash" => "bash",
        "ps" | "ps1" | "powershell" => "PowerShell",
        "json" => "JSON",
        "yaml" | "yml" => "YAML",
        "toml" => "TOML",
        "html" | "htm" => "HTML",
        "css" => "CSS",
        "md" | "markdown" => "Markdown",
        "sql" => "SQL",
        "xml" => "XML",
        "java" => "Java",
        "kotlin" | "kt" => "Kotlin",
        "swift" => "Swift",
        "scala" => "Scala",
        "hs" | "haskell" => "Haskell",
        "lua" => "Lua",
        "diff" | "patch" => "Diff",
        "dockerfile" => "Dockerfile",
        other => other,
    }
    .to_string()
}

/// Convert a syntect Style to a ratatui Style.
/// We map foreground and bold; we intentionally skip background to respect the terminal theme.
fn convert_style(style: &syntect::highlighting::Style) -> Style {
    let mut s = Style::default();
    if let Some(color) = convert_color(style.foreground) {
        s = s.fg(color);
    }
    if style.font_style.contains(FontStyle::BOLD) {
        s = s.add_modifier(Modifier::BOLD);
    }
    s
}

/// Convert a syntect Color to a ratatui Color.
/// syntect alpha=0 encodes an ANSI palette index in the red channel.
fn convert_color(c: syntect::highlighting::Color) -> Option<Color> {
    if c.a == 0x00 {
        if c.r == 0 && c.g == 0 && c.b == 0 {
            return None; // fully transparent = terminal default
        }
        return Some(ansi_color(c.r));
    }
    Some(Color::Rgb(c.r, c.g, c.b))
}

fn ansi_color(index: u8) -> Color {
    match index {
        0 => Color::Black,
        1 => Color::Red,
        2 => Color::Green,
        3 => Color::Yellow,
        4 => Color::Blue,
        5 => Color::Magenta,
        6 => Color::Cyan,
        7 => Color::Gray,
        8 => Color::DarkGray,
        9 => Color::LightRed,
        10 => Color::LightGreen,
        11 => Color::LightYellow,
        12 => Color::LightBlue,
        13 => Color::LightMagenta,
        14 => Color::LightCyan,
        15 => Color::White,
        n => Color::Indexed(n),
    }
}

/// Produce unstyled plain lines as a fallback.
fn plain_lines(code: &str) -> Vec<Line<'static>> {
    code.lines().map(|l| Line::from(l.to_string())).collect()
}
