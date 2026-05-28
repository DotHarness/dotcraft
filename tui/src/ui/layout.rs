// Screen zone layout computation (§7.1 of specs/clients/tui-client.md).
// Content-flow layout. ChatView takes its preferred height when content is
// short; the composer stack follows it and only settles near the bottom once
// the transcript needs the available viewport.

use ratatui::layout::Rect;

/// Output of the layout computation — the two primary zones plus sub-zones.
pub struct Zones {
    pub chat_view: Rect,
    /// Shown above InputEditor only while a turn is running.
    pub status_indicator: Option<Rect>,
    /// Shown between StatusIndicator and InputEditor when pending_input is non-empty.
    pub pending_preview: Option<Rect>,
    pub input_editor: Rect,
    /// Slash command suggestions, rendered directly below InputEditor.
    pub command_popup: Option<Rect>,
    /// On-demand footer/status line below InputEditor.
    pub footer: Option<Rect>,
}

/// Compute the screen layout.
///
/// - `transcript_height`: preferred height of the rendered ChatView content.
/// - `turn_running`: whether to reserve space for the `StatusIndicator`.
/// - `has_pending`: whether to reserve space for `PendingInputPreview`.
/// - `input_height`: desired InputEditor height in rows (content lines only, no separator).
/// - `status_indicator_lines`: number of lines the StatusIndicator needs (default 1).
/// - `command_popup_height`: desired height for slash command suggestions.
/// - `footer_height`: desired height for the contextual composer footer.
pub fn compute(
    area: Rect,
    transcript_height: u16,
    turn_running: bool,
    has_pending: bool,
    input_height: u16,
    status_indicator_lines: u16,
    command_popup_height: u16,
    footer_height: u16,
) -> Zones {
    let compact = area.height < 20;

    // On compact terminals suppress extra chrome.
    let input_h = if compact {
        1
    } else {
        input_height.clamp(1, 10)
    };
    let footer_h: u16 = footer_height.min(1);
    let status_h: u16 = if turn_running && !compact {
        status_indicator_lines.max(1)
    } else {
        0
    };
    let pending_h: u16 = if has_pending && !compact { 1 } else { 0 };
    let max_popup_h = area
        .height
        .saturating_sub(status_h + pending_h + input_h + footer_h);
    let popup_cap = if compact { 4 } else { 8 };
    let popup_h = command_popup_height.min(popup_cap).min(max_popup_h);

    let composer_h = status_h + pending_h + input_h + popup_h + footer_h;

    let available_chat_h = area.height.saturating_sub(composer_h);
    let chat_h = transcript_height.min(available_chat_h);
    let gap_h = if chat_h > 0 && composer_h > 0 && chat_h < available_chat_h && !compact {
        1.min(available_chat_h - chat_h)
    } else {
        0
    };

    let chat_view = Rect {
        x: area.x,
        y: area.y,
        width: area.width,
        height: chat_h,
    };

    let mut y = area.y + chat_h + gap_h;

    let status_indicator = if status_h > 0 {
        let r = Rect {
            x: area.x,
            y,
            width: area.width,
            height: status_h,
        };
        y += status_h;
        Some(r)
    } else {
        None
    };

    let pending_preview = if pending_h > 0 {
        let r = Rect {
            x: area.x,
            y,
            width: area.width,
            height: pending_h,
        };
        y += pending_h;
        Some(r)
    } else {
        None
    };

    let input_editor = Rect {
        x: area.x,
        y,
        width: area.width,
        height: input_h,
    };
    y += input_h;

    let command_popup = if popup_h > 0 {
        let r = Rect {
            x: area.x,
            y,
            width: area.width,
            height: popup_h,
        };
        y += popup_h;
        Some(r)
    } else {
        None
    };

    let footer = if footer_h > 0 {
        Some(Rect {
            x: area.x,
            y,
            width: area.width,
            height: footer_h,
        })
    } else {
        None
    };

    Zones {
        chat_view,
        status_indicator,
        pending_preview,
        input_editor,
        command_popup,
        footer,
    }
}

/// Compute the preferred InputEditor height from the number of content lines.
/// No separator row is included (separator was removed in the new design).
pub fn input_preferred_height(content_lines: usize) -> u16 {
    (content_lines.max(1) as u16).clamp(1, 10)
}

/// Compute the preferred StatusIndicator height from the number of detail lines
/// (header line + wrapped detail lines).
pub fn status_indicator_height(detail_lines: usize) -> u16 {
    (1 + detail_lines as u16).min(4)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn short_transcript_places_input_after_content() {
        let area = Rect {
            x: 0,
            y: 0,
            width: 80,
            height: 24,
        };
        let zones = compute(area, 6, false, false, 1, 1, 0, 0);

        assert_eq!(zones.chat_view.height, 6);
        assert_eq!(zones.input_editor.y, 7);
        assert!(zones.input_editor.y < area.height - 1);
    }

    #[test]
    fn long_transcript_uses_available_viewport() {
        let area = Rect {
            x: 0,
            y: 0,
            width: 80,
            height: 24,
        };
        let zones = compute(area, 100, false, false, 1, 1, 0, 0);

        assert_eq!(zones.chat_view.height, 23);
        assert_eq!(zones.input_editor.y, 23);
    }

    #[test]
    fn footer_is_on_demand() {
        let area = Rect {
            x: 0,
            y: 0,
            width: 80,
            height: 24,
        };

        let quiet = compute(area, 6, false, false, 1, 1, 0, 0);
        assert!(quiet.footer.is_none());

        let active = compute(area, 6, false, false, 1, 1, 0, 1);
        assert_eq!(active.footer.expect("footer").height, 1);
    }

    #[test]
    fn command_popup_participates_in_composer_flow() {
        let area = Rect {
            x: 0,
            y: 0,
            width: 80,
            height: 24,
        };
        let zones = compute(area, 6, false, false, 1, 1, 6, 1);
        let input = zones.input_editor;
        let popup = zones.command_popup.expect("command popup");
        let footer = zones.footer.expect("footer");

        assert_eq!(zones.chat_view.height, 6);
        assert_eq!(input.y, 7);
        assert_eq!(popup.y, input.y + input.height);
        assert_eq!(footer.y, popup.y + popup.height);
        assert_eq!(footer.height, 1);
    }
}
