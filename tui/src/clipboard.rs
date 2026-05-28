// Feature-gated clipboard abstraction using arboard.
// When the "clipboard" feature is disabled, functions return errors gracefully.

#[cfg(feature = "clipboard")]
pub fn read_text() -> Result<String, String> {
    let mut clipboard =
        arboard::Clipboard::new().map_err(|e| format!("clipboard unavailable: {e}"))?;
    clipboard
        .get_text()
        .map_err(|e| format!("clipboard read failed: {e}"))
}

#[cfg(not(feature = "clipboard"))]
pub fn read_text() -> Result<String, String> {
    Err("clipboard feature not enabled".to_string())
}

#[cfg(feature = "clipboard")]
pub fn write_text(text: &str) -> Result<(), String> {
    let mut clipboard =
        arboard::Clipboard::new().map_err(|e| format!("clipboard unavailable: {e}"))?;
    clipboard
        .set_text(text.to_owned())
        .map_err(|e| format!("clipboard write failed: {e}"))
}

#[cfg(not(feature = "clipboard"))]
pub fn write_text(_text: &str) -> Result<(), String> {
    Err("clipboard feature not enabled".to_string())
}
