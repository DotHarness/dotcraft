# Oratorio Native Surfaces Specification

| Field | Value |
| --- | --- |
| Version | 0.8.10 |
| Status | Living |
| Date | 2026-10-17 |
| Parent Spec | [Oratorio Design](./oratorio-design.md) |

This document is the canonical frontend contract for the Oratorio surfaces in
DotCraft Desktop.

The native Oratorio view is the project board and task surface for agent work.
Detailed conversation, approval decisions, plan inspection, diff, file,
terminal and preview views, model selection, stop controls and general
follow-up turns belong in DotCraft Desktop. The task Discussion may offer the
narrow Oratorio-owned `Ask Oratorio` flow for Agent Discussion Turns.

## 1. Scope

In scope:

- the board header with Oratorio brand identity and the Settings entry;
- the Kanban board, filters, search, cards, drag-and-drop and undo feedback;
- the Task Drawer and the Task detail page;
- Settings for local preferences, source status and configuration, runtime
  posture, isolation and review policy;
- the DotCraft Desktop shell integration around these surfaces;
- responsive layout, accessibility and visual quality.

Out of scope:

- embedded Conversation, Approvals, Plan, Diff, Files, Terminal or Preview tabs;
- embedded prompt composers, model pickers or stop buttons;
- general-purpose chat with the AppServer thread;
- coming-soon destinations such as Sources, Agents, Rules or Integrations;
- a full conversation or approval console inside the Oratorio view;
- a separately distributed Oratorio desktop application.

## 2. Information Architecture

The board, a task in the drawer, a task's full page and each Settings section
have their own address within the project, so refreshing or sharing lands on
the same view. Source settings are per provider: GitHub and GitLab each have
one section holding that provider's connection, project routing and sync.

The board has no navigation rail or vertical divider. Settings lives in the
board header's action group beside the board actions. The Oratorio logo and
product name sit in the board header next to the `Kanban` mode label.

A feature without a real implemented contract is omitted rather than shown as
coming soon.

## 2.1 DotCraft Desktop shell

Oratorio uses the existing DotCraft Desktop window, navigation and native
surface registry. It owns no second shell or browser product.

DotCraft Desktop owns window chrome, global navigation, diagnostics and window
controls. Oratorio adds no titlebar of its own. Product identity, New Task,
Settings, filters and board actions stay in the board header and follow the
active DotCraft theme.

Service connection modes:

- **Local** is the default. Desktop's main process asks Hub to ensure the
  bundled Oratorio Server and keeps its address and credential out of the
  renderer.
- **Remote** is used with an active saved DotCraft Stack. Desktop opens
  separate SSH tunnels for AppServer and Oratorio and holds both credentials
  only in the main process. The renderer never connects to a URL directly.
- Tunnels use the system SSH client with the user's own SSH configuration,
  agent and keys, forward to loopback only, never prompt, and fail when
  forwarding fails. Desktop checks the tunneled service's health before
  treating it as connected.
- The renderer talks only to the main process, which builds the requests,
  adds the credential and owns the realtime stream.
- Every backend restart or connection-mode change starts a new renderer
  session, even when the service address is unchanged. The renderer
  immediately drops task, detail, source-status and other backend state, stops
  the previous realtime stream, and ignores late answers from the previous
  session.
- Once the new backend is running, the renderer reconnects the stream and
  reloads the active board and the selected closed-task view. Every successful
  reconnect also reloads the board snapshot so events missed while
  disconnected cannot leave stale cards. These reloads never trigger a source
  sync.
- A remote connection failure never falls back to local mode. The error state
  offers retry and points to Server settings.

### 2.2 DotCraft App Binding UX

App Binding appears as a compact connection status and consent flow, never as
an embedded conversation.

- DotCraft receives Oratorio app-binding links, checks the active Workspace,
  then forwards the approval to Oratorio through the main process.
- The main process supplies the current local or tunneled AppServer address
  and runtime identity. Neither AppServer nor Oratorio credentials reach the
  renderer.
- Local and remote services both support App Binding. Switching service drops
  the previous Oratorio stream and cached service metadata.

The native surface may show App Binding state where the operator must act on
it. That state does not replace service health and does not imply a binding to
a specific thread.

The consent dialog follows Oratorio's modal and density language:

- connection consent shows the requesting DotCraft workspace and user, the app,
  the expiry, and that thread access remains a separate DotCraft choice;
- thread binding consent, when policy shows it, summarizes the thread, scopes
  and tools as compact badges or rows;
- loading, error, cancel and approving states stay visible and accessible;
- success, failure and follow-up messages use the shared notice and toast
  style.

### 2.3 Built-in surface registration

One Desktop-owned native surface supplies the board, drawer, detail page and
Settings in both local and remote modes. There is no separate extension UI.

## 3. Kanban Board

The board title lockup shows the Oratorio logo, a small `Kanban` label and the
title `Oratorio`.

The board offers:

- search;
- repository and assignee filters;
- `source:` and `label:` search qualifiers;
- new local task;
- refresh;
- a view switcher with `Active`, `All`, `Cancelled` and `Archived`;
- four Active columns: To do, In progress, In review and Done;
- paged lists for `All`, `Cancelled` and `Archived`;
- task cards;
- drag-and-drop;
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

List rows have no dividers; a row answers hover with a rounded background.
List views page their results and load the next page when the user scrolls
near the end. Changing view, search or filters starts again from the first
page.

Cards show compact board information only: the status dot, source and kind
chips, the title, a one-line summary, labels, a neutral pill for a closed or
merged source, and the update time. Cards never show the full source body, raw
agent output, worktree paths or long technical identifiers.

The Local Task create and edit form puts task intent first, then routing:

- title and description are the primary fields;
- the dialog uses the shared DotCraft icon-and-title header, with no Oratorio
  eyebrow and no internal header or footer dividers;
- repository uses the shared closed selector; assignee and base branch are
  editable comboboxes offering known values;
- labels are added and removed as pills, with suggestions and an inline
  `Add label` pill, never a comma-separated field;
- labels are trimmed, empty values ignored and duplicates removed ignoring case
  while keeping the first spelling;
- `Base branch` is the branch runs start from, not the generated work branch;
  leaving it empty means the repository default;
- the last used repository is preselected while still available, and a single
  available repository is selected automatically;
- `Create task` starts a short non-blocking celebration from the button before
  the request completes;
- success shows the notice `New task "<title>" created. Click to view details.`,
  which opens the task in the drawer;
- reduced-motion users get the notice without particles.

### 3.1 Card Visual Contract

Cards use [Desktop DESIGN](../../architecture/DESIGN.md) chip, status and icon
primitives. Source and kind chips share optical weight; a source without a
provider glyph uses the Local source icon. The header reads source then kind;
the kind shows the issue, pull request or merge request number, while internal
identifiers stay in addresses and detail views. A GitLab merge request reads
`Merge request` with GitLab's `!` number. The source chip, list rows and filter
options name the project by its path without provider prefix or public host
(`github.com`, `gitlab.com`); a path deeper than two segments shows its last
two (`backend/billing-api`), the same shape as a GitHub `owner/name`, with the
full path as a tooltip. The detail page's Source shows the full path, including
a self-hosted instance. The title has its own row, with the status dot at its
trailing edge.

The column already says where a task is, so the dot tells the user what
Oratorio's latest result means for them, and colour lives only in the dot:

- **In progress:** blue while a run is queued or running, red when it failed.
- **In review:** from Oratorio's latest result, which stays until the next run
  produces a new one ([Oratorio Design §4](oratorio-design.md#4-domain-and-api-contract)):
  - green when the latest review found no issues, or the latest
    implementation was delivered as a pull or merge request;
  - yellow when the latest review has suggestions, the latest implementation
    is ready for the user to look at or deliver, or follow-up tasks are
    proposed and waiting for a decision; pending follow-ups win over the rest;
  - no dot when there is no result.
- **To do** and **Done:** no dot.

The dot's tooltip names its meaning. Cards have no status pills; labels are
neutral outlined pills in secondary text, and a closed or merged source adds one
neutral pill. The full lifecycle and check state live in the drawer and the
detail page.

## 4. Task Drawer

Opening a task on the board opens the Task Drawer: the task page of §5 itself,
in a drawer presentation over the right edge of the board. There is no separate
summary view and no separate set of quick actions; whatever the page offers,
the drawer offers, so an operator can triage, decide and comment without
leaving the board. The drawer differs from the page only in layout:

- it is about 410px wide, floats over the board on the elevated surface, and
  closes with its close button or Escape;
- its toolbar keeps the status menu, `Open on GitHub` / `Open on GitLab`,
  `Open thread` and the state's action, and ends with `Full details`, which
  opens the full page, and close. It drops the back button and the source
  text;
- the rail's properties (§5.4) sit as compact label and value rows between the
  description and Activity, with the project path followed by the source
  number.

While the drawer is open, board refreshes keep the loaded task on screen and
reload it only when it changed, so live activity never blanks it. Action
success and failure use the DotCraft global toast host and never insert banners
into the page.

A running run's Activity line carries a live indicator: a short verb for what
the agent is doing (Thinking, Running command, Writing, Using tool), optionally
followed by its latest streamed text. It is an ephemeral status line, not a
conversation row: replaced in place as the run streams, never stored, and
cleared when the run ends.

The Discussion composer closes Activity with separate `Add comment` and `Ask
Oratorio` actions. `Add comment` records internal operator feedback. `Ask
Oratorio` starts an Agent Discussion Turn only when the backend reports an
eligible completed thread and no active Discussion Turn. Agent replies are
signed `Oratorio` with the Oratorio mark, the operator's own comments carry
their Profile avatar, and comments imported from the provider keep their
author's initial. The composer uses the card of Desktop line comments
([DESIGN Line comments](../../architecture/DESIGN.md#line-comments)): the user's
avatar and `You` in the header, a bare field that grows with its text, and the
actions in the card's footer. Enter adds a comment and Shift+Enter starts a new
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

The Task detail page is the task's full review surface, reached from the
drawer's `Full details` action and from any link; the Task Drawer (§4) renders
the same page in a narrower layout. Deeper agent execution surfaces stay in
DotCraft Desktop, reached through `Open thread`.

The page is laid out like a pull request: a toolbar, then a main column with
the task and its Activity, and a narrow rail of properties. It has no tabs or
stages; everything that happened to the task reads top to bottom. It shows only
what an operator acts on. Execution internals — workspace and worktree paths,
thread and turn identifiers, worktree state, run attempts, round history,
system timeline events, the source snapshot's identity and head commit — are
not shown; the DotCraft thread is where execution is inspected.

Colour lives on icons and dots, not on pills: the page renders no state badge,
no chips, and no severity or draft-status pills.

### 5.1 Routing

The page has its own address; refreshing or sharing it lands on the same task.

### 5.2 Toolbar

One row, left to right:

- a back button;
- the status menu: the state's icon in its tone and the state's name. When the
  task can be archived or reopened, the button opens a menu with `Archive`, or
  `Reopen` once archived; this is how a task is closed, the way a pull request
  is closed from its status. Otherwise the state is plain text;
- the provider glyph, the project name per §3.1 and the source number (`#N`,
  `!N`), or the short identity of a local task, as plain secondary text;
- trailing icon buttons for `Open on GitHub` / `Open on GitLab` (when the task
  has a source link) and `Open thread` (disabled until a thread exists);
- one action chosen by state, always in the primary button style:
  - to do: the first start action the task allows (`Implement`,
    `Auto PR / MR`, `Review only`) as a split button holding the others;
  - queued or running: a confirmed `Cancel run`;
  - failed or timed out: `Retry`;
  - awaiting review: `Review` (§5.5);
  - approved or rejected with a newer source revision: `Review new revision`.

When the state changes, the status and the action cross-fade in place while
their width eases to the new label, so the toolbar never jumps.

### 5.3 Main column

The title, the source branch for a pull or merge request, and the source or
local task body as Markdown. Then **Activity**, in time order. Every entry is
its own quiet tile with no connecting line: a round 24px icon, one line of
text, the time at the trailing edge, and any detail beneath. A comment's or
event's text aligns with the line above it, while a draft card's findings,
fields and diffs use the tile's full width. Tiles have no inner dividers, and
the Activity heading has no rule. Only decisions (approve green, request
changes red), failed runs (red) and failed source writes (warning) colour their
icon; every other icon is secondary. The entries are:

- each Oratorio run like a CI check: a spinner, check or cross, the run's
  purpose (`Review`, `Implementation`) and status, and the time. A running run
  adds its live activity; a finished one its summary; a failed one the
  server's message (`Retry` is the toolbar's). A run sits before the drafts it
  produced;
- drafts as cards signed with the Oratorio logo: the review draft card (§5.7),
  implementation drafts and follow-up drafts. A card's header carries its
  title, its counts as plain text, its state as text, and its actions:
  `Deliver` for an implementation draft; `Discard`, `Save` and `Create local
  task` for a follow-up draft. Follow-up drafts from a pull or merge request
  review show the inherited review-target branch when the draft leaves its
  branch empty and targets the same repository. Discarded drafts are not shown;
- comments (§4);
- decisions as events: `{author} approved these changes`, `requested
  changes`, `rejected this task`, `reopened this task` or `asked for a new
  review`, quoting the feedback when there is any;
- each failed source write as `Couldn't update GitHub` with the server's
  message and `Retry`, except a failed review publication, which lives on its
  review draft card;
- while a Discussion Turn is pending or running, `Oratorio is replying…`.

The Discussion composer (§4) closes the column.

### 5.4 Rail

Short sections divided by hairlines, each a quiet heading and plain text:
`Oratorio` (the latest run's purpose and status, or `Not started`), `Changes`
(the latest implementation draft's files, when there are any), `Assignee`,
`Labels` (when there are any), and `Source` (the full project path per §3.1,
the relative sync time and a refresh icon button). Below 760px the rail
follows the main column.

### 5.5 Review decision

`Review` opens a popover anchored to the button: `Approve`, `Request changes`
and `Reject` as one radio group, an optional comment field (required for
`Request changes`), and `Cancel` with `Submit review`, which turns danger for
`Reject`. Escape or a click outside closes it. The decision then appears in
Activity, and the status menu shows the new state. A submission that fails
keeps the popover open with the operator's comment, and the composer keeps its
text the same way.

### 5.6 Empty states

A task with nothing in its history renders no Activity entries above the
composer; rail sections with no content are omitted rather than shown empty.

### 5.7 Review draft card

The review draft card shows the review Oratorio published to the provider
([Oratorio Design §6.1](oratorio-design.md#61-structured-review-suggestions)):

- its summary, and its counts as plain text;
- its publication state as text: published, or publication failed with the
  server's reason;
- only when publication failed, `Retry` and `Discard`. There is no `Save`, no
  `Publish` and no editing: publication is automatic;
- its findings one after another, resolved under [Oratorio
  Design §6.4](oratorio-design.md#64-review-finding-resolution):
  - an open finding shows its severity as a coloured dot and plain text, its
    title, its `path:line` in monospace, its body as Markdown and any suggested
    change as an inline diff, and once the review is published `Dismiss` and
    `Mark fixed`;
  - a resolved finding collapses to one muted line with a check, its title,
    the resolution (`Fixed` or `Dismissed`) and `Reopen`;
  - the counts include open findings only; resolved findings stay visible for
    audit and are never silently removed.

## 6. Settings

Settings is the only destination besides the board.

GitHub and GitLab each have one Settings section, listed as its own top-level
entry, holding top to bottom: Connection (endpoint, identity, write
enablement and write-only credential inputs), Project routing (repository or
project to DotCraft workspace mappings, GitHub owner installations and GitLab
per-project access), and Sync (read status, a primary `Sync now`, scheduled
incremental sync with its next run, `Full repair` and failed-sync retry in an
overflow menu, and provider background failures). There is no separate
Sources, Credentials or Projects destination.

Settings content:

- repository cards combining the repository, its DotCraft workspace and the
  workspace and AppServer health. Workspaces are chosen only from local
  Projects registered with DotCraft Desktop; Chat workspaces, remote Projects,
  secondary folders and arbitrary folders are excluded;
- GitHub installations listed per owner on the GitHub page, showing detected
  or manual status, with a manual installation override;
- an Access group on each project's page: for GitLab, the project's token (its
  kind and whether it is set), webhook secret and signing token, each shown as
  configured or not set; for GitHub, the installation covering the
  repository's owner. The token kind is chosen from project, personal or group
  access token together with a new token, never typed as free text;
- a provider page header with read, write and webhook health as a compact
  status line, a primary `Sync now`, scheduled incremental sync with its next
  run, and `Full repair` plus failed-sync retry in an overflow menu;
- credential presence and write-only password inputs with show and hide that
  never echo stored values;
- Agents: DotCraft bridge status, AppServer and Hub discovery, approval policy
  and run timeout. Approval offers approve automatically or deny and continue,
  defaulting to deny and continue; a server run never follows a desktop
  workspace default, and any other stored approval value reads as deny and
  continue;
- Worktrees: managed worktrees, the managed root and the branch prefix;
- implementation auto-dispatch policy and delivery;
- Review: repository Auto Review.

Concurrency, retry, stall and cleanup policy stay server-owned and are not in
Desktop Settings. Review publication has no setting: it is always automatic.

Settings never shows stored tokens, webhook secrets, private keys or private
key paths. Write-only inputs are cleared after the save answer, and an empty
secret input leaves the stored value unchanged. Auto-start command and process
argument inputs are not Settings content. GitHub installations appear only as
owner installations. GitLab instance connection (endpoint, read sync, write
enablement, webhook verification) lives on the GitLab page; per-project GitLab
token, webhook secret and signing token appear only in that project's Access
group. Changing the GitLab endpoint host clears project access from the draft,
with copy explaining the restart and impact.

Saves that need a process restart show a pending restart banner at the top of
Settings. Desktop offers a restart button when it can restart the server;
otherwise it states that a manual restart is needed. Saving never uses a
native confirmation dialog.

Until the configuration first loads, Settings shows a loading state instead of
default values, and a load failure replaces the form with a page-level retry;
nothing is editable, and so nothing can overwrite the server's configuration,
before the server has answered. Other asynchronous failures use DotCraft
Desktop's shared toasts. Automatic saves, synchronization and sync-schedule
saves never insert error rows into the form. Failed saves and explicit
synchronization offer a toast retry, and a repeated failure replaces the
previous Settings error toast. The server's answer to a save is authoritative:
Settings shows what the server stored, keeping only edits made after the
request was sent. Leaving Settings saves edits still waiting for their save
delay instead of dropping them. Local validation such as duplicate labels,
malformed addresses and out-of-range numbers stays next to the field.
Persistent service-unavailable, remote read-only, restart-required and provider
health states stay in the page.

In remote mode, server administration is read-only even when the tunnel makes
the backend look locally writable. Settings may show remote configuration,
diagnostics, workspace inventory and source status, but saving server
configuration, secrets, workspace routing, worktree policy and automation
policy is disabled, with copy directing operators to manage the server through
its own configuration. Board, task, review, comment and decision actions stay
available when the backend allows them.

Workspace paths shown in remote mode are paths on the remote host or container,
and remote mode never writes local paths into remote workspace mappings.

Settings actions share one compact visual language. Group actions such as
`Start server`, `Discard` and `Save` sit in the group header. Rows hold status,
form controls, toggles and read-only values. Page refresh is an icon-only
action with an accessible label, matching the board toolbar. Rows never use
browser-native select menus or number spinners; single-choice and numeric
controls use DotCraft Desktop components with accessible labels, keyboard
navigation, visible focus, and disabled, highlighted and selected states.
Display labels may be human-readable while stored values stay unchanged. The
DotCraft bridge `Start server` action is visible whenever the bridge is not
connected or any configured workspace is not connected.

Settings manage repository lists such as Auto Review as compact cards rather
than per-repository switches. A card shows
how many repositories are included, the selected ones, a remove action per row
and `Manage`. `Manage` opens a searchable checklist of configured source
projects only. The dialog updates the Settings draft; the page's `Save` still
commits it. The dialog never invents project details, such as per-project
index times, that Settings does not have.

List pills and choices show short project paths, normally the last owner or
group and project segments. Ambiguous labels expand only as far as needed to be
distinct, and a tooltip keeps the provider, instance and full path. Stored
values keep the canonical project keys.

Repository Settings never offer a fallback workspace; every AppServer run
resolves its workspace from the item's repository mapping. A saved binding that
is no longer registered stays visible as unavailable until the user rebinds or
removes it, and it never blocks unrelated saves. Removing a source project is
one atomic change: it removes the provider's synchronized project, its
workspace route, and its entries in the Auto Review and Implementation
Follow-up lists. Task, run and audit history stays, and shared provider
installations and credentials are kept.

### Connect a source

Settings offers one guided path for a new source beside the per-field pages.
It is a full-width view inside Oratorio settings (breadcrumb `Oratorio settings
› Connect GitHub|GitLab`, the shared setup stepper and a Back/Next footer),
never a modal, and it uses plain fields and inline `Learn more` links rather
than disclosures, capsule pills or in-panel progress lists. Two entry points
open it: the board's "no source connected" state, with `Connect GitHub` and
`Connect GitLab`, shown while no provider has credentials, installations or
projects and no tasks exist; and the `Connect a source` action on the Settings
root. It is the only way to add a project: running it again adds another
project and reuses an already configured GitHub App.

The five steps are Source (provider, endpoint and credentials: GitHub App ID
plus private key or key path, or a GitLab token kind and token; an already
configured GitHub App collapses to a configured row with `Replace`), Project
(`owner/repository` or `group/project`, validated and checked against
configured projects; GitHub shows an optional installation ID whose absence
means server-side detection), Workspace (a choice among local DotCraft
Projects, defaulting to the foreground Project; with none registered the step
explains that a Workspace must be opened first and cannot advance), Automation
(sync schedule Off / 15 minutes / hourly / custom minutes, automatic review,
and provider writes, defaulting for a local Desktop to 15 minutes, review on,
writes off), and Connect (a read-only summary with `Change` per row and the
single primary `Connect and sync`).

The wizard never shows project access records or installations as such. For
GitLab it stores the token as the project's access, keyed by its path; for
GitHub it records an owner installation only when an ID is typed, and
otherwise, with no installation known for the owner, requests server-side
detection. Connecting the same project again updates it instead of adding
duplicates. Connect commits one atomic configuration change, saves the sync
schedule, reports the saved configuration in a toast, then starts a sync for
the new project and follows it. A finished sync with no failed project
confirms read access with imported counts. A rejected save shows the server's
reason and states that nothing was saved. Once the configuration is saved, a
failed schedule save, a failed or partly failed sync, or a detection warning
for the new owner shows the failure, states that the configuration is saved,
and offers a way back to the step that owns the fix. `Retry` resumes after the
saved configuration and saves again only if the user changed the connection.
A sync still running after the waiting window reports that the first sync
continues in the background. Remote read-only mode disables every field and the
primary action and explains why. Enter inside a field advances a valid step.

## 7. DotCraft design system

[Desktop DESIGN](../../architecture/DESIGN.md) owns palette, typography,
themes, icons, controls, surfaces and accessibility. The card, drawer and
detail page rules here define Oratorio's information hierarchy using those
primitives. The board reserves no product rail; logo, product name and Settings
stay in the header. Text and actions stay reachable at supported widths.
