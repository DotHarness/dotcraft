// Plan inline block helper.
// The full rendering logic lives in ChatView::render_inline_plan.
// This module provides helpers used by ChatView.

use crate::app::state::AppState;

/// Returns true if there is a plan that should be rendered inline.
pub fn should_show(state: &AppState) -> bool {
    state.plan.is_some()
}
