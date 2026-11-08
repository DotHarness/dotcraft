# Host interactions

- Keep filters, selections, and other presentation-only interactions local. For drill-down actions that ask for further investigation or explanation of selected data, call `await window.openai.sendFollowUpMessage({ prompt, title, context })`, where the optional `title` is a concise confirmation-dialog heading of up to 250 characters. The user confirms before the message is sent. Include the selected values and requested investigation in the prompt, and label the action clearly. Pass the optional `context` (JSON, under 32 KiB) only when the follow-up needs structured visualization data beyond the prompt; it is appended to the user's message as visible JSON.

### Remembering interaction state

- Read saved state from `window.openai.widgetState` when rendering. Listen for `openai:set_globals` and apply `event.detail.globals.widgetState` when present. Use defaults for missing or incompatible state.
- After meaningful interactions, call `window.openai.setWidgetState({ modelContent, privateContent }).catch(() => {})`. Updates are optimistic. Both fields accept JSON or `null` and may be omitted (treated as `null`). Each call replaces the snapshot; keep it under 16 KiB. Never save on load or state events.
- Put choices useful for follow-up questions in `modelContent`, UI details worth restoring in `privateContent`, and transient or recomputable values in memory. Only `modelContent` may reach the model, and delivery is best-effort; never rely on it. Saving never starts a turn. No secrets or images.

### Display mode

- The user can open any view fullscreen and step through the conversation's visualizations there; that copy shows the view up to 1,024px wide. `window.openai.displayMode` is `"inline"` or `"fullscreen"`, and `window.openai.maxHeight` is the host's height cap in pixels for the current mode. Read both when rendering and apply `event.detail.globals.displayMode` and `.maxHeight` from `openai:set_globals` when present.
- Content taller than `maxHeight` scrolls inside the view; never size the layout from the viewport height.
- Call `await window.openai.requestDisplayMode({ mode: "fullscreen" })` only from a clearly labeled control the user activates, and only when the view materially benefits from more room. It resolves with `{ mode }`, the mode actually granted; a request made without a user action stays `"inline"`. In fullscreen, `requestDisplayMode({ mode: "inline" })` returns to the conversation.
- Fullscreen uses Escape to close and the left and right arrow keys to page. Text fields keep these keys; call `event.preventDefault()` on them only when the view itself uses them, such as for a step-through.
