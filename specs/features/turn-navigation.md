# DotCraft Desktop Turn Navigation Specification

| Field | Value |
|-------|-------|
| **Version** | 0.9.1 |
| **Status** | Living |
| **Date** | 2026-10-19 |
| **Related Specs** | [AppServer Protocol](../protocols/appserver-protocol.md), [Design System](../architecture/DESIGN.md), [Desktop Client](../clients/desktop-client.md) |

Purpose: define the conversation turn navigation rail, which lets a user see the shape of a long thread, preview any earlier user message, and jump to it.

## 1. Scope

The turn navigation rail is a column of short markers at the leading edge of the active conversation, one marker per user message the transcript shows. It covers the whole thread, including turns whose history pages have not been loaded yet.

The feature includes:

- The navigation index: one entry per user message, built from loaded turns plus Turn metadata for older, unloaded turns.
- The rail: markers, viewport tracking, hover magnification, and drag scrubbing.
- Jumping: click, keyboard focus, and `Alt+ArrowUp` / `Alt+ArrowDown`, including loading older history on demand.
- The preview card: the user message, the reply that followed it, and what the turn produced.
- Bookmarks: marking entries so they stand out on the rail.

The feature is owned by Desktop. It reads the AppServer history methods, and uses the Turn-page `backwardsCursor` to read outward from a Turn it has not loaded.

## 2. Goals and Non-goals

### 2.1 Goals

- A user can reach any earlier user message in a thread without scrolling through everything between.
- The rail shows where the viewport is in the thread at a glance.
- Hovering a marker tells the user what that turn was about before they commit to the jump.
- The rail stays out of the way: it appears only when there is room and enough history to navigate.

### 2.2 Non-goals

- Search. Finding text inside the transcript belongs to conversation find.
- Navigation between threads.
- Syncing bookmarks across devices or through AppServer.
- A separate bookmark list or bookmark filter.
- Changing how the transcript itself pages or renders history.

## 3. Architecture boundary

- **Turn metadata** comes from `thread/turns/list`, which returns Turns without Items. It lists which turns exist, oldest to newest, without reading their content. Each listed Turn keeps the page's `backwardsCursor` and its offset in that page, which is enough to read outward from it later.
- **Entry content** comes from the conversation store for loaded turns, and from Turn-scoped `thread/items/list` for a turn that has not been loaded.
- **Revealing** an unloaded turn loads a history segment around it, separated from the rest of the loaded history by gaps.
- **Bookmarks** are Desktop-local settings. They are never sent to AppServer.

## 4. Navigation index

### 4.1 Entries

- Each user message that the transcript renders as a user bubble is one entry. A turn with several visible user messages has several entries. Guidance and subagent-mailbox messages, which the transcript hides, have none.
- Entries are ordered oldest to newest by Turn and then by Item position.
- An entry's **label** is its message text, trimmed. An entry with no text shows a no-content label.
- An entry's **response** is the last agent message that follows its user message in the same Turn, before the next entry's user message. It may be empty.
- An entry's **outputs** are defined in §8. Only the last entry of a Turn carries outputs.

### 4.2 Unloaded turns

- While the transcript has not loaded the oldest page, Desktop reads Turn metadata newest first, 100 Turns per request, until it has listed every Turn or read 1000 Turns.
- Each listed Turn that is not in the conversation store contributes one placeholder entry. A placeholder has no label, response, or outputs until its preview loads (§6.2).
- When a placeholder's preview loads, the placeholder is replaced by one entry per visible user message in that Turn. A Turn with no visible user message leaves no entry.
- When a Turn becomes loaded in the conversation store, its entries come from the store and replace any placeholder or preview for it.
- If the Turn listing fails, or the thread has more than 1000 Turns, the rail is shown only once the transcript has loaded all history.
- The Turn listing is metadata only. It does not advance the transcript's history cursor and never loads Items.

### 4.3 Freshness

- Entries for loaded Turns follow the conversation store, so new and running turns appear as they arrive.
- The Turn listing and every loaded preview belong to one thread and one restore generation. Switching threads, switching workspaces, reconnecting, rollback, and fork discard them and read again.
- Preview results are cached for the lifetime of that generation.

## 5. Rail

### 5.1 Visibility

- The rail is shown for the active thread when it has at least 4 entries and the reading column leaves room for it beside the conversation area's leading edge. It is re-evaluated whenever the window, conversation area, or column resizes.

### 5.2 Placement

- The rail is vertically centred at the leading edge of the conversation area, above the transcript, and does not scroll with it.
- The rail has a bounded height; beyond it, the rail scrolls on its own without a visible scrollbar.
- The rail keeps the first active entry (§5.3) in view as the transcript scrolls.

### 5.3 Marker states

- **Active** entries are those whose part of the transcript intersects the conversation viewport. The first entry of a Turn owns the whole Turn block; a later entry owns its own user bubble. Several entries can be active at once.
- The entry under the pointer, the keyboard-focused entry, and the scrub target are each the **target**. The target's marker extends fully. Its neighbours extend part way, less with each step away, up to three steps.
- While another entry is the target, active entries lose their highlight so only one entry reads as selected.
- Bookmarked entries stay fully visible at rest and carry a dot at the end of their marker.

### 5.4 Scale

Rendering cost remains bounded to the visible rail. Entry updates preserve unchanged marker identity and position.

## 6. Interactions

### 6.1 Jumping

- Clicking an entry scrolls the transcript smoothly so its user bubble sits at the top of the conversation viewport, then flashes the bubble once (§6.3). Reduced motion jumps without animation.
- Clicking a placeholder first reveals its Turn (§6.4), then jumps.
- Pressing on the rail and dragging along it scrubs: the entry under the pointer becomes the scrub target and the transcript jumps to it immediately, without smooth scrolling. Placeholders are skipped while scrubbing. Releasing the pointer ends scrubbing and does not also count as a click.
- `Alt+ArrowUp` jumps to the previous entry above the viewport's top edge and `Alt+ArrowDown` to the next one below it, with a small tolerance. It works wherever focus is in the window except a text input or a dialog, and uses the same smooth scroll and flash. If the previous entry is a placeholder, Desktop reveals it first.
- Each marker is a button. Tab moves focus between entries, and focusing an entry opens its preview card.

### 6.2 Preview card

- Hovering or focusing an entry opens its preview card beside the rail after a short delay. Moving between entries shortly after a card closes opens the next card immediately.
- The card shows the entry's label on one line, then up to three lines of the response rendered as compact Markdown, then its outputs (§8). Links and images in the response are rendered as plain text.
- A Turn triggered by an automation shows the automation origin icon before its label.
- For a placeholder, Desktop starts loading the preview once the pointer rests on the entry briefly, or immediately on keyboard focus. The card shows a loading state.
- Loading a preview reads the Turn's Items with `thread/items/list`, derives the entries of §4.1 from them, and trims each label and response to 240 characters.
- A preview that fails to load says so. Desktop does not retry it within the same generation.
- The card closes when the pointer leaves the rail and the card, or focus leaves the rail. The pointer can travel into the card to reach its bookmark toggle.

### 6.3 Flash

The flash is a single background pulse on the user bubble that fades back to its own fill. It confirms which message the jump landed on and has no other meaning.

### 6.4 Revealing an unloaded turn

- Revealing positions a cursor on the target Turn from its listing, then loads one history page on each side of it. Those Turns become a new segment of the transcript, with gaps between it and any other segment. It does not load the history in between.
- If the new segment meets or overlaps a loaded one, the two merge and no gap remains between them.
- The gaps load as the user scrolls toward them, so reading on from a jump target fills in history in either direction.
- Revealing is started only by an explicit user action. Hovering or previewing never loads history pages.
- If revealing fails, the transcript keeps what it had loaded and no jump happens.

## 7. Bookmarks

- The preview card has a bookmark toggle.
- A bookmark belongs to one entry: the pair of its Turn id and its user message Item id. A placeholder cannot be bookmarked until its preview has loaded, and an entry whose user message has no server Item id yet cannot be bookmarked.
- A placeholder whose Turn holds a bookmarked entry is shown as bookmarked, so bookmarks in unloaded history stay visible after a restart.
- Bookmarks are stored per workspace and thread in Desktop settings, survive restarts, and never leave the device.
- Deleting a thread removes its bookmarks. Archiving keeps them.
- A bookmark whose entry no longer exists, for example after rollback, is ignored and not shown.

## 8. Outputs

- Outputs describe what a completed Turn produced. Running and failed Turns, and placeholders, have none.
- A Turn's outputs are:
  - each Markdown file the Turn wrote, as a **file** output labelled with the file name;
  - each HTML file the Turn wrote, as a **web preview** output labelled with the file name;
  - each image the Turn generated, as an **image** output.
- Files are selected by the same rule the transcript uses for turn artifacts.
- Outputs are de-duplicated by kind and label, ordered web previews first, then files, then images.
- The card shows at most two outputs, each with its icon and label, then a count of the rest.

## 9. Accessibility and localization

- The rail is a labelled navigation landmark. Each entry button names its 1-based position and whether it is bookmarked.
- Active entries are marked current, and an open preview card describes its entry.
- The bookmark toggle is a pressed/unpressed button, and the preview loading state is exposed as a status.
- All strings are localized in every supported Desktop locale.

## 10. Constraints

- With an AppServer that returns no `backwardsCursor`, unloaded Turns get no placeholders: the rail covers loaded history only, and appears once that reaches four entries.
- A thread still being created has no rail.
- Remote workspaces behave the same as local ones.
