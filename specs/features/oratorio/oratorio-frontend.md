# Oratorio Native Surfaces Specification

| Field | Value |
| --- | --- |
| Version | 1.1.0 |
| Status | Living |
| Date | 2026-05-27 |
| Parent Spec | [Oratorio Design](./oratorio-design.md) |

This document is the canonical frontend contract for the Oratorio surfaces in
DotCraft Desktop.

The native Oratorio view is the project board and compact status surface for
agent work. Detailed AppServer
conversation, approval decisions, plan inspection, diff/file/terminal/preview
views, model selection, stop controls, and general follow-up turns belong in
DotCraft Desktop. The Task detail Discussion may expose the narrow
Oratorio-owned `Ask agent` flow for Agent Discussion Turns.

---

## 1. Scope

In scope:

- the board header with Oratorio brand identity and the Settings entry;
- the Kanban board, filters, search, cards, drag-and-drop, and undo feedback;
- the Status-only Task Drawer;
- Settings for implemented local preferences, source status/configuration,
  runtime posture, isolation, and review policy;
- the DotCraft Desktop shell integration around the native surfaces;
- responsive layout, accessibility, and visual quality for those surfaces.

Out of scope:

- embedded Conversation, Approvals, Plan, Diff, Files, Terminal, or Preview tabs;
- embedded prompt composers, model pickers, or stop buttons;
- general-purpose chat with the AppServer thread;
- coming-soon top-level routes such as Sources, Agents, Rules, or Integrations;
- a full AppServer conversation or approval console inside the native Oratorio view;
- a separately distributed Oratorio desktop application.

---

## 2. Information Architecture

Default route:

```text
/projects/:workspaceId
```

Task drawer route:

```text
/projects/:workspaceId/tasks/:shortId
```

Task detail route:

```text
/projects/:workspaceId/tasks/:shortId/detail/:stage
```

Settings route:

```text
/settings/:section
```

Source configuration is provider-centric: the `github` and `gitlab` sections each
consolidate that provider's connection, project routing, and sync.

The normal board route has no dedicated navigation rail or vertical divider.
Settings lives in the board header action group beside board actions. The
Oratorio logo and product name live in the board header next to the `Kanban`
mode label.

If a feature does not have a real implemented contract, it is omitted rather
than shown as coming soon.

---

## 2.1 DotCraft Desktop shell

Oratorio uses the existing DotCraft Desktop window, navigation, and native
surface registry. It does not own a second Electron shell or browser product.

DotCraft Desktop owns window chrome, global navigation, diagnostics, and window
controls. Oratorio adds no product-specific titlebar. Product identity, New
Task, Settings, filters, and board actions remain in the native board header and
follow the active DotCraft theme.

Desktop service connection modes:

- `local` is the default mode. Desktop Main asks Hub to ensure the bundled
  Oratorio Server and keeps its endpoint and service bearer out of Renderer.
- `remote` is selected with an active saved DotCraft Stack. Desktop opens
  independent SSH tunnels for AppServer and Oratorio and reads both credentials
  only in Main memory. Direct Renderer URLs are not supported.
- SSH tunnel mode uses the system `ssh` binary, local SSH config/agent/key
  authentication, loopback-only `-L` forwarding, `BatchMode=yes`, and
  `ExitOnForwardFailure=yes`. Desktop validates the tunneled local HTTP origin
  with the same `/health` contract before treating it as connected.
- Renderer calls bounded typed IPC. Desktop Main derives HTTP and realtime
  endpoints, injects the bearer, and owns the WebSocket.
- Every Desktop backend restart or connection-mode change begins a new renderer
  backend session, even when the effective base URL is unchanged (for example,
  when local mode and an SSH tunnel both use `http://127.0.0.1:5087`). The
  renderer must immediately discard task, detail, source-status, and other
  backend-scoped state, stop the previous realtime stream, and prevent stale
  requests from restoring data from the previous session.
- Once the replacement backend reports `running`, the renderer must reconnect
  the realtime stream and reload the active board plus the currently selected
  closed-task view. Every successful realtime reconnect also reconciles the
  board from an HTTP snapshot so events missed while disconnected cannot leave
  stale cards. These snapshot reloads do not trigger external source sync.
- Remote connection failure does not silently fall back to local mode. The
  native error state offers retry and directs the user to Server settings.

### 2.2 DotCraft App Binding UX

DotCraft App Binding is surfaced as a compact connection status and consent
flow, not as an embedded DotCraft conversation surface.

Handoff behavior:

- DotCraft receives `dotcraft-service://oratorio/...` handoffs and validates the
  active Workspace before forwarding approval to Oratorio through typed IPC.
- Desktop Main injects the current local or tunneled AppServer endpoint and
  runtime identity. Neither AppServer nor Oratorio credentials enter Renderer.
- Local and remote service contexts both support App Binding. Changing
  context invalidates the previous Oratorio stream and cached service metadata.

The native surface may show App Binding connection state where an operator must
act on it. This state does not replace service health and does not imply a
specific thread binding.

The App Binding consent dialog must follow Oratorio modal and density language:

- connection consent shows the requesting DotCraft workspace/user, app identity,
  expiry, and a short statement that thread access remains a separate DotCraft
  selection;
- thread binding consent, when shown by policy, summarizes the requested thread,
  scopes, and tools with compact badges or rows;
- loading, error, cancel, and approving states remain visible and accessible;
- success, failure, and follow-up messages use the shared Oratorio notice/toast
  style.

### 2.3 Built-in surface registration

DotCraft ships an Oratorio built-in plugin descriptor that registers the board,
settings, and plugin detail surfaces. Its entry module selects Desktop-owned
native components; it does not ship a separately maintained extension UI.

The same native board, Task Drawer, Task Detail, settings, source write audit,
review, comment, draft, and run surfaces work in local and remote Stack modes.

---

## 3. Kanban Board

The board title lockup shows the Oratorio logo, a small `Kanban` label, and the
primary title `Oratorio`.

The board owns:

- search;
- repository and assignee filters;
- source and label advanced search qualifiers;
- new local task;
- refresh;
- a top-level view switcher with `Active`, `All`, `Cancelled`, and `Archived`;
- four Active Kanban columns: `todo`, `in_progress`, `in_review`, `done`;
- paged list views for `All`, `Cancelled`, and `Archived`;
- task cards;
- drag-and-drop operations;
- loading, empty, error, reconnect, and undo states.

Drag-and-drop operations are lifecycle actions, not arbitrary column edits. The
Active board supports dispatching `todo` cards to `in_progress`, requesting
changes from `in_review` back to `in_progress`, approving `in_review` cards to
`done`, and confirmed active-run cancellation from `in_progress` back to
`todo`. Run cancellation is available only for cards whose lifecycle state is
`dispatching` or `running`; failed cards that project to `in_progress` remain
non-cancellable by dragging. Confirmed cancellation interrupts the active
DotCraft run when one is attached, returns the card to `todo`, and does not show
an undo toast because the interrupt is an external side effect.

Column labels:

| TaskStatus | Label |
| --- | --- |
| `todo` | To do |
| `in_progress` | In progress |
| `in_review` | In review |
| `done` | Done |

`cancelled` remains a backend/API TaskStatus projection for rejected and
archived tasks, but it is not rendered as an Active board column. Closed work is
available through explicit list views:

| View | Shape | Source query |
| --- | --- | --- |
| `Active` | Kanban | Active lifecycle states only |
| `All` | List | `includeArchived=true`, newest updated first |
| `Cancelled` | List | `state=rejected`, newest updated first |
| `Archived` | List | `state=archived`, newest updated first |

Closed list views page results and automatically load the next page when the
user scrolls near the end while `nextCursor` is present. Changing view, search,
or filters resets the current list page.

Cards show compact board information only:

- micro-status dot;
- source chip;
- kind chip;
- title;
- one-line brief/summary preview;
- lifecycle/source/check badges;
- updated time.

Cards do not show full source body, raw agent output, worktree paths, or long
technical identifiers.

The Local Task create/edit form keeps task intent first, then routing metadata:

- title and description remain the primary fields;
- repository, labels, assignee, and base branch provide typed input plus
  quick-pick candidates when known data exists;
- `Base branch` is the optional source branch/base ref for task runs, not the
  generated work branch name;
- clicking `Create task` starts a short non-blocking celebration from the
  button position before the create request completes;
- successful task creation shows the actionable notice
  `New task "<title>" created. Click to view details.`;
- clicking that notice opens the created task in the Task Drawer;
- reduced-motion users receive the success notice without animated particles.

### 3.1 Card Visual Contract

Card content composes a small fixed set of element classes; each class has one
visual treatment and may not borrow another class's treatment:

| Element | Role | Treatment |
| --- | --- | --- |
| Source chip | Where the task lives (Local, GitHub repo, GitLab project) | Compact pill with provider icon at the shared chip-icon size |
| Kind chip | Work kind (PR, Issue, Local task) | Compact pill with kind icon at the same chip-icon size as the source chip |
| Status pill | Lifecycle/check state, shown only when it adds signal beyond the column | Themed pill following the status-pill modes below |
| Micro-status dot | Always-on per-card lifecycle indicator | Filled circle at the dot-indicator size token, colored by state |

Source chip and kind chip icons must render at the same optical weight. Both
use the `chip-icon` size token and the global lucide stroke width. Provider
chips for sources without a real glyph (such as Local tasks) must use the
designated Local source icon — never a degenerate one-pixel placeholder or a
shrunken variant.

Card header order, left to right: source chip, kind chip. Stable ShortId remains
available in routes and drawer/detail headers, but is not shown as a board-card
chip. The title sits on its own row beneath the chip row and never shares
horizontal space with chips. The micro-status dot lives on the title row's right
edge and never sits inside the chip row.

Status pills follow one of three visual modes by lifecycle category:

| Category | Examples | Treatment |
| --- | --- | --- |
| Success | `Approved`, `Passing` | Filled success-tint background, on-tint label, optional check icon |
| Attention | `Attention`, `Failed`, `Locked` | Outlined with attention or destructive border + matching icon, neutral surface |
| Neutral | `Discovered`, `Awaiting review`, `Pending` when not redundant | Outlined neutral border, neutral label |

A single card must not mix pill modes for the same logical category — for
example, an `Approved` filled pill next to a `Passing` outlined pill is a
regression. Status pills that share a card use the same mode.

The micro-status dot is the always-on per-card lifecycle indicator; the footer
status pill is shown only when it adds signal beyond the card's column. Kanban
card footers therefore hide lifecycle pills that merely restate the column —
`Discovered` in `To do`, `Awaiting review` in `In review`, and `Approved` in
`Done` — relying on the column, the colored dot, and the accent edge instead.
`Running` renders as an animated spinner (no text); `Dispatching`, `Failed`, and
the terminal `Rejected` / `Archived` keep their themed pills. The full lifecycle
pill — including `Discovered`, `Awaiting review`, and `Approved` — remains on the
status drawer and detail page, which are not column-grouped.

Card footers also hide review/check pills that do not add card-level triage
signal. `Not configured` is never shown on cards, and `Pending` is hidden when
the lifecycle state is already `Dispatching` or `Running`. Full check state
remains available in the detail page and status drawer where `oratorio/review`
has enough context.

---

## 4. Status Drawer

The Task Drawer has no tabs. It renders only Status.

Status defaults to a Problem / Result / Action / Stats hierarchy:

- **Problem** shows the source/local task brief (`BriefFields.summary`) or the
  source/local description. It must not fall back to agent-generated item
  summaries.
- **Result** shows compact review outputs such as review drafts,
  implementation drafts, follow-up drafts, and suggestion counts. Short result
  previews may be shown, but raw run/round markdown is not drawer content.
- **Action** shows the next operator action or the review decision block.
- **Stats** shows compact counts for review drafts, implementation drafts,
  follow-ups, source writes, and comments.

Status may show the latest run kind, attempt, and status only while a run is
active or failed/cancelled/timed out. It must not render a run progress bar: the
percentage duplicates the status label and the active-or-not signal, so it adds
no operator value. While a run is active, Status surfaces a live activity
indicator in the run section body (not as the section subtitle): a short
activity verb derived from the current AppServer item (e.g. Thinking, Running
command, Writing), optionally followed by a muted tail of the latest streamed
agent text. This is an ephemeral status feed, not an AppServer conversation
row — it renders the single current activity in a fixed-height body area that may
wrap and clamp the latest output to a few lines, is replaced in place as the run
streams, never persisted, and cleared the moment the run ends. The drawer must
not stack multiple items, render markdown, show roles/timestamps, or retain
history; the full conversation stays in DotCraft Desktop and the detail page. Successful run summaries are hidden
from the default drawer because the review draft or follow-up draft is the
operator-facing result. Status may also show board-safe actions such as
dispatch, retry, archive, reopen, edit local task, re-review PR, and copy task
id when backend gates allow them, plus a persistent `Open detail page` action in
the drawer overflow menu. For closed work — an `Approved` or `Rejected` task —
Status may promote `Archive` to an explicit `Action` section above any re-run or
re-review action, so filing completed work no longer requires the overflow menu.

Status must not show:

- AppServer conversation rows (the single-line live activity indicator above is
  not a conversation row and is permitted);
- AppServer approval cards or approval buttons;
- plan snapshots or plan todos;
- prompt composer, model selector, or stop button;
- embedded Diff, Files, Terminal, or Preview tabs.

The Task detail page, reached through `Open detail page`, may render a compact
Discussion composer with separate `Add comment` and `Ask agent` actions. `Add
comment` records internal operator feedback. `Ask agent` creates an Agent
Discussion Turn only when the backend reports an eligible completed AppServer
thread and no active Discussion Turn. The Status Drawer must not render this
composer; it may only show comment/discussion counts and route operators to the
detail page.

When a GitHub pull request's current head SHA differs from the latest
successful AppServer review analysis run, the Status Drawer and Task detail page
may expose a `Re-review PR` or `Review latest commit` action. This action calls
the Oratorio re-review endpoint directly; it must not be implemented by adding a
comment, creating an Agent Discussion Turn, or recording `requestChanges`.

If operators need execution detail, they use the Task detail page's Diagnostics
stage or DotCraft Desktop. A future Desktop handoff button may be added only
after a stable deep-link contract exists.

### 4.1 Drawer Section Hierarchy

Drawer sections use one of four section types. Each type has a fixed surface
treatment so a single glance identifies the section's role:

| Type | Purpose | Treatment |
| --- | --- | --- |
| Action | The primary operator action (e.g. `Review decision`, `Dispatch round`) | Neutral elevated surface, neutral inverse primary CTA inside; nothing else competes with the CTA |
| State | Current lifecycle / active-or-failed run status (e.g. `Awaiting review`, `mock attempt N`) | Left-edge state-color stripe, status icon, status label |
| Info | Read-only narrative (e.g. `Problem`) | Plain surface, no accent, body text |
| Stats | Compact counts of related artifacts (e.g. `Review artifacts`) | Plain surface, individual counts |

The primary `Action` section must remain reachable when the drawer overflows.
The drawer either pins the primary `Action` section to the bottom of the
scroll container or guarantees the primary CTA stays visible. Secondary
actions like `Add comment` may scroll with content.

`Stats` sections de-emphasise zero counts: the digit `0` and its label render
in a muted text color and one weight lighter than active counts. Non-zero
counts render with the standard text color and may take on a category accent
when relevant (e.g. unresolved comments tint with attention).

Drawer section header icons follow the same `chip-icon` size token as card
chips. Stacking identical neutral surfaces with identical icon weight for
sections of different types is a regression — each section type must read
distinctly.

---

## 5. Task Detail Page

The Task detail page is the deeper review surface reached from the drawer's
`Open detail page` action and from any deep link. It is in scope for the
native Oratorio view; deeper agent execution surfaces remain in DotCraft
Desktop.

The default detail-page information hierarchy is Problem / Result / Decision:

- **Problem** renders the source issue/PR/local task body and compact source
  metadata.
- **Result** renders operator-facing agent outputs: review drafts,
  suggestions, implementation drafts, and follow-up drafts. Follow-up drafts
  from PR/MR review display the inherited review-target branch when the draft
  leaves its branch empty and targets the same repository.
- **Decision** renders the current decision composer and a compact decision
  history.

Execution process data (run attempts, round history, timeline events,
thread/worktree identifiers, raw round summaries, and agent status markdown) is
Diagnostics content. Diagnostics is shown by default only for active,
failed/cancelled/timed-out, or explicitly deep-linked runs. Normal
awaiting-review, approved, and rejected workflows must not surface agent
process markdown in their default panels.

### 5.1 Routing

The detail route is `/projects/:workspaceId/tasks/:shortId/detail/:stage`,
where `:stage` is one of `intake`, `analysis`, `review`, `decision`, or
`closed`.

The stable route segment is `analysis`, while the user-facing stage label is
`Diagnostics`. `defaultReviewStage(item, run)` routes to
Diagnostics only when the item/run is active or needs troubleshooting; a
succeeded run does not by itself make Diagnostics the default stage.

- The selected stage must match the URL `:stage` segment exactly. Mounting
  the detail page must not silently override the URL with
  `defaultReviewStage(item)`.
- When the route has no `:stage` segment, the renderer falls back to
  `defaultReviewStage(item)` and replaces the URL with the resolved stage.
- Changing tabs inside the page updates the URL; refreshing or sharing the
  URL lands on the same stage.

### 5.2 Header

The detail page header is a single-title surface:

- the breadcrumb row contains source chip, kind chip, ShortId, repo path,
  and the current status pill — never the title;
- the page title renders once as an `H1` beneath the breadcrumb;
- the metadata row beneath the title shows branch, assignee, round, and last
  sync time as compact icon + label pairs at the `metadata-icon` size.

Duplicating the task title inside the breadcrumb is a regression.

### 5.3 Sub-pill row

The chip row in the detail page header carries four conceptual classes; each
class renders with a distinct visual treatment:

| Class | Examples | Treatment |
| --- | --- | --- |
| Source | `Local`, GitHub icon + `dotcraft/server`, GitLab icon + project | Provider chip with provider icon at `chip-icon` size |
| Repo | `dotcraft/server` | Plain text chip in monospace; rendered only when the source resolves to a multi-repo provider |
| ID | `task:seed-auth-review` | Monospace chip, no icon, neutral surface |
| Status | `Attention`, `In review`, `Awaiting review` | Status pill following §3.1 pill modes |

The four classes must not collapse into one visually identical chip style.

### 5.4 Review stage stepper

The review stage stepper is the page's primary status visualization. The normal
path shows Intake, Review, Decision, and Closed. Diagnostics appears only when
the current item/run needs attention or when the active URL is the `analysis`
stage.

- **Completed** stages render as a filled node in the success accent with a
  centered checkmark glyph.
- **Current** stage renders as a filled node in the primary accent with a
  soft outer halo or pulse; the surrounding ring is visibly thicker than the
  completed node ring.
- **Pending** stages render as an outlined empty node in the muted/neutral
  color, no fill.
- The **connector line** fills with the success accent up to the current
  stage; pending segments stay neutral.

Each node shows the stage name and a per-stage status sublabel
(e.g. `Succeeded`, `In progress`, `Open`, `Pending`). Diagnostics is an
on-demand diagnostic node, not a mandatory lifecycle step for successful review
work.

### 5.5 Decision actions

The detail page's primary decision actions are `Approve`, `Request changes`,
and `Reject`. They follow a strict visual hierarchy:

- `Request changes` is the secondary feedback action — outlined neutral
  button, occupying the first row so feedback text and the feedback action read
  together.
- `Approve` is the primary affirmative — neutral inverse primary CTA in the
  lower affirmative row.
- `Reject` is the terminal destructive action — outlined destructive style,
  paired with `Approve` in the lower row but visually separated through
  destructive color, spacing, and copy so it never competes with the primary
  affirmative action.

The decision action panel is sticky to the bottom of the detail page when
the page content overflows the viewport. Scrolling never hides the primary
decision actions.

### 5.6 Empty states

Detail page empty states (no decisions, no comments, no follow-ups, no
timeline) render with a muted lucide icon at the `empty-state-icon` size
above the empty-state copy. Plain-text empty states without an icon are a
regression for the detail page.

### 5.7 Review finding resolution

The review stage renders published review findings (design §5.7). Each finding
shows its resolution state:

- open findings render at full emphasis;
- resolved findings render visibly de-emphasized (muted surface, reduced
  emphasis) with a resolution chip showing the kind (`Fixed` or `Dismissed`),
  the resolver (`agent` or `operator`), and the optional note;
- finding tallies on the review surface count open findings only; resolved
  findings stay visible for audit and are not silently removed.

When backend gates allow, the detail page exposes an operator `Resolve` control
on an open finding and a `Reopen` control on a resolved finding; resolving
prompts for the `Fixed`/`Dismissed` kind and an optional note. These controls
are detail-page only. The Status Drawer must not render resolve/reopen controls;
it may only surface open-finding counts and route operators to the detail page,
consistent with the Discussion composer rule in §4.

---

## 6. Settings

Settings is the only non-board navigation destination.

Source settings are organized per provider. GitHub and GitLab each have a single
Settings section, listed as a distinct top-level navigation entry, that
consolidates top to bottom: Connection (endpoint, identity, write enablement, and
write-only credential inputs), Project routing (repository/project → DotCraft
workspace mappings, GitHub installation profiles, and GitLab per-project profiles),
and Sync (read status, a primary `Sync now`, scheduled incremental sync with next
run, `Full repair` and failed-sync retry in an overflow menu, and provider-local
background failures). There is no separate Sources, Credentials, or Projects
destination.

Allowed Settings content:

- Repository cards that combine GitHub repository identity, DotCraft workspace
  path, and workspace/AppServer health. Workspace bindings are selected only
  from local Projects already registered with DotCraft Desktop; Chat workspaces,
  remote Projects, secondary folders, and arbitrary folder picking are excluded;
- GitHub installation profiles grouped by GitHub instance and owner inside
  Project routing, with detected/manual status, retry detection, and manual
  installation ID override;
- GitLab project profiles inside Project routing, keyed by canonical
  `gitlab:<instance>/<group[/subgroup]/project>` project keys, with token kind,
  token, webhook secret, signing token, and missing-profile status;
- A provider page header per source that shows read/write/webhook health as a
  compact status line, a primary `Sync now`, scheduled incremental sync with next
  run, and `Full repair` plus failed-sync retry in an overflow menu;
- Credentials presence and write-only password-style inputs with show/hide
  controls that never echo stored plaintext;
- Agents configuration for DotCraft bridge status, AppServer endpoint
  discovery, Hub discovery, approval policy, and run timeout;
- Worktree configuration for managed worktrees, the managed root, and the
  branch prefix;
- implementation auto-dispatch policy and delivery behavior;
- concurrency, retry, stall, and cleanup policy remain Server-owned runtime
  configuration and are not exposed in Desktop Settings;
- Review configuration for PR Auto Review triggers and Review Draft
  auto-publish policy.

Settings must never render stored token, webhook secret, private key, or private
key path values. It may render write-only inputs whose typed values are cleared
after the save response. Empty secret inputs leave existing values unchanged.
Auto-start command and process argument inputs are not Settings content.
GitHub installation IDs appear only as owner profiles in Project routing.
GitLab instance connection settings (endpoint, read sync, write enablement,
webhook verification) live in the GitLab section's Connection; per-project
GitLab tokens, webhook secrets, and signing tokens appear only on that section's
project-routing cards. Changing the GitLab endpoint host clears project profiles
from the draft with restart/impact copy.

Configuration saves that require process restart show a pending restart banner at
the top of Settings. Desktop builds offer a restart button through the desktop
bridge when available; test or preview contexts without the bridge show the
manual restart requirement. Saving settings must not use a native confirmation
dialog.

Asynchronous Settings failures use DotCraft Desktop's shared toast system. Initial
configuration loading, automatic saves, provider/project synchronization, and
sync-schedule saves do not insert transient error rows into the form. Failed saves
and explicit synchronization actions offer a toast-level retry, and repeated
failures in this surface replace the preceding Settings error toast. Local input
validation such as duplicate labels, malformed URLs, and numeric range errors
remains next to the affected field. Persistent service-unavailable, remote
read-only, restart-required, and provider health states remain in the page.

Remote backend mode treats server-admin configuration as read-only even when a
tunnel makes the backend appear loopback-writable. Settings may show remote
configuration, diagnostics, workspace inventory, and source status, but save
controls for server configuration, secrets, workspace routing, worktree policy,
and automation policy are disabled with copy that directs operators to manage
the server through `.env`, environment variables, or a server-side overlay.
Board, task, review, comment, and decision actions remain enabled when their API
capabilities are available.

Repository workspace paths shown in remote mode are remote host or container
paths. Remote mode must not write local filesystem paths into remote workspace
mappings.

Settings actions use one compact visual language. Group-level actions such as
`Start server`, `Discard`, and `Save` live in the Settings group header action
area. Row controls are reserved for status, form controls, toggles, and
read-only values. Page-level refresh uses an icon-only action with
accessible label/title text, matching the board toolbar instead of rendering a
large text button.
Settings row controls must not expose browser-native select menus or native
number spinners. Single-choice and numeric controls use DotCraft Desktop
components with accessible labels, keyboard navigation, visible focus, and
disabled, highlighted, and selected states. Display labels may be
human-readable, but configuration values, API payloads, and the Configuration
Overlay shape remain unchanged.
The DotCraft bridge `Start server` action is visible whenever the bridge
status is not connected or any configured workspace inventory row is not
connected.

Review Settings manage repository allowlists with compact cards rather than
per-repository switches. Each allowlist card shows the included repository
count, selected repositories, row-level remove actions, and a `Manage` action.
`Manage` opens a searchable checkbox dialog sourced only from configured source
projects. Dialog saves update the Settings draft only; the page-level
`Save` action still commits configuration to the backend. The Review Settings
dialog must not invent project metadata such as per-project last-indexed times
unless that data is available in the Settings API.

Repository Settings must not expose a fallback workspace. Every AppServer run
must resolve its workspace from the item repository's configured mapping. Add
Project starts with an empty source project field and defaults its binding to the
foreground local DotCraft Project. If there are no registered local Projects,
the dialog explains that a Workspace must first be opened in DotCraft and cannot
submit. A saved binding that is no longer registered remains visible as an
unavailable value until the user explicitly rebinds it.

---

## 7. DotCraft design system

Oratorio uses the shared [DotCraft Desktop design system](../../architecture/DESIGN.md).
It does not define a separate palette, typography scale, theme, icon library,
surface recipe, control vocabulary, or scrollbar treatment.

The following Oratorio-specific layout contracts remain:

- Board mode does not reserve a product-specific rail column.
- The Oratorio logo and product name remain visible in the board header.
- Settings remains in the board header action group.
- Cards and drawers use compact, scan-friendly rows.
- Dense actions have accessible labels.
- Text and actions remain reachable at supported Desktop widths.

Lifecycle, source, and review states use DotCraft semantic tokens. The status
pill catalogue in §3.1, drawer section catalogue in §4.1, and stepper behavior
in §5.4 define meaning and hierarchy, not independent visual tokens.

---

## 8. Validation

A frontend change is acceptance-ready when:

- it matches this document and does not revive out-of-scope embedded agent
  surfaces;
- `cd desktop && npm run build` passes;
- `cd desktop && npm test` passes when tests are affected;
- the board renders without framework overlays or console errors;
- the board header shows logo plus `Oratorio`, and the old rail divider is not
  present;
- the Kanban title and filter toolbar are visually aligned;
- opening a task shows a Status-only drawer;
- the surface follows the shared DotCraft design system and accessibility
  contract;
- the Task detail page renders the stage named by the URL `:stage` segment
  and does not always render the Decision panel;
- the Task detail page title appears once below the breadcrumb, never
  inside it;
- decision actions follow the §5.5 hierarchy and the decision panel is
  sticky to the bottom when the page overflows;
- the drawer primary action remains reachable when content overflows;
- detail page empty states render with the §5.6 empty-state icon.
