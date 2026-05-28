// Terminal lifecycle helpers: init, restore, and panic hook.

use anyhow::Result;
use crossterm::{
    event::{
        DisableBracketedPaste, DisableMouseCapture, EnableBracketedPaste, EnableMouseCapture,
    },
    execute,
    terminal::{disable_raw_mode, enable_raw_mode, EnterAlternateScreen, LeaveAlternateScreen},
};
use ratatui::{backend::CrosstermBackend, Terminal};
use std::io::{stdout, Stdout};

/// The concrete terminal type used throughout the TUI.
pub type Term = Terminal<CrosstermBackend<Stdout>>;

/// Prepare a new frame with cursor hidden.
///
/// Ratatui may move the terminal cursor while flushing cell diffs. Hiding it
/// before each draw avoids visible cursor movement artifacts during high
/// frequency updates (for example while status text is refreshing). The final
/// cursor state is still decided by `frame.set_cursor_position(...)`.
pub fn prepare_frame(terminal: &mut Term) {
    let _ = terminal.hide_cursor();
}

/// Enable raw mode, enter the alternate screen, enable bracketed paste,
/// install a panic hook that restores the terminal before printing the panic,
/// and return the Ratatui terminal handle.
pub fn init() -> Result<Term> {
    install_panic_hook();
    enable_raw_mode()?;
    execute!(
        stdout(),
        EnterAlternateScreen,
        EnableBracketedPaste,
        EnableMouseCapture
    )?;
    // Keyboard enhancement (Kitty protocol) is attempted but not required;
    // Windows Terminal and some other terminals do not support it.
    let _ = execute!(
        stdout(),
        crossterm::event::PushKeyboardEnhancementFlags(
            crossterm::event::KeyboardEnhancementFlags::DISAMBIGUATE_ESCAPE_CODES
                | crossterm::event::KeyboardEnhancementFlags::REPORT_EVENT_TYPES,
        )
    );
    let backend = CrosstermBackend::new(stdout());
    let terminal = Terminal::new(backend)?;
    Ok(terminal)
}

/// Restore the terminal to its state before `init()` was called.
/// Safe to call multiple times; subsequent calls are no-ops if already restored.
pub fn restore() {
    // Best-effort: ignore individual errors so all steps are attempted.
    let _ = execute!(stdout(), crossterm::event::PopKeyboardEnhancementFlags);
    let _ = execute!(
        stdout(),
        DisableMouseCapture,
        DisableBracketedPaste,
        LeaveAlternateScreen
    );
    let _ = execute!(stdout(), crossterm::cursor::Show);
    let _ = disable_raw_mode();
}

/// Install a panic hook that restores the terminal before printing the panic
/// message. Without this, a panic leaves the terminal in raw/alternate-screen
/// mode and the user's shell becomes unusable.
fn install_panic_hook() {
    let original_hook = std::panic::take_hook();
    std::panic::set_hook(Box::new(move |panic_info| {
        restore();
        original_hook(panic_info);
    }));
}

/// RAII guard: restores the terminal when dropped.
/// Wrap the event loop result in this guard so cleanup always runs.
pub struct TerminalGuard;

impl Drop for TerminalGuard {
    fn drop(&mut self) {
        restore();
    }
}
