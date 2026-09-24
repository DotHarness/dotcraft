# `dotcraft.computer` API

All methods return promises. Errors reject with an `Error` whose message starts with a stable code such as `app_not_approved:`.

## Types

- `App`: `{ id, displayName, isRunning }`. `id` identifies the application.
- `Window`: `{ id, pid, title, app: { id, displayName } }`. Pass the whole object back as `window`.
- `Screenshot`: `{ id, width, height }`. Coordinates are pixels of this image.

## Finding apps and windows

| Call | Returns |
|---|---|
| `list_apps()` | `App[]` of installed and running apps. |
| `list_windows()` | `Window[]` of visible windows. |
| `get_window({ id })` | The `Window` with that handle. |
| `launch_app({ app })` | Starts an app by an `id` from `list_apps()` in the current turn. |
| `activate_window({ window })` | Brings the window to the front. |

## Observing

`get_window_state({ window, include_screenshot = true, include_text = false })` returns `{ window, screenshots: Screenshot[], accessibility: { tree } | null }`. `tree` lists interactive elements with their indexes. Screenshots are attached to the tool result; do not emit them again.

## Acting

| Call | Notes |
|---|---|
| `click({ window, element_index })` | Clicks an element from the latest `include_text` observation. |
| `click({ window, x, y, screenshotId, click_count = 1, mouse_button = "left" })` | Clicks a screenshot pixel. `mouse_button` is `left`, `right` or `middle`; `click_count` is 1 to 3. |
| `type_text({ window, text })` | Types literal text into the focused control. |
| `press_key({ window, key })` | Presses a key or chord such as `Return`, `Tab`, `Escape`, `F5`, `ctrl+s` or `ctrl+shift+Tab`. |
| `scroll({ window, x, y, scrollX = 0, scrollY = 0, screenshotId })` | Scrolls at a screenshot pixel. Positive `scrollY` scrolls down and positive `scrollX` scrolls right, in wheel notches. |
| `set_value({ window, element_index, value })` | Sets an editable element's value directly. |
| `drag({ window, from_x, from_y, to_x, to_y, screenshotId })` | Drags between screenshot pixels. |

## Example

```js
const windows = await dotcraft.computer.list_windows()
const notepad = windows.find((w) => w.app.displayName === "Notepad")
const state = await dotcraft.computer.get_window_state({ window: notepad, include_text: true })
nodeRepl.write(state.accessibility.tree)
```
