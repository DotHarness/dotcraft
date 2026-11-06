# Desktop Thread Panels

| Field | Value |
|---|---|
| Version | 0.10.2 |
| Status | Living |
| Date | 2026-11-06 |
| Parent Specs | [Desktop Client](../clients/desktop-client.md), [In-App Browser](desktop-inapp-browser.md) |

## 1. Ownership

A panel belongs to a stable project identity and a thread, or that project's Welcome draft. The identity is independent of the foreground connection, cwd, worktree, temporary forwarding port, and window selection. Each window independently selects which panel to present.

The panel owns its viewer descriptors, open system tabs, active tab, preferred visibility, full-view state and layout. Responsive suppression changes presentation, not the saved preference. Switching threads, local projects, or non-conversation views does not close tabs. Welcome-to-thread creation transfers the panel and its live resources without duplication.

React presentation, durable routes and running resources are separate. An inactive component need not remain mounted. A terminal detaches from its PTY; a browser presentation detaches or hides without closing a protected page. File editor navigation retains the existing editor session and save/conflict safeguards.

## 2. Local Background Work

A local connection demoted to secondary continues handling its bound `ext/nodeRepl/evaluate` and `ext/nodeRepl/cancel` requests in the originating client, workspace and window context. Browser approvals and turn subscriptions use that same client. Disposed or replaced connections cannot admit work. Process-wide runtime identifiers cannot be rebound to another project/window while live: a colliding thread or browser session id is rejected rather than sharing its runtime. This exception does not enable arbitrary background dynamic tools or foreground renderer streams.

Browser turns retain their originating connection until completion, failure or cancellation. Active leases are exempt from the idle secondary connection limit; idle connections remain bounded. Transcript warm-cache eviction is independent of panel and browser ownership.

Actual disconnection, explicit runtime reset, AppServer restart and window shutdown retain their cancellation semantics. Connection disposal releases Agent registrations without treating retained viewer pages as an explicit close; window/application teardown checkpoints pages before disposing clients and execution resources. Requests are never automatically replayed. Remote projects remain foreground-only; persistent panel state does not create remote background connections or continue work after a real disconnect.

## 3. Durable Routes

Panel checkpoints are Desktop-local, versioned data under Electron userData. Only explicit serializers participate; the application never serializes arbitrary stores, components or runtime objects.

| Type | Durable state | Restart behavior |
|---|---|---|
| Files | Tab identity | Reopen the file explorer |
| File | Path, content classification, navigation/display hints | Revalidate access and reload; no unsaved buffer or undo-history promise |
| Browser | Tab identity, browserStorageId, URL and metadata | Reuse a live page or lazily restore a navigation snapshot |
| Terminal | Tab/session identity, cwd and display metadata | Start a new shell on first actual mount if no session survives |
| System tabs | Tab kind | Render from the restored thread and current capabilities |
| Workflow/automation | Stable run/automation identity | Query the existing resource |

Side Chats and other ephemeral surfaces without a reconstruction contract are not persisted. Restoring an interactive terminal never saves or replays command, environment, PID or pendingRun; shell profiles still have their ordinary startup effects. Inactive terminal descriptors do not eagerly start shells.

A checkpoint preserves order, active tab, preferred open state, full view and existing layout values. Unsupported or unavailable routes cannot block thread recovery. The active tab falls back to a surviving route, or the launcher. Restoring a manually opened empty panel preserves its open preference.

Reads finish before persistence is enabled for that scope. Initial empty state must not overwrite saved data; stale asynchronous restoration cannot modify a newer selection or resurrect closed tabs. Normal changes are checkpointed and normal shutdown flushes accepted updates. A crash may lose updates that have not been accepted. Corrupt or unknown-version data is ignored safely, without legacy parsers or migrations.

Explicit tab close and thread deletion remove their durable entries. Suspension and ordinary process shutdown preserve them. Existing archive/removal behavior remains authoritative.

## 4. Browser Snapshots

A stable random browserStorageId identifies the snapshot independently of webContents. Its owner includes project scope, thread/draft and tab identity. Snapshot data contains URL, title, favicon, Chromium navigation history entries and active index. Chromium pageState is sensitive local state: it is not logged, traced or exposed to agents. There is no custom DOM, JS heap, cookie or form serializer.

Restore outcomes are `already-live`, `snapshot-ready`, `missing`, and `owned-by-another-window`. Snapshot-ready pages remain suspended until attached; the host binds a pristine guest before its first navigation and uses Electron navigationHistory.restore. The native guest bootstrap association is covered by the real Electron lifecycle test when upgrading Electron. Missing snapshots may fall back to a validated saved URL, but other-window ownership cannot be bypassed by automatically cloning the page. Restored pages do not inherit an old evaluation, turn or Agent ownership.

The initial primary window owns browser snapshot persistence/restoration for that application run. It is not automatically elected again or stolen by another window. Existing live ownership is respected; a subsequent application run establishes a new initial owner.

Snapshots are atomically written beneath userData and bounded to 100 pages / 64 MiB. Evicting a snapshot does not close its durable tab. Website profiles retain the existing workspace partition. Local-preview sessions and exact-file grants stay attached to their owning scope, not whichever project is now foreground; project roots are never merged into a single authority. Grants are not restored from disk. Every restored URL and history entry must satisfy scheme, profile and current access rules.

## 5. Page Budget

Each window has a soft budget of 32 live-detached pages. A conversation's recently selected page has a 30-minute working-set protection period. Presented/current pages, active browser use, tab capture, audio/media capture, loading, restoring and active downloads are protected. When every candidate is protected, the budget may temporarily be exceeded.

Prefer inactive secondary pages, then inactive selected pages, then secondary pages of the current conversation. Suspension first saves its snapshot, then disposes the execution instance while preserving the route. It does not preserve JS heap, connections, timers or in-flight requests. Restore/suspend operations do not overlap for one page.

Automation protection comes from browser commands and turn lifecycle, never panel visibility. Finalize, deliverable, handoff and turn cleanup continue to follow the in-app browser ownership contract.

## 6. Validation

Behavior coverage includes same-project and cross-local-project A/B/A navigation; system/viewer selection and visibility; Welcome handoff; original live browser and PTY continuity; background evaluation/cancel/approval routing; stale connection rejection; close/delete versus late restore; checkpoint reconstruction; lazy terminal startup without command replay; Chromium history restoration; snapshot corruption and limits; protected-page budgets; multi-window ownership; and project/window/file-authority isolation.

Use actual Electron with an isolated profile for live-page, history and restart acceptance. Unit tests use temporary state directories, explicit signals and injected clocks rather than filesystem/source-text assertions or sleeps.
