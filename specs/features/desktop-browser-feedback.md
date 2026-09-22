# Desktop browser and context feedback

| Field | Value |
| --- | --- |
| Version | 1.1.0 |
| Status | Accepted |
| Date | 2026-09-14 |
| Parent Specs | [Desktop client](../clients/desktop-client.md), [In-app browser](desktop-inapp-browser.md) |

## Purpose and scope

Provide accurate embedded-browser automation and preserve user-selected context from pasted text, web pages, assistant responses, and code diffs through the existing Desktop composer. Browser runtime details remain owned by the in-app browser specification; this specification owns the complete user workflow and context contract.

Included capabilities are browser observation and interaction accuracy, the browser toolbar, ordinary user downloads, page selection, find/zoom/context-menu controls, capability-based runtime documentation, turn-scoped tab cleanup, pasted-text attachments, response annotations, and user/model diff comments.

Task sorting and sidebar changes, general document editing, cross-origin iframe input, AX APIs, account migration, and new browser permission systems are outside this feature.

## Reply text interactions

Reply selections share one active overlay per renderer window. A new selection replaces the previous one. The content-sized action strip offers **Add to chat** and **Comment**, separated by a hairline; the comment editor has its own input width. Only selections contained within one completed reply can become response annotations. The overlay preserves a source/Range snapshot while the editor has focus, uses a viewport-clamped portal above the selection (below when necessary), and follows scrolling and resizing.

Outside pointer-down, Escape, a context menu outside the comment editor, task changes, and source unmount dismiss the overlay. Passive dismissal retains an unfinished comment in window memory keyed by source and selection; explicit cancellation or saving clears that draft. Opening or closing a native menu never clears the text selection or automatically reopens the overlay. The editor retains its own input context menu.

Reply body context menus use Electron native menus through a typed preload boundary scoped to the requesting window. On Windows a nonempty selection offers Search with Google, a separator, Copy (Ctrl+C), and Select All. Without a selection only Select All remains. Search opens an encoded Google query in the system browser; Copy and Select All use the requesting WebContents' native operations. macOS omits Select All in the context menu, following the reference platform convention. The message footer retains whole-message copying, and specialized link, file, input, and embedded-page menus retain their ownership.

## Runtime and host boundaries

Electron main owns embedded pages, downloads, page selection capture, and browser controls. Renderer code uses typed preload APIs and serializable events. Pages are persistent DOM webview guests. A window-level host retains each node across task and panel switches; main owns the bound guest WebContents. Creation waits for guest readiness before navigation or automation. Menus and find use renderer portals above the live page, without hiding or recreating it. Guest pointer events dismiss application popups through the host bridge. Selecting a page region must not target another page.

The bundled browser client forwards observation and DOM-CUA operations to one host-owned implementation. Snapshots and actions use the same element identity and name/state semantics, including supported same-origin frame and open Shadow DOM content. Node identifiers are opaque. Cross-origin frames remain unsupported. Existing size limits and command-specific readiness remain in effect.

`browser.documentation()` returns the selected backend's supported API documentation. `agent.documentation.get(name)` returns an applicable named topic. Existing `describeApi()` and bootstrap mismatch checks remain supported. Page-specific WebMCP availability remains dynamically discovered.

`tab.markDeliverable()` and `tab.markHandoff()` mark a tab for the active turn. The latest mark wins. Completed, failed, and cancelled turns close unmarked agent-created tabs and release claimed user tabs without closing them. Marks apply only to their turn. Explicit `tabs.finalize({ keep })` performs early cleanup and writes equivalent turn marks. Evaluation completion/cancellation does not end the turn or reset the Node REPL. Old or repeated terminal notifications cannot clean up a newer turn's pages.

Main retains the thread notification subscription needed by an active browser turn until its terminal event, even when the renderer changes tasks or workspaces. It then releases its ownership without removing a subscription still needed by the renderer. Background turn notifications reach browser cleanup independently of the renderer's foreground filtering.

## Browser user workflows

### Toolbar

The browser toolbar is a 40px strip whose controls all share the 28px catalog control band: Back, Forward, Reload or Stop, the address field, the annotation toggle, and the options menu trigger. The address field sits on the same band, grows with the row up to 770px, and carries the open-in-system-browser action inside its trailing edge, revealed on hover or on keyboard focus within the field; there is no standalone external-browser button. Escape in the address field returns focus to the page. While a page loads, Reload becomes Stop and a 2px pulsing bar runs along the toolbar's bottom edge; the bar is decorative, hidden from assistive technology, and rests as a static rule under reduced motion.

### Downloads

User downloads save automatically in the configured directory (system Downloads by default), with numbered names for collisions. Browser settings exposes Location/Change using the system directory picker and Download history/Manage. The chosen location persists across restart and applies only to subsequent downloads; existing file paths remain unchanged. Downloads do not prompt for a location on each transfer. Main owns DownloadItem instances and persisted records. Records include source tab/thread, file name, byte progress when known, state, and completed local path. Interrupted downloads remain recognizable after restart; resuming the underlying transfer across restart is not required.

The options menu's Downloads action opens Browser settings at Download history; the toolbar has no standalone Downloads control. The history page has search, an all-time history section, an empty state, cancellation, opening completed files, individual record removal, and Clear all. Clearing history removes finished records only, without deleting local files or interrupting active transfers. Navigation does not clear download records. Agent download-wait, upload, and filechooser APIs are not enabled by user download support.

### Find, zoom, and context actions

Find exposes query, current match, total matches, next/previous, and close. Results belong to the initiating tab and query. Ctrl/Cmd+F, Enter/Shift+Enter, and Escape work when the native page has focus. Find is a top-right floating capsule shared with application search presentation, not a toolbar row. Its empty query shows only the input and actions; a nonempty query reveals navigation and result count in a second row inside the capsule. Opening or updating find does not resize the page.

Zoom belongs to the page and supports increase, decrease, and reset to 100%. The options menu lists Find in page, a separator, the zoom group, a separator, Downloads and Inspect, a separator, and Browser settings. Rows are text-only; the zoom group is a compact minus/read-only percentage/plus stepper with a separate reset button disabled at 100%. Browser settings opens the Browser settings tab without opening Download history. Zoom actions keep the menu open. The options popup anchors to its trigger, fit the viewport, and restore focus on keyboard dismissal. Context actions support copying a link, opening it in a new browser tab or externally, and inspecting the page element. Inspect targets the embedded page, not the application renderer.

### Page references

Users can select text, an element, or a rectangle. Selection captures a fixed URL/title, text or element summary, and an image when needed. The result goes to the originating task's draft, with editable comment and removable reference. Subsequent navigation cannot change an existing draft reference. Region captures use the selected page and its current scale. The annotation control selects an element on click or a region on drag; a native text-selection context action captures selected text. The annotation control is a mode toggle: idle it is a 28px icon-only control with a tooltip; active it grows in place to show its icon and the Annotating label and reads as pressed. While the mode is active an interaction layer covers the page: page hover and click are blocked, wheel scrolling still reaches the page, the pointer is a crosshair, and the element under the pointer is outlined in the application accent at that element's own corner radius. A drag past 4px replaces the element outline with a dashed region box and a speech-bubble pointer. Escape during a drag cancels only the drag; Escape with no drag leaves the mode; Escape in the comment editor discards the comment and returns to the page. The page-side accent follows the application accent. While commenting, a captured page image holds the source in place above the live guest, blocking interaction with the covered page. Only the selected image is stored as a task attachment; the full-page preview is transient.

## Composer context

### Pasted text

Pastes of at least 5,000 characters create real UTF-8 text attachments without truncating their content. The composer displays a preview and provides open/remove. Attachments with known lengths from 5,000 through 25,000 characters also offer restoration into the text field.

Pasted text and ordinary files share one horizontally scrollable attachment row. A pasted card uses a single truncated text excerpt as its title, with a restore action or the pasted-text identity beneath it. Full text and character count belong in the preview, not the compact composer card. Pending file creation uses the same card footprint and disables submission until the content is ready or removed.

Submission contains a reversible file reference, not automatic expansion of the entire file into the prompt. The complete file remains accessible through existing file capabilities for the task's host. File references do not imply that AppServer reads or materializes file bodies.

### Feedback sources

Browser annotation starts from the browser toolbar. The selected element or region stays outlined with a dashed accent outline and a speech-bubble marker at its bottom-right corner. The compact comment editor sits 25px from the selection, preferring the right side, then the left, then below, then above, and keeps 16px from the page edges. Response text selection exposes separate Add to chat and Comment actions beside the selected text; it never substitutes a predefined excerpt for an empty selection. Diff editors and comments appear at their corresponding code lines rather than in a detached comments section.

The composer summarizes feedback in a compact count entry. Opening it reveals the sources and selected text separately from user comments, with per-entry edit and removal. Pasted text retains its separate file-card presentation. New empty comments expose dictation; populated comments expose a circular 28px primary confirm action with a check glyph, while existing-comment editing has explicit cancel/save controls. Dictation targets the active comment and is discarded with that editor, without replacing the task composer draft. Comments use existing field and action primitives, without a full-width form beneath the source or duplicate selected text in the compact annotation editor.

The single-line comment editor keeps the same height and text baseline when focus changes or dictation and save actions switch. Action controls do not determine the input height.

Response annotations retain source thread/turn/item, selected text, and the user's comment. Diff annotations retain file, old/new side, line range, selected code snapshot, and comment. User annotations and model review comments remain separate data sources.

Model review comments are parsed from completed assistant responses' `code-comment` directives and mapped to their file and line positions. Commentary text is not a source of finalized review comments. This does not introduce multi-user review state or automatic remapping across revisions.

### Submission and recovery

Pasted-file references, page references, and response/diff annotations use typed `contextRef` input parts. Native input persists the complete source record; server materialization produces model-visible text and image parts. A page screenshot belongs to its context and is not also submitted as an ordinary image. Context goes through the same start, enqueue, update, and steer paths as normal composer input; attachment-only and feedback-only drafts count as nonempty input.

Native parts are the source of truth for message presentation, queue editing, and historical draft restoration. Model text is never decoded into UI state. Missing metadata must not expose an operation whose preconditions cannot be established, such as restoring an unknown-length pasted file into the editor.

Desktop submissions carry a UUID `clientUserMessageId` through start, enqueue, steer, persistence, and user-message events. Queue edits retain that identity; a new submission gets a new identity. Optimistic messages reconcile by this identity within the thread, never by text, image count, or time proximity. This is correlation, not a retry idempotency contract.

Sent messages show read-only feedback summaries and source details using the same presentation primitives as the composer. Screenshots appear once within their owning feedback, with preview and a missing-image state. Pasted text retains its file-card presentation. Live, acknowledged, and reloaded messages have the same content and layout.

Plain-text drafts persist locally. Unsent attachment and feedback drafts survive task navigation in memory; restoring every unsent attachment after application restart is not required. Sending clears the corresponding draft only after acceptance. Failed submissions preserve it. Welcome drafts and task drafts follow the same content conversion rules.

## Presentation and compatibility

UI follows Desktop tokens, locale catalogs, and existing component conventions. The maintained design system previews production components through its adapter boundary; simulated native operations are labelled as such. A successful design preview does not validate Electron input, view stacking, downloads, or capture coordinates. The design system previews the page-side selection layer by running the shared selection script inside a same-origin fixture frame; that preview validates hover, drag, pointer and Escape behavior, not guest capture or coordinates.

Existing ordinary text, file, command, skill, and image inputs remain usable. Existing explicit browser finalize calls remain supported. Persistent data is descriptive of final behavior and contains no delivery-stage terminology.

## Acceptance

- Real local page fixtures verify accessible names, hidden/disabled states, same-origin frame/open Shadow DOM observation, and actions against freshly returned identifiers through the bundled client.
- Multiple evaluations in one turn preserve pages; all terminal turn states respect ownership and current marks in foreground and secondary workspaces.
- Download progress, completion, cancellation, filename collisions, persistence, and opening a completed file work in Electron.
- Find/zoom/context actions operate on the correct native page, including native focus and resized bounds.
- All three page selection modes preserve origin and image coordinates through submission.
- The page-side selection layer's hover outline, drag threshold, pointer changes, and Escape tiers are verified in a DOM fixture; capture coordinates at page zoom are verified in Electron.
- Long pastes retain complete files beyond the previous editor limit and honour the conversion/restoration thresholds.
- Mixed context survives start/queue/steer, failure recovery, task switching, queue editing, and historical decoding without silently losing source content.
- Diff side/range mapping and separate user/model comments work in unified and split views.
- Production surfaces match the accepted design-system specimens at the same theme and width, and all supported UI locales are updated.
