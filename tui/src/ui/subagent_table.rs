// SubAgent inline block helper.
// The full rendering logic lives in ChatView::render_inline_subagents.
// This module re-exports the helper so other consumers can use it if needed.

use crate::app::state::AppState;

/// Returns true if there is any SubAgent data worth rendering.
pub fn should_show(state: &AppState) -> bool {
    !state.subagent_entries.is_empty()
}
