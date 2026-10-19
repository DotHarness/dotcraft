# Desktop browser and context feedback

| Field | Value |
| --- | --- |
| Version | 0.9.1 |
| Status | Living |
| Date | 2026-10-19 |
| Parent Specs | [In-app browser](desktop-inapp-browser.md) |

## Purpose and scope

Provide accurate embedded-browser interaction and preserve user-selected context from pasted text, web pages, assistant responses, code diffs, and file previews through the Desktop composer. The [in-app browser runtime](desktop-inapp-browser.md) owns page hosting, command identity, supported APIs, readiness, browser policy, and turn-terminal tab cleanup; this specification owns the user workflows and the context contract.

Included: the browser toolbar, user downloads, find/zoom/context-menu controls, page references, pasted-text attachments, response annotations, and user and model line comments on diffs and file previews.

Out of scope: general document editing, cross-origin iframe input, accessibility-tree APIs, and new browser permission systems.

## Runtime boundaries

Electron Main owns downloads, page-selection capture, and browser controls, scoped to the requesting window. Renderer overlays stay above live pages without hiding or recreating them. Selection capture targets its source page. Browser runtime cleanup continues while the renderer shows another task or workspace.

## Reply text interactions

One reply-selection overlay is active per window; a new selection replaces it. It offers **Add to chat** and **Comment**. Only a selection contained within one completed reply can become a response annotation, and an empty selection never becomes one. The overlay keeps its source selection while the comment editor has focus and follows scrolling and resizing.

Outside pointer-down, Escape, an outside context menu, task changes, and source unmount dismiss the overlay. Passive dismissal keeps an unfinished comment in memory keyed by source and selection; explicit cancel or save clears it. Opening or closing a native menu never clears the text selection or reopens the overlay.

Reply bodies use native context menus. With a selection they offer Search with Google (opened in the system browser), Copy, and Select All; without one, only Select All. macOS omits Select All, following platform convention. Link, file, input, and embedded-page menus keep their own behavior.

## Browser user workflows

### Toolbar

The toolbar holds Back, Forward, Reload or Stop, the address field, the annotation toggle, and the options menu. Open-in-system-browser is an action inside the address field, revealed on hover or focus. Escape in the address field returns focus to the page. While a page loads, Reload becomes Stop and a decorative progress indicator shows, static under reduced motion.

### Downloads

User downloads save automatically to the configured directory (system Downloads by default) without prompting, with numbered names on collision. Browser settings exposes the download location and download history. A changed location persists and applies only to later downloads.

Main owns active transfers and persisted records. A record holds source tab and thread, file name, byte progress when known, state, and completed local path. Interrupted downloads remain recognizable after restart; resuming them is not required.

The options menu's Downloads action opens download history, which supports search, cancellation, opening completed files, removing individual records, and Clear all. Clearing removes finished records only, never local files or active transfers. Navigation does not clear records. User download support does not enable agent download, upload, or file-chooser APIs.

### Find, zoom, and context actions

Find exposes query, current match, total matches, next/previous, and close. Results belong to the initiating tab and query. Ctrl/Cmd+F, Enter/Shift+Enter, and Escape work while the page has focus. Find floats over the page and never resizes it.

Zoom belongs to the page and supports increase, decrease, and reset to 100%. The options menu groups Find in page; zoom; Downloads and Inspect; and Browser settings. Zoom actions keep the menu open, and keyboard dismissal restores focus to the trigger. Page context actions copy a link, open it in a new browser tab or externally, and inspect the element. Inspect targets the embedded page, not the application.

### Page references

Users select text, an element, or a region. A selection captures a fixed URL and title, the text or element summary, and an image when needed, at the page's current scale. The result goes to the originating task's draft with an editable comment and a removable reference; later navigation never changes it.

The annotation toggle is a mode. While active, an interaction layer covers the page: hover and click are blocked, wheel scrolling still reaches the page, and the element under the pointer is outlined in the application accent. Click selects an element; drag selects a region. A native text-selection context action captures selected text. Escape cancels an in-progress drag, otherwise leaves the mode; Escape in the comment editor discards the comment.

The selected element or region stays outlined while the compact comment editor is open beside it. While commenting, a captured image holds the page in place and blocks interaction with it. Only the selected image is stored as a task attachment; any full-page preview is transient.

## Composer context

### Pasted text

Pastes of at least 5,000 characters become UTF-8 text attachments without truncation. The composer shows a preview and offers open and remove; attachments with known length up to 25,000 characters also offer restoring into the text field. Pasted text and files share one attachment row. A pending attachment blocks submission until it is ready or removed.

Submission carries a reversible file reference, not the expanded file. The file stays accessible through the task host's file capabilities; a reference does not imply AppServer reads or materializes the file body.

### Line comments

Line comments belong to the Changes panel diff (unified and split) and to text file previews, shown as cards at their lines. The in-transcript edited-files card and external-change review carry no line comments.

In a diff, a comment starts from a per-row add control on the line-number edge (hover or keyboard focus). Line numbers select lines, Shift+click extends on the same side, and the add control on a selection comments on the whole selection; dragging from it selects a range. In a file preview, a comment starts from a Comment action beside a non-empty text selection; rendered Markdown switches to source first. A line that already ends a comment or open draft does not start another.

A draft opens focused with Cancel, dictation, and Comment, which stays disabled while empty. Enter submits and returns focus to the task composer, Shift+Enter inserts a newline, Escape or Cancel discards. Any number of drafts may be open; they persist in memory per thread until submitted or discarded. A submitted comment stays on its line and can be edited (with Cancel restoring the saved text) or deleted. The card names its author and its line as `L` (old side) or `R` (new side) with the line number or range.

### Composer summary

The composer summarizes feedback in two counts: line comments, and page plus response annotations. Each opens its list; its remove action clears everything it counts.

- A line comment row names its file (opening it), side, and line or range, then the comment. It does not repeat the code; editing happens on the card.
- The annotations list shows page annotations first, then numbered response annotations with their selected text and comment, each editable and removable.
- Sent messages show the same summaries read-only.

The compact annotation editor offers dictation while empty and a confirm action once populated, never submits empty text, and keeps its height stable as actions change. Dictation targets the active comment and never replaces the task composer draft.

### Annotation data

- Response annotations keep source thread, turn, and item, the selected text, and the comment.
- Diff annotations keep file, old/new side, line range, the selected code snapshot, and the comment. A file-preview comment is a diff annotation on the new side.
- Model review comments come from `code-comment` directives in completed assistant responses, mapped to file and line. Commentary text is never a source. User annotations and model review comments are separate sources. There is no multi-user review state or remapping across revisions.

### Submission and recovery

Pasted-file references, page references, and response and diff annotations are submitted as typed `contextRef` input parts, and Desktop submissions carry a `clientUserMessageId` (see [AppServer Protocol](../protocols/appserver-protocol.md#inputpart)). A page image belongs to its context and is not also submitted as an ordinary image. Context uses the same start, enqueue, update, and steer paths as normal input; attachment-only and feedback-only drafts count as non-empty.

Native input parts are the source of truth for message presentation, queue editing, and draft restoration; model text is never decoded into UI state. An operation whose preconditions cannot be established from the metadata, such as restoring an unknown-length paste, is not offered. Optimistic messages reconcile only by `clientUserMessageId`, never by content or timing; queue edits keep the id and a new submission gets a new one.

Sent messages show read-only feedback with each image once, including a missing-image state. Live, acknowledged, and reloaded messages present identically.

Plain-text drafts persist locally. Unsent attachment and feedback drafts survive task navigation in memory, not necessarily application restart. Sending clears a draft only after acceptance; failure preserves it. Welcome and task drafts follow the same conversion rules.

## Presentation

UI follows [Desktop DESIGN](../architecture/DESIGN.md#line-comments) and shared component conventions. Design-system previews do not validate Electron input, view stacking, downloads, or capture coordinates.
