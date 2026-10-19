# Oratorio Native Surfaces Specification

| Field | Value |
| --- | --- |
| Version | 0.9.1 |
| Status | Living |
| Date | 2026-10-19 |
| Parent Spec | [Oratorio Design](./oratorio-design.md) |

This document is the canonical frontend contract for the Oratorio surfaces in
DotCraft Desktop.

The native Oratorio view is the project board and task surface for agent work.
Detailed conversation, approval decisions, plan inspection, diff, file,
terminal and preview views, model selection, stop controls and general
follow-up turns belong in DotCraft Desktop. The task Discussion may offer the
narrow Oratorio-owned `Ask Oratorio` flow for Agent Discussion Turns.

Clients render server-owned lifecycle, capability and validation state and
never invent transitions ([Oratorio Design §4](oratorio-design.md#4-domain-and-api-contract)).
The shared Desktop design system owns palette, typography, themes, icons,
controls, surfaces and accessibility; this spec defines Oratorio's information
hierarchy using those primitives.

## 1. Scope

In scope:

- the board header with Oratorio brand identity and the Settings entry;
- the Kanban board, filters, search, cards, drag-and-drop and undo feedback;
- the Task Drawer and the Task detail page;
- Settings for source status and configuration, runtime posture, isolation and
  review policy;
- the DotCraft Desktop shell integration around these surfaces.

Out of scope:

- embedded Conversation, Approvals, Plan, Diff, Files, Terminal or Preview tabs;
- embedded prompt composers, model pickers or stop buttons;
- general-purpose chat with the AppServer thread;
- destinations without an implemented contract (no coming-soon entries);
- a separately distributed Oratorio desktop application or extension UI.

## 2. Information Architecture and Shell

The board, a task in the drawer, a task's full page and each Settings section
have their own address within the project, so refreshing or sharing lands on
the same view. Settings is the only destination besides the board.

Oratorio uses the existing DotCraft Desktop window, navigation and native
surface registry; one Desktop-owned native surface supplies the board, drawer,
detail page and Settings in both local and remote modes. Desktop owns window
chrome, global navigation, diagnostics and window controls. The board has no
navigation rail; product identity, New Task, Settings, filters and board
actions stay in the board header and follow the active DotCraft theme.

### 2.1 Service connection

- **Local** is the default. Desktop's main process asks Hub to ensure the
  bundled Oratorio Server and keeps its address and credential out of the
  renderer.
- **Remote** is used with an active saved DotCraft Stack. Desktop opens
  separate SSH tunnels for AppServer and Oratorio and holds both credentials
  only in the main process.
- Tunnels use the system SSH client with the user's own SSH configuration,
  agent and keys, forward to loopback only, never prompt, and fail when
  forwarding fails. Desktop checks the tunneled service's health before
  treating it as connected.
- The renderer talks only to the main process, which builds the requests,
  adds the credential and owns the realtime stream. The renderer never
  connects to a URL directly.
- Every backend restart or connection-mode change starts a new renderer
  session, even when the service address is unchanged. The renderer
  immediately drops backend state, stops the previous realtime stream, and
  ignores late answers from the previous session.
- Once the new backend is running, the renderer reconnects the stream and
  reloads the active board and the selected closed-task view. Every successful
  reconnect also reloads the board snapshot so events missed while
  disconnected cannot leave stale cards. These reloads never trigger a source
  sync.
- A remote connection failure never falls back to local mode. The error state
  offers retry and points to Server settings.

### 2.2 App Binding

App Binding appears as a compact connection status and consent flow, never as
an embedded conversation.

- DotCraft receives Oratorio app-binding links, checks the active Workspace,
  then forwards the approval to Oratorio through the main process.
- The main process supplies the current local or tunneled AppServer address
  and runtime identity. Neither AppServer nor Oratorio credentials reach the
  renderer.
- Local and remote services both support App Binding. Switching service drops
  the previous Oratorio stream and cached service metadata.
- App Binding state may be shown where the operator must act on it. It does
  not replace service health and does not imply a binding to a specific
  thread.
- Connection consent shows the requesting DotCraft workspace and user, the
  app, the expiry, and that thread access remains a separate DotCraft choice.
  Thread binding consent, when policy shows it, summarizes the thread, scopes
  and tools. Loading, error, cancel and approving states stay visible.

## 3. Kanban Board

The board offers:

- search, repository and assignee filters, and `source:` and `label:` search
  qualifiers;
- new local task and refresh;
- a view switcher with `Active`, `All`, `Cancelled` and `Archived`;
- four Active columns: To do, In progress, In review and Done;
- task cards and drag-and-drop;
- loading, empty, error, reconnect and undo states.

Drag-and-drop performs lifecycle actions, not free column edits: dispatching a
To do card to In progress, requesting changes by moving an In review card back
to In progress, approving an In review card into Done, and a confirmed run
cancellation from In progress back to To do. Cancellation is available only
while a run is dispatching or running; a failed card in In progress cannot be
dragged back. A confirmed cancellation interrupts the attached DotCraft run,
returns the card to To do and shows no undo, because the interrupt has already
happened.

Rejected and archived tasks belong to the Cancelled task status, which has no
Active column. Closed work is reached through list views:

| View | Shape | Contents |
| --- | --- | --- |
| `Active` | Kanban | Active lifecycle states |
| `All` | List | Every task including archived, newest update first |
| `Cancelled` | List | Rejected tasks, newest update first |
| `Archived` | List | Archived tasks, newest update first |

List views page their results and load the next page when the user scrolls
near the end. Changing view, search or filters starts again from the first
page.

The Local Task create and edit form puts task intent first, then routing:

- title and description are the primary fields;
- repository is a closed selector; assignee and base branch offer known values
  but accept new ones;
- labels are added and removed as pills with suggestions, never a
  comma-separated field; they are trimmed, empty values ignored and duplicates
  removed ignoring case while keeping the first spelling;
- base branch is the branch runs start from, not the generated work branch;
  leaving it empty means the repository default;
- the last used repository is preselected while still available, and a single
  available repository is selected automatically;
- success shows a notice that opens the new task in the drawer.

### 3.1 Cards

Cards show compact board information only: the status dot, source and kind
chips, the title, a one-line summary, labels, a neutral pill for a closed or
merged source, and the update time. Cards never show the full source body, raw
agent output, worktree paths or long technical identifiers.

The kind chip shows the issue, pull request or merge request number (GitLab's
`!` number for a merge request), while internal identifiers stay in addresses
and detail views. A source without a provider glyph uses the Local source icon.
The source chip, list rows and filter options name the project by its path
without provider prefix or public host; a path deeper than two segments shows
its last two, with the full path as a tooltip. The detail page's Source shows
the full path, including a self-hosted instance.

The column already says where a task is, so the dot tells the user what
Oratorio's latest result means for them, and colour lives only in the dot:

- **In progress:** blue while a run is queued or running, red when it failed.
- **In review:** from Oratorio's latest result, which stays until the next run
  produces a new one:
  - green when the latest review found no issues, or the latest
    implementation was delivered as a pull or merge request;
  - yellow when the latest review has suggestions, the latest implementation
    is ready for the user to look at or deliver, or follow-up tasks are
    proposed and waiting for a decision; pending follow-ups win over the rest;
  - no dot when there is no result.
- **To do** and **Done:** no dot.

The dot's tooltip names its meaning. Cards have no status pills. The full
lifecycle and check state live in the drawer and the detail page.

## 4. Task Drawer

Opening a task on the board opens the Task Drawer: the task page of §5 itself,
in a drawer presentation over the right edge of the board. Whatever the page
offers, the drawer offers, so an operator can triage, decide and comment
without leaving the board. The drawer differs from the page only in layout:

- it floats over the board and closes with its close button or Escape;
- its toolbar keeps the status menu, `Open on GitHub` / `Open on GitLab`,
  `Open thread` and the state's action, and ends with `Full details`, which
  opens the full page, and close. It drops the back button and the source
  text;
- the rail's properties (§5.4) sit as compact rows between the description and
  Activity.

While the drawer is open, board refreshes keep the loaded task on screen and
reload it only when it changed, so live activity never blanks it. Action
success and failure use the DotCraft global toast host and never insert banners
into the page.

The saved drawer selection is restored once per board mount, waiting for the
task list to load when necessary. While waiting, state reporting preserves the
saved task identity. Selecting another task, creating a task or closing the
drawer supersedes restoration; subsequent list and detail updates preserve the
operator's current selection or closed drawer.

A running run's Activity line carries a live indicator: a short verb for what
the agent is doing, optionally followed by its latest streamed text. It is an
ephemeral status line, not a conversation row: replaced in place as the run
streams, never stored, and cleared when the run ends.

The Discussion composer closes Activity with separate `Add comment` and `Ask
Oratorio` actions. `Add comment` records internal operator feedback. `Ask
Oratorio` starts an Agent Discussion Turn only when the backend reports an
eligible completed thread and no active Discussion Turn. Agent replies are
signed `Oratorio`, the operator's own comments carry their Profile avatar, and
comments imported from the provider keep their author. The composer follows
Desktop's line comment card. Enter adds a comment and Shift+Enter starts a new
line; `Ask Oratorio` is only ever a button.

When a pull or merge request's head differs from the one the latest successful
review analyzed, the toolbar action becomes `Review new revision`. It requests
a re-review directly; it is never a comment, a Discussion Turn or a request for
changes.

Neither the drawer nor the page renders conversation rows, approval cards,
plan snapshots, a prompt composer, a model selector, or embedded Diff, Files,
Terminal or Preview tabs. Execution detail lives in the task's DotCraft thread
(`Open thread`).

## 5. Task Detail Page

The Task detail page is the task's full review surface, with its own address,
reached from the drawer's `Full details` action and from any link.

The page is laid out like a pull request: a toolbar, then a main column with
the task and its Activity, and a narrow rail of properties. It has no tabs or
stages; everything that happened to the task reads top to bottom. It shows only
what an operator acts on. Execution internals — workspace and worktree paths,
thread and turn identifiers, worktree state, run attempts, round history,
system timeline events, the source snapshot's identity and head commit — are
not shown; the DotCraft thread is where execution is inspected.

Colour lives on icons and dots, not on pills: the page renders no state badge,
no chips, and no severity or draft-status pills.

### 5.1 Toolbar

One row, left to right:

- a back button;
- the status menu: the state's icon and name. When the task can be archived or
  reopened, it opens a menu with `Archive`, or `Reopen` once archived; this is
  how a task is closed. Otherwise the state is plain text;
- the provider glyph, the project name per §3.1 and the source number, or the
  short identity of a local task;
- `Open on GitHub` / `Open on GitLab` (when the task has a source link) and
  `Open thread` (disabled until a thread exists);
- one primary action chosen by state:
  - to do: the first start action the task allows (`Implement`,
    `Auto PR / MR`, `Review only`) as a split button holding the others;
  - queued or running: a confirmed `Cancel run`;
  - failed or timed out: `Retry`;
  - awaiting review: `Review` (§5.4);
  - approved or rejected with a newer source revision: `Review new revision`.

### 5.2 Main column

The title, the source branch for a pull or merge request, and the source or
local task body as Markdown. Then **Activity**, in time order. Only decisions
(approve green, request changes red), failed runs (red) and failed source
writes (warning) colour their icon. The entries are:

- each Oratorio run like a CI check: its progress or outcome, purpose
  (`Review`, `Implementation`), status and time. A running run adds its live
  activity; a finished one its summary; a failed one the server's message. A
  run sits before the drafts it produced;
- drafts as cards: the review draft card (§5.5), implementation drafts and
  follow-up drafts. A card's header carries its title, counts, state and
  actions: `Deliver` for an implementation draft; `Discard`, `Save` and
  `Create local task` for a follow-up draft. Follow-up drafts from a pull or
  merge request review show the inherited review-target branch when the draft
  leaves its branch empty and targets the same repository. Discarded drafts
  are not shown;
- comments (§4);
- decisions as events (approved, requested changes, rejected, reopened, asked
  for a new review), quoting the feedback when there is any;
- each failed source write with the server's message and `Retry`, except a
  failed review publication, which lives on its review draft card;
- a pending indicator while a Discussion Turn is pending or running.

The Discussion composer (§4) closes the column. A task with nothing in its
history renders no Activity entries above the composer.

### 5.3 Rail

Short sections: `Oratorio` (the latest run's purpose and status, or not
started), `Changes` (the latest implementation draft's files), `Assignee`,
`Labels`, and `Source` (the full project path per §3.1, the relative sync time
and a refresh action). Sections with no content are omitted. On narrow layouts
the rail follows the main column.

### 5.4 Review decision

`Review` opens a popover anchored to the button: `Approve`, `Request changes`
and `Reject` as one choice, an optional comment (required for `Request
changes`), and submit. The decision then appears in Activity, and the status
menu shows the new state. A failed submission keeps the popover open with the
operator's comment, and the composer keeps its text the same way.

### 5.5 Review draft card

The review draft card shows the review Oratorio published to the provider
([Oratorio Design §6.1](oratorio-design.md#61-structured-review-suggestions)):

- its summary and counts;
- its publication state: published, or publication failed with the server's
  reason;
- only when publication failed, `Retry` and `Discard`. There is no `Publish`
  and no editing: publication is automatic;
- its findings, resolved under [Oratorio
  Design §6.4](oratorio-design.md#64-review-finding-resolution):
  - an open finding shows its severity, title, `path:line`, its body as
    Markdown and any suggested change as an inline diff, and once the review
    is published `Dismiss` and `Mark fixed`;
  - a resolved finding collapses to one muted line with its title, the
    resolution (`Fixed` or `Dismissed`) and `Reopen`;
  - the counts include open findings only; resolved findings stay visible for
    audit.

## 6. Settings

GitHub and GitLab each have one top-level Settings section holding top to
bottom: Connection (endpoint, identity, write enablement and write-only
credential inputs), Project routing (repository or project to DotCraft
workspace mappings, GitHub owner installations and GitLab per-project access),
and Sync (read, write and webhook health, a primary `Sync now`, scheduled
incremental sync with its next run, `Full repair` and failed-sync retry, and
provider background failures). There is no separate Sources, Credentials or
Projects destination.

Settings content:

- repository cards combining the repository, its DotCraft workspace and the
  workspace and AppServer health. Workspaces are chosen only from local
  Projects registered with DotCraft Desktop; Chat workspaces, remote Projects,
  secondary folders and arbitrary folders are excluded;
- GitHub installations listed per owner, showing detected or manual status,
  with a manual installation override;
- an Access group on each project's page: for GitLab, the project's token (its
  kind and whether it is set), webhook secret and signing token, each shown as
  configured or not set; for GitHub, the installation covering the
  repository's owner. The token kind is chosen from project, personal or group
  access token together with a new token, never typed as free text;
- Agents: DotCraft bridge status, AppServer and Hub discovery, approval policy
  and run timeout. Approval offers approve automatically or deny and continue,
  defaulting to deny and continue; a server run never follows a desktop
  workspace default, and any other stored approval value reads as deny and
  continue;
- Worktrees: managed worktrees, the managed root and the branch prefix;
- implementation auto-dispatch policy and delivery;
- Review: repository Auto Review.

Concurrency, retry, stall and cleanup policy stay server-owned and are not in
Desktop Settings. Review publication has no setting.

Settings never shows stored tokens, webhook secrets, private keys or private
key paths. Write-only inputs are cleared after the save answer, and an empty
secret input leaves the stored value unchanged. Auto-start command and process
argument inputs are not Settings content. Changing the GitLab endpoint host
clears project access from the draft and explains the restart and impact.

Saves that need a process restart show a pending restart banner. Desktop
offers a restart action when it can restart the server; otherwise it states
that a manual restart is needed.

Until the configuration first loads, Settings shows a loading state instead of
default values, and a load failure replaces the form with a page-level retry;
nothing is editable before the server has answered. Other asynchronous
failures use DotCraft Desktop's shared toasts and never insert error rows into
the form; failed saves and explicit synchronization offer a toast retry. The
server's answer to a save is authoritative: Settings shows what the server
stored, keeping only edits made after the request was sent. Leaving Settings
saves edits still waiting for their save delay instead of dropping them. Local
validation stays next to the field. Persistent service-unavailable, remote
read-only, restart-required and provider health states stay in the page.

In remote mode, server administration is read-only even when the tunnel makes
the backend look locally writable. Settings may show remote configuration,
diagnostics, workspace inventory and source status, but saving server
configuration, secrets, workspace routing, worktree policy and automation
policy is disabled, directing operators to manage the server through its own
configuration. Board, task, review, comment and decision actions stay
available when the backend allows them. Workspace paths shown in remote mode
are paths on the remote host or container, and remote mode never writes local
paths into remote workspace mappings.

Display labels may be human-readable while stored values stay unchanged. The
DotCraft bridge `Start server` action is visible whenever the bridge is not
connected or any configured workspace is not connected.

Repository lists such as Auto Review are edited through a searchable
checklist of configured source projects only; it updates the Settings draft,
and the page's save still commits it. List pills and choices show short
project paths, expanding only as far as needed to be distinct, with the
provider, instance and full path available; stored values keep the canonical
project keys.

Repository Settings never offer a fallback workspace; every AppServer run
resolves its workspace from the item's repository mapping. A saved binding that
is no longer registered stays visible as unavailable until the user rebinds or
removes it, and it never blocks unrelated saves. Removing a source project is
one atomic change: it removes the provider's synchronized project, its
workspace route, and its entries in the Auto Review and Implementation
Follow-up lists. Task, run and audit history stays, and shared provider
installations and credentials are kept.

### 6.1 Connect a source

Settings offers one guided path for a new source beside the per-field pages,
as a full-width stepped view inside Oratorio settings, never a modal. Two
entry points open it: the board's "no source connected" state, shown while no
provider has credentials, installations or projects and no tasks exist; and a
`Connect a source` action on the Settings root. It is the only way to add a
project: running it again adds another project and reuses an already
configured GitHub App.

The five steps are:

1. **Source**: provider, endpoint and credentials — GitHub App ID plus private
   key or key path, or a GitLab token kind and token. An already configured
   GitHub App is shown as configured with a replace option.
2. **Project**: `owner/repository` or `group/project`, validated and checked
   against configured projects. GitHub shows an optional installation ID whose
   absence means server-side detection.
3. **Workspace**: a choice among local DotCraft Projects, defaulting to the
   foreground Project. With none registered the step explains that a Workspace
   must be opened first and cannot advance.
4. **Automation**: sync schedule (off, 15 minutes, hourly or custom minutes),
   automatic review and provider writes, defaulting for a local Desktop to 15
   minutes, review on, writes off.
5. **Connect**: a read-only summary with a change action per row and the
   single primary `Connect and sync`.

The wizard never shows project access records or installations as such. For
GitLab it stores the token as the project's access, keyed by its path; for
GitHub it records an owner installation only when an ID is typed, and
otherwise, with no installation known for the owner, requests server-side
detection. Connecting the same project again updates it instead of adding
duplicates. Connect commits one atomic configuration change, saves the sync
schedule, reports the saved configuration, then starts a sync for the new
project and follows it. A finished sync with no failed project confirms read
access with imported counts. A rejected save shows the server's reason and
states that nothing was saved. Once the configuration is saved, a failed
schedule save, a failed or partly failed sync, or a detection warning for the
new owner shows the failure, states that the configuration is saved, and
offers a way back to the step that owns the fix. Retry resumes after the
saved configuration and saves again only if the user changed the connection.
A sync still running after the waiting window reports that the first sync
continues in the background. Remote read-only mode disables every field and
the primary action and explains why.
