# Oratorio Design Specification


| Field | Value  |
| ----- | ------ |
| Version      | 0.2.0    |
| Status       | Living |
| Date         | 2026-09-06        |
| Parent Specs | [AppServer Protocol](../../protocols/appserver-protocol.md), [Automations Lifecycle](../automations-lifecycle.md) |
| Companion   | [Oratorio Native Surfaces](./oratorio-frontend.md) — canonical board and settings layout, navigation, and component vocabulary. This document owns product behavior; the frontend spec owns visual and interaction design.                                                                              |


Oratorio is DotCraft's built-in agent project management product. It runs as
native DotCraft Desktop surfaces backed by a durable headless service, owns Task
state, comments, dispatch and review rounds, decisions, run summaries, review drafts, delivery
drafts, and source write audit history, and drives DotCraft through AppServer.
DotCraft remains the agent runtime and workspace execution layer.

The native surface boundary is intentionally narrow: Oratorio shows
the board, Task cards, and compact Status drawers. Detailed AppServer
conversation, approval decisions, plan inspection, file/terminal/preview views,
and turn-by-turn interaction belong in DotCraft Desktop.

This document is the canonical product and behavior contract for Oratorio: enduring boundaries, domain behavior, source semantics, runtime contracts, and validation expectations.

---

## 1. Product Boundary

Oratorio is:

- a Project and Task board for assigning, tracking, and reviewing agent work;
- a long-running orchestration backend with durable state;
- a source adapter host for GitHub first and additional trackers later;
- an AppServer client that dispatches review and work rounds to DotCraft;
- the owner of multi-round operator feedback and review decisions.

Oratorio is not:

- a replacement for DotCraft Session Core, AppServer, or Hub;
- a separate Desktop application or third-party extension UI;
- a larger version of built-in Automations;
- a general cron or reminder system;
- the owner of low-level agent execution internals.

DotCraft built-in Automations intentionally has no task-level review gate. Local
task automation, scheduled execution, and built-in source-neutral dispatch stay
there unless a separate design contract explicitly moves behavior into Oratorio.

---

## 2. Architecture Contract

```mermaid
flowchart LR
    Desktop["DotCraft Desktop Oratorio surfaces"]
    Backend["Headless Oratorio Backend"]
    DB[("Oratorio DB")]
    Sources["Source Adapters"]
    GitHub["GitHub App Adapter"]
    Local["Local Task Source"]
    AppServer["DotCraft AppServer"]
    Worktrees["Managed Worktrees"]

    Desktop <--> Backend
    Backend <--> DB
    Backend <--> Sources
    Sources <--> GitHub
    Sources <--> Local
    Backend <--> AppServer
    Backend --> Worktrees
```

| Component          | Contract                                                                                                                                         |
| ------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| DotCraft Desktop surfaces | Provide the task board, Status-only drawer, source/run/write summaries, Settings, and operator-visible board actions through typed Main-process IPC. |
| Headless Backend   | Owns orchestration, durable state, source sync, review transitions, AppServer dispatch, reconciliation, and source writes. It exposes API, health, and realtime stream endpoints, and does not serve a browser UI. |
| Oratorio DB        | Stores items, rounds, runs, comments, decisions, review drafts, source snapshots, timeline events, and source write logs.                        |
| Source Adapters    | Normalize external and local work into Oratorio source items and expose explicit read/write capabilities.                                        |
| GitHub App Adapter | Reads GitHub issues/PRs and writes comments, reviews, check runs, and source write audit entries through installation credentials.               |
| Local Task Source  | Owns Oratorio-local tasks with comments, rounds, dispatch, and review decisions independent of GitHub.                                           |
| AppServer Bridge   | Starts or resumes DotCraft threads, submits turns, subscribes to events, and records final output.                                               |
| Worktree Manager   | Owns backend-managed worktree paths, branch naming, concurrency limits, cleanup, and restart reconciliation.                                     |
| Desktop Provider   | Resolves the local Hub-managed or remote tunneled service, injects credentials, and owns the realtime stream.                                     |

Runtime topology contract:

- **Local-managed service**: DotCraft Desktop asks Hub to ensure the bundled
  Oratorio Server. Hub owns process lifecycle; Desktop Main owns API semantics,
  authentication injection, and the realtime stream.
- **Remote Stack service**: DotCraft Desktop connects to the official Stack with
  separate authenticated SSH tunnels for AppServer and Oratorio. The remote
  service owns orchestration, durable state, source sync, managed worktrees,
  AppServer dispatch, reconciliation, and source writes.
- Direct public exposure of the Oratorio API is unsupported. The optional
  webhook gateway exposes only the exact GitHub webhook POST endpoint.
- In a remote-controlled deployment, the Oratorio backend and DotCraft AppServer
  must run where they can access the same repository workspace and managed
  worktree filesystem paths. Containerized deployments must mount the shared
  workspace at the same absolute path for both services. The local Desktop
  filesystem is never used for remote AppServer execution.
- Stack initialization creates a writable server configuration under
  `state/oratorio`. Authenticated Desktop settings use the same revision and
  secret semantics in local and remote modes.
- Source webhook ingress is enabled and disabled through `dotcraft stack
  webhook`; it never exposes the remaining Oratorio API.


---

## 3. Lifecycle Contract

Oratorio state names are product states, not built-in Automations states.

```mermaid
stateDiagram-v2
    [*] --> Discovered
    Discovered --> Dispatching: Dispatch
    Dispatching --> Discovered: Cancel run
    Dispatching --> Running: Runner started
    Dispatching --> Failed: Preparation failed
    Running --> AwaitingReview: Run succeeded
    Running --> Failed: Run failed or timed out
    Running --> Discovered: Cancel run
    AwaitingReview --> Approved: Approve
    AwaitingReview --> Discovered: Request changes
    AwaitingReview --> Discovered: Implementation follow-up
    AwaitingReview --> Rejected: Reject
    Failed --> Dispatching: Retry
    Approved --> Discovered: Reopen
    Rejected --> Discovered: Reopen
```



Required lifecycle behavior:

- Every dispatch belongs to a durable round.
- Every run belongs to a round and records runner kind, attempt, status,
progress, heartbeat, summary, and error information when available.
- `approve` and `reject` are terminal for the current round until reopened.
- Timeline entries are operator-facing projections; canonical state remains in
items, rounds, runs, comments, decisions, source snapshots, and source write
logs.
- Operator cancellation is available only while an item is `dispatching` or
  `running`. It marks the active run as `cancelled`, closes the current round as
  `cancelled`, clears the current run, and returns the item to `discovered`.
  Cancellation is a run lifecycle action, not a decision, and the next dispatch
  creates the next numbered round.
- `requestChanges` requires non-empty feedback, records a linked operator
comment, closes the current round as `changesRequested`, and returns the item
to `discovered`.
- The next dispatch after requested changes creates the next numbered round.
- `reReview` is available for a GitHub pull request or GitLab merge request
  after its head SHA changes from the one analyzed by the latest successful
  AppServer review analysis run. It records an internal decision, supersedes the
  current round, creates the next numbered round, and queues a new read-only
  review run. It writes no source decision; the new round drives the
  `oratorio/review` gate check like any other review round.
- Repository-level Auto Review uses the same round semantics as `reReview`.
  For enabled repositories, new open non-draft pull requests that appear after
  enablement queue an AppServer `reviewAnalysis` run automatically. Later head
  SHA changes supersede the current round and queue the next read-only review
  round after any active run finishes. Auto Review never writes a source
  decision.
- Implementation Follow-up is an automated, gated, bounded loop anchored on the
  originating GitHub/GitLab issue or local task — never on the generated pull
  request, which stays a read-only review target per §6.2 and §6. When an
  originating item that already delivered a generated pull request is in
  `awaitingReview` and that generated PR accrues new unresolved published review
  findings (§6.1) or new human PR review comments, the Implementation Follow-up
  scheduler re-activates the originating item to `discovered`, creates the next
  numbered round, and queues a new implementation run that reuses the existing PR
  branch and pushes follow-up commits to the same pull request (§6.2). The loop
  fires only while the originating item is `awaitingReview`, has no active run, is
  not `approved`/`rejected`/`archived`, the generated PR is still open (not merged
  or closed), and the item's follow-up round count is below the configured
  maximum. The next implementation round after follow-up re-activation is an
  ordinary numbered round.
- Implementation Follow-up terminates when the generated PR has no open findings
  and its latest review round is clean, when the follow-up round cap is reached
  (recorded as an operator-visible skip state), when the operator `approve`s the
  originating item (accepting the handoff), or when the generated PR is merged,
  closed, or archived. Operators can disable the loop globally or per repository
  through the Implementation Follow-up policy in §4.
- `approve` is allowed only after a completed run has moved the item to
  `awaitingReview`.
- Every GitHub pull request review round — first dispatch, `reReview`, Auto
  Review, and retry alike — drives one `oratorio/review` check run, keyed to the
  head SHA under review. Oratorio writes it `in_progress` when the round is
  queued and updates that same check run to completed `neutral` when the review
  run succeeds, returning merge ownership to GitHub collaborators. A later
  Oratorio approval, requested changes, rejection, cancellation, or terminal run
  failure updates the same check run to success, action-required, or failure.
  This external check is the merge gate only when the repository requires it
  through GitHub branch protection or rulesets.
- `archive` is available for non-active local and source-backed items and
  preserves all history; `reopen` restores an archived item to `discovered`.
- Rejected and archived items are closed/history work. Their TaskStatus
  projection is `cancelled`, and they are not part of the default Active board.
- Imported source comments are source context, not operator feedback, and must
  not be treated as requested follow-up work by themselves. The bounded
  exemptions are:
  - a verified GitHub PR conversation command defined by the
    [GitHub Mention Review specification](./github-mention-review.md), which may
    request a read-only review run for an already configured repository; and
  - human review comments on the generated pull request of an originating
    implementation item, which are actionable follow-up feedback for that
    originating item's next implementation round under the gated
    Implementation Follow-up loop (§6.2, §8).

---

## 4. Domain and API Contract

Oratorio's backend owns workflow truth. The Desktop renderer renders state
returned by the API and must not invent domain transitions.

Canonical domain records:

- `Item`: source-neutral unit of work with `(source, externalId)` identity,
  optional repository metadata, lifecycle state, current round, current run,
  latest summary, check state, source lifecycle state, archive reason, and
  source sync timestamp.
- `Round`: durable review cycle for one item with a number, status, prompt
  audit context, latest summary, and completion timestamp.
- `Run`: execution attempt inside a round with runner kind, dispatch trigger,
  target head SHA when applicable, attempt, status, thread ID, turn ID,
  AppServer endpoint, prompt audit context, summary, error details, managed
  worktree metadata, retry state, and scheduler lease metadata.
- `Comment`: operator, source, agent, or system feedback attached to an item and
  optionally a round, with a purpose such as feedback, discussion question,
  discussion reply, source context, or system note.
- `DiscussionTurn`: lightweight operator-to-agent question bound to one item,
  optional round, operator question comment, optional agent reply comment, base
  AppServer run, thread ID, turn ID, status, prompt audit context, and error
  information. Discussion Turns are not Runs and do not create Rounds.
- `Decision`: operator action on a round: `approve`, `requestChanges`,
  `reject`, `reopen`, or `reReview`.
- `ReviewDraft` and `ReviewDraftComment`: structured PR review draft data
  submitted by DotCraft and later published, discarded, or left in draft state.
  A published, accepted `ReviewDraftComment` additionally carries resolution
  state (open/resolved, kind, actor, note, provenance, and source-thread
  mapping) per §6.4.
- `SourceWrite`: auditable GitHub write attempt with request, response, status,
  error, retry, and external URL metadata.
- `TimelineEvent`: append-only operator-facing projection for source sync,
  round, run, comment, decision, check, review draft, and write events.
- `AutoReviewRepositoryState` and `AutoReviewItemState`: durable backend
  scheduler state for repository Auto Review enablement, first-enable
  baselines, last observed PR heads, last queued PR heads, and visible skip or
  routing errors.
- `ImplementationFollowUpItemState`: durable backend scheduler state for the
  Implementation Follow-up loop (§3, §6.2), keyed by originating item, with the
  linked generated PR item, the last observed open-finding signature, the last
  observed human PR comment time, last queued head/round/run, the follow-up round
  count, and visible skip or cap state.

Public REST endpoints live under `/api/v1`, use camelCase JSON, and return
stable error objects:

```json
{
  "error": {
    "code": "invalidTransition",
    "message": "Cannot approve an item that is not awaiting review.",
    "details": {}
  }
}
```

Required endpoint groups:

- item list/detail and source-key lookup;
- local task create/update/archive/reopen;
- source-backed item archive/reopen;
- comment, discussion-turn, dispatch, approve, request-changes, reject, and
  reopen actions;
- run detail;
- GitHub status, sync, write retry, and source write visibility;
- review draft detail exposure plus edit, publish, and discard actions, and
  operator resolve/reopen of a published review finding per §6.4;
- DotCraft/AppServer status, workspace inventory, per-workspace health, and
  dispatch diagnostics.
- top-level status capabilities for managed worktrees, concurrency limits, and
  bridge/runtime feature visibility.
- settings diagnostics and server configuration endpoints for redacted
  diagnostics, configuration reads/writes, encrypted secret updates, and change
  audit history.

Mutating endpoints must validate lifecycle transitions at write time. Retrying a
failed source write retries only that write record and never creates a new
operator decision.

Server configuration writes are a local-admin capability, not a general remote
administration API. They require a trusted local boundary or production operator
authentication. Writable fields include selected GitHub source, GitHub credential
presence, DotCraft bridge, workspace routing, managed worktree, concurrency,
retry, timeout, cleanup policy values, implementation auto-dispatch policy,
repository Auto Review allowlists, Draft auto-publish allowlists, and the
Implementation Follow-up policy (global enablement, repository allowlist, and
maximum follow-up rounds). Tokens,
webhook secrets, and private keys are writable only through one-shot
replace/clear semantics and must be stored encrypted. Auto-start commands and
process arguments are never writable through Settings. Writes create a durable
redacted configuration change audit entry and return a restart-required
signature; they do not hot-apply by reloading the configuration root.

Repository workspace routes are declarative configuration. Configuration writes
validate each canonical source project key and require a syntactically valid,
fully qualified filesystem path, but they do not require that the directory is
currently mounted, registered with Hub, or present on disk. Availability belongs
to workspace inventory and run preparation: unavailable routes remain visible to
operators, report their health reason, and fail execution with the
applicable stable error such as `workspaceNotRegisteredInHub` or
`baseWorkspaceMissing`. An unavailable route must not block unrelated Settings
changes, rebinding, or removal.

---

## 5. Source Contract

### 5.1 GitHub Read Sync

GitHub read sync must normalize:

- issues and pull requests into stable Oratorio source items;
- stable source keys of the form `github` plus an external ID such as
`issue:owner/repo#42` or `pr:owner/repo#184`;
- title, body, repository, assignee, labels, external URL, branch, draft state,
source updated time, and head SHA where applicable;
- issue comments, PR reviews, and PR review comments as source-visible comments;
- source lifecycle state as `open`, `closed`, `merged`, or `unknown`, plus
  source close and merge timestamps when available;
- source snapshots for prompt reconstruction and audit.

GitHub read failures should be visible to operators and must not corrupt
existing imported item history.

Verified GitHub PR conversation commands are governed by the
[GitHub Mention Review specification](./github-mention-review.md). Command
handling is a narrow source-command path and must not make ordinary imported
comments dispatchable.

Closed issues and closed or merged pull requests should be automatically
archived when no run is active. If a source item reopens and the archive reason
was source-driven, Oratorio should restore it to `discovered`. Manual archive
must not be undone by a later source sync.

Archived source-backed and local tasks are hidden from the Active board by
default. Operators access them through an explicit Archived list view that pages
history results instead of rendering all archived cards in the board.

### 5.2 GitHub Write Feedback

GitHub writes are backend-owned adapter operations, not implicit agent behavior.
Oratorio supports explicit, auditable write actions for:

- Oratorio review summaries;
- request-changes feedback;
- reject or attention-needed feedback;
- GitHub PR review comments, including suggestion blocks;
- `oratorio/review` check-run state;
- source write logs visible in Oratorio.

GitHub writes are enabled only when GitHub write configuration and GitHub App
authentication are available. If writes are disabled or misconfigured, Oratorio
records a failed source write with a stable error code instead of hiding the
problem.

GitHub App installation identity is routed by GitHub instance and repository
owner rather than by one global installation ID. A configured profile maps
`<instance>/<owner>` to one installation ID, and every GitHub read, write,
branch push, and PR creation resolves the target repository through that owner
profile. When a profile is missing, Oratorio may use GitHub App credentials to
discover the repository installation through GitHub's repository installation
API; discovery failures must be reported without blocking unrelated project
routing saves.

Decision write mapping:

| Oratorio item | Operator action | GitHub write |
| --- | --- | --- |
| Pull request | `approve` | PR review plus `oratorio/review` success check |
| Pull request | `requestChanges` | PR review plus action-required check |
| Pull request | `reject` | PR review plus failure check |
| Pull request | `reReview` | no decision write; the new round drives the `oratorio/review` check per §3 |
| Issue | `approve`, `requestChanges`, or `reject` | Issue comment only |
| Local task | any decision | no GitHub write |

PR review suggestions must be published through GitHub PR review APIs as
operator-visible Oratorio writes. A single GitHub `COMMENT` review may include
one summary body and multiple inline comments. Inline comments should use
GitHub's current diff anchor fields (`path`, `line`, `side`, and optional
`startLine`/`startSide`) rather than relying on deprecated diff positions.
Suggestion replacements are rendered by Oratorio into GitHub suggestion blocks
inside the inline comment body.

GitHub App installation alone must not be treated as a merge gate. Repositories
must explicitly require the Oratorio check through branch protection or rulesets.

Automated Oratorio writes must not silently merge PRs, create commits, push
branches, or approve outside an explicit operator decision. Review draft
publication always creates a GitHub `COMMENT` review and never emits approval,
request-changes, merge, close, or branch-protection decisions by itself. GitHub
`APPROVE` review events are emitted only for explicit Oratorio operator approval
decisions.

Every intended write creates a source write record with item, round, decision or
draft linkage, source, kind, intent, status, repository, source number, head SHA
when applicable, request JSON, response JSON, external ID or URL when available,
attempt count, error code, error message, and timestamps. Timeline entries must
show queued, succeeded, and failed write attempts.

GitHub write failures are audited and retryable through Oratorio source-write
records. They do not roll back recorded Oratorio decisions or item transitions;
check-gated repositories should rely on the `oratorio/review` check-run state as
the external merge gate when GitHub write delivery fails.

### 5.3 Local Tasks

Oratorio-local tasks are first-class Oratorio records. They are separate from
DotCraft built-in Automations local tasks.

Local task behavior must include:

- operator-created title and body;
- optional repository, branch, labels, and workspace metadata;
- comments, review rounds, decisions, and timeline history;
- dispatch through mock runner or DotCraft AppServer;
- approve, request-changes, reject, and reopen transitions;
- edit, archive, and reopen actions only when the task is not actively
  dispatching or running.

Local task identity:

```text
source = local
kind = localTask
externalId = task:{shortId}
```

The backend generates the local task external ID. The UI must not derive
identity from the title because titles are editable. Default task lists hide
archived local tasks unless an explicit archived filter is selected.

Local tasks may participate in implementation auto-dispatch policy for
implementation work. They are still not a general cron/reminder system.

---

## 6. Review Draft Contract

### 6.1 Structured PR Review Suggestions

PR review suggestions are a structured draft flow, not free-form agent text.

The required ownership boundary is:

- DotCraft agents analyze the PR and submit structured review drafts.
- Oratorio validates, stores, displays, and audits the drafts.
- Operators decide whether to publish, edit, discard, or request another round.
- GitHub writes are performed only by Oratorio through installation
  credentials.

The canonical agent submission contract is the Runtime Dynamic Tool
`oratorio_run.SubmitReviewDraft`. Oratorio declares it on
`thread/start.dynamicTools` for Oratorio-created AppServer runs; DotCraft
invokes it through `item/tool/call`, and the callback is bound to the AppServer
connection and thread that created the run.

Every Oratorio Runtime Dynamic tool has exactly one identity — a single
description, JSON Schema, and prompt-visible tool id in the `oratorio_run`
namespace — shared by every surface that exposes it. Each surface applies its
own allowlist before dispatch. Runtime declarations carry no MCP metadata; MCP
annotations, UI resources, and UI metadata are MCP-only sidecars.

Runtime Dynamic Tools are not plugin manifest native tools. Plugin manifests
contribute Skills, MCP server declarations, and interface metadata;
model-callable plugin services use MCP when they are external reusable services.
Dynamic Tools remain the direct thread-scoped callback path for an AppServer
client such as Oratorio.

Every `oratorio_run.SubmitReviewDraft` call must bind to the current Oratorio
run thread so that drafts cannot be submitted across unrelated runs. Any
external reusable review service must provide an explicit run or round binding
contract before it can submit drafts.

`oratorio_run.SubmitReviewDraft` input must include:

- `summary`: object with review counts and body text;
- `comments`: array of inline review findings.

Each `comments` item must use the same field shape as DotCraft's built-in
GitHub PR review automation where possible:

- `severity`;
- `kind`: `suggestion` or `commentOnly`;
- `title`;
- `body`;
- `path`;
- suggestion fields: optional `oldText` and `newText`;
- comment-only fields: optional `line`, `side`, `startLine`, `startSide`, and
  `reason`.

Each inline comment must be either a concrete code suggestion or a
comment-only finding:

- `kind: suggestion` must provide `oldText`, the exact current right-side diff
  text to replace, and `newText`, the exact
  replacement body to render as a native GitHub/GitLab suggested change.
  Oratorio resolves `oldText` against the provider diff and derives
  `line`/`startLine` for publication;
- `kind: commentOnly` must provide `line` and `reason`; `reason` is one of
  `needsHumanDecision`,
  `requiresLargerChange`, `cannotAnchorSafely`, `investigateOnly`, or
  `leftSideOrDeletion`.

`kind` is the authoritative branch discriminator. As with DotCraft Cron tools,
fields declared for the other branch are ignored. Undeclared fields are rejected
by the closed generated schema.

Review Draft content contract:

- a clean review uses `summary.body` exactly `No issues found.`, sets
  `majorCount`, `minorCount`, and `suggestionCount` to `0`, and submits
  `comments: []`;
- a review with accepted findings uses the summary body `Found N issue.` or
  `Found N issues.`; the detail belongs in the inline comments;
- Oratorio canonicalizes agent-submitted `summary.body`, `majorCount`, and
  `minorCount` from the accepted comments when the draft is submitted; operator
  edits made afterward are respected when publishing;
- published inline comment titles are prefixed with `🔴` for `RED` findings and
  `🟡` for `YELLOW` findings; stored draft titles remain unprefixed;
- `RED` means a likely bug affecting correctness, security, data loss, or a
  broken workflow; `YELLOW` means an investigation flag, maintainability risk,
  or lower-confidence issue;
- `kind: suggestion` is used only for exact, small, right-side code changes that
  can be published as native suggestions; every other finding is
  `kind: commentOnly` with the matching `reason`;
- informational explanations stay in `summary.body` and must not become inline
  comments.

`summary.suggestionCount` means accepted concrete code suggestions only. The
server derives and persists this value from accepted inline comments with
resolved `oldText`/`newText`; if the agent-submitted
count differs, Oratorio stores the derived value and records a warning.

Successful `oratorio_run.SubmitReviewDraft` output must include:

- `draftId`;
- accepted comment count;
- warnings for skipped anchors that the agent cannot repair in the current
  round, such as unavailable provider diff data.

Correctable agent anchor errors, including paths outside the diff,
non-commentable comment-only line or range anchors, or side mismatches, must
fail the dynamic tool with `reviewDraftAnchorNotCommentable`. Suggestion
`oldText` that is absent from the right-side diff must fail with
`reviewDraftSuggestionTextNotFound`; `oldText` that matches multiple right-side
diff ranges must fail with `reviewDraftSuggestionTextAmbiguous`. Failed results
must include enough metadata and available commentable ranges for the agent to
repair the draft and call `oratorio_run.SubmitReviewDraft` again in the same DotCraft round.
Invalid items must not cause Oratorio to publish a partial GitHub review
silently.

Validation requirements:

- summary body is required;
- paths must be repository-relative and must not contain traversal;
- `kind: suggestion` comments must provide non-empty `oldText` and present
  `newText`; missing values fail the tool with `InvalidArguments`;
- `oldText` must match exactly one contiguous right-side
  changed/context diff range after line-ending normalization;
- `kind: commentOnly` comments must provide `line` and `reason`;
- `line` and `startLine` must be positive when present;
- `startLine` must be less than or equal to `line`;
- `side` and `startSide` must be `right` or `left`;
- unknown `kind` values, missing branch fields, and undeclared fields are
  rejected with `InvalidArguments`;
- no-op replacements whose `newText` exactly matches
  `oldText` should be skipped with a
  `reviewDraftNoOpSuggestion` warning;
- changed file and diff anchor validation must fail correctable invalid agent
  anchors with `reviewDraftAnchorNotCommentable` and must not persist a draft
  for that tool call;
- summary-only drafts with `comments: []` must not require source diff reads;
- unavailable source diff data or provider/file patch omissions must preserve
  submitted inline comments as skipped warnings instead of failing the dynamic
  tool call.

Draft lifecycle:

- `draft`: editable and publishable;
- `published`: immutable after GitHub publication succeeds;
- `discarded`: intentionally ignored by the operator;
- `publishFailed`: retryable after a failed publish attempt.

Every GitHub pull request and GitLab merge request AppServer `reviewAnalysis`
run must call `oratorio_run.SubmitReviewDraft` before it can succeed. If the agent finds no
actionable issues, it must submit a summary-only draft with `majorCount`,
`minorCount`, and `suggestionCount` all `0`, summary body `No issues found.`,
and `comments: []`. A GitHub PR or GitLab MR review run that completes without any
Review Draft fails with the stable error code `reviewDraftRequired`; Oratorio
must not synthesize the draft on the agent's behalf.

`reviewDraftRequired`, AppServer turn failure, and a non-operator AppServer
turn cancellation are recoverable PR/MR review failures. They schedule a new
run attempt in the same round according to the configured bounded retry policy.
The immediately preceding failed thread may be resumed only when its failure is
semantic or turn-level, its workspace and required Dynamic Tools still match,
and the AppServer supports Dynamic Tool rebind. Timeout, disconnection, stalled
heartbeat, and interrupted-run recovery start a fresh thread.

A successfully persisted Review Draft is the authoritative result of a review
analysis run. If a non-operator terminal failure arrives after the draft is
stored, Oratorio completes the run from the draft, records the terminal signal
as an operator-visible warning, and must not ask the agent to submit a duplicate
draft. Explicit Oratorio operator cancellation remains authoritative.

Comment lifecycle:

- `accepted`: valid inline comment eligible for publication;
- `skipped`: stored for audit and warning display, but not sent to GitHub.

An accepted comment that has been published additionally carries a resolution
state per §6.4. Publication status and resolution state are independent: only
published, accepted comments are resolvable, and resolution never edits the
published comment body.

Oratorio uses Runtime Dynamic Tools for direct client orchestration because
Review Draft submission is connection-bound and thread-scoped. Plugin-bundled
MCP remains appropriate for external reusable review services that are not
submitting back into a specific Oratorio run.

### 6.2 Implementation and Follow-up Drafts

Implementation mode is available for GitHub issues and Oratorio local tasks.
GitHub pull requests remain review targets and are not mutated by
implementation runs.

Implementation runs expose an Oratorio-owned Runtime Dynamic Tool named
`oratorio_run.SubmitImplementationDraft`. The tool is bound to the current run and thread in
the same way as `oratorio_run.SubmitReviewDraft`; final agent summaries are not sufficient to
create commits or pull requests. A valid draft includes a concise summary,
validation notes, risks, changed files, proposed commit message, proposed PR
title, and proposed PR body.

Agents may modify only the Oratorio-managed execution worktree selected for the
run. Agents must not commit, push, create pull requests, write GitHub issues,
publish reviews, approve, request changes, close issues, or merge. Oratorio
performs delivery actions through its backend and GitHub App credentials.

Delivery policy values are:

- `manualDelivery`: keep the Implementation Draft for explicit operator
  delivery;
- `autoPr`: after validation, commit locally, push a branch through the GitHub
  App installation token, create a pull request through the GitHub API, upsert
  the generated PR as a source item, and link it to the originating issue or
  local task.

Approving an originating implementation item is blocked while it has an
undelivered Implementation Draft. After generated PR delivery, approving the
originating item means the operator accepts the handoff to the generated PR
review flow; it does not approve or merge the generated PR.

Implementation auto-dispatch is controlled separately from delivery policy.
`autoDispatch` decides whether the backend scheduler may start eligible
implementation runs. `deliveryPolicy` decides whether a valid draft waits for
manual delivery or uses `autoPr`. Allow/block labels and repository/workspace
configuration determine eligibility.

Implementation follow-up delivery handles the case where an originating item that
already delivered a generated pull request is implemented again under the
Implementation Follow-up loop (§3). It is delivery of additional commits to the
existing review target, not creation of a new one:

- Before creating a review target, delivery must detect an existing open
  generated pull request for the originating item (by parent linkage and head
  branch). When one exists, delivery pushes follow-up commits to the same branch
  and updates the same generated PR item; it must not open a second pull request.
  If the source API rejects a duplicate creation because the PR already exists,
  delivery resolves and links the existing pull request instead of failing.
- After a follow-up push, delivery updates the generated PR item head so that
  Auto Review or `reReview` (§3, §6.1) detects the new head and re-reviews it.
- The follow-up implementation round's managed worktree is prepared from the
  existing generated PR branch head, not reset to the repository base ref, so
  previously delivered commits are retained and new commits stack on top. This is
  the narrow exception to the per-round worktree base-ref reset in §6.

`autoFollowUp` is a third policy, independent of `autoDispatch` and
`deliveryPolicy`. It decides whether the backend may automatically re-implement an
originating item in response to its generated pull request's review feedback. It
is globally gated and repository opt-in by exact `owner/name`, mirroring Auto
Review and Draft auto-publish. With `autoFollowUp` disabled or the repository not
allow-listed, generated PR feedback never auto-re-activates the originating item.

The Implementation Follow-up loop (§3) is distinct from `oratorio_run.SubmitFollowUpDraft`
below: the loop continues the current item's own delivery on its existing PR,
while `oratorio_run.SubmitFollowUpDraft` proposes separately scoped new work.

Follow-up runs expose an Oratorio-owned Runtime Dynamic Tool named
`oratorio_run.SubmitFollowUpDraft` when eligible. Agents use it to propose split-out work,
blockers, or separately scoped improvements without directly creating external
issues or mutating source trackers.

Follow-up Drafts are bound to the current item, round, run, and AppServer thread.
Operators can edit, discard, or create each draft as an Oratorio local task while
it remains in `draft` status. Creating a local task copies the operator-reviewed
fields into a new local task, marks the draft `created`, records the created item
ID, and adds timeline entries on both the originating item and created task.
When the originating item is a GitHub PR or GitLab MR and the draft does not
explicitly override routing, the created local task inherits the review target's
repository, head branch, and head SHA so later implementation runs start from
the reviewed head rather than the mapped workspace's current `HEAD`.
Follow-up Drafts are advisory and do not become hidden requirements for the
current round.

### 6.3 Agent Discussion Turns

Oratorio supports a narrow, lightweight operator question flow for completed
AppServer work. This flow exists so operators can ask the agent questions from
the Task detail Discussion without re-dispatching a full review or
implementation round.

The required ownership boundary is:

- `Add comment` creates internal operator feedback for record keeping and later
  review rounds.
- `Ask agent` creates an internal operator discussion question and a
  `DiscussionTurn`.
- A pull request `reReview` action is the explicit way to dispatch a fresh
  review after new commits. `Add comment` plus `Ask agent` must not implicitly
  create a re-review round.
- Discussion Turns never create a `Round` or `Run`, never change Task lifecycle
  state, never update `currentRunId`, and never change check state. A Discussion
  Turn writes to a source system only to resolve a review finding under §6.4;
  it must perform no other source write.
- Operator questions and agent replies are rendered in the same Discussion
  history as comments, but their purpose keeps them out of next-round feedback
  by default.

Ask agent eligibility:

- The Task must not be archived, dispatching, or running.
- The Task must have a latest compatible successful AppServer run with a
  reusable thread whose prompt context used compact prompt mode and whose
  dynamic tool list includes `oratorio_run.SubmitDiscussionReply`.
- The Task must not already have a pending or running Discussion Turn.
- If no compatible thread exists, Oratorio rejects Ask agent with a stable
  validation error instead of implicitly dispatching a new round.

The canonical agent reply contract is the Runtime Dynamic Tool
`oratorio_run.SubmitDiscussionReply`. Oratorio declares this tool on every new
Oratorio-created AppServer thread from `thread/start.dynamicTools` so the tool
set remains prompt-cache friendly across later turns. The tool input is:

- `discussionTurnId`: the pending Discussion Turn to answer;
- `body`: the Markdown reply to record.

`oratorio_run.SubmitDiscussionReply` succeeds only when the call is bound to the current
thread and turn for a pending or running Discussion Turn. Mismatched thread,
mismatched turn, unknown turn, completed turn, and empty reply calls must fail
with stable errors. On success, Oratorio records one agent comment with purpose
`discussionReply`, links it from the Discussion Turn, marks the Discussion Turn
succeeded, and publishes a board update so the detail page refreshes.

Discussion Turn prompts must be short and incremental. They should identify the
Task, include the operator's question and `discussionTurnId`, mention the most
recent run summary when available, and point the agent to the stable Oratorio
discussion runtime context for reply submission and boundaries. They must not
restate full source snapshots, full round history, imported source comment
history, or stable tool-use rules.
When the Task has open published review findings, the prompt may additionally
list them per §6.4 so the agent can resolve a finding the discussion concludes
is handled. Discussion Turns require both Dynamic Tool rebind and runtime
additional context support.

### 6.4 Review Finding Resolution

A published review finding (§6.1) is a standing thread that stays open until it
is addressed. Both agents and operators can close a finding without re-running a full review.
Resolution serves two flows:

- in an Agent Discussion Turn (§6.3), once discussion concludes a finding is a
  non-issue or already handled;
- in a later review round, once the agent confirms an earlier round's finding was
  fixed at the current head.

Oratorio owns resolution as durable state on the finding and, when the finding
maps to a known source review thread, propagates it to the source system through
installation credentials. Source resolution is never the source of truth;
Oratorio's stored resolution state is.

Resolution model. Each published, accepted `ReviewDraftComment` carries:

- `resolutionState`: `open` or `resolved`, defaulting to `open`;
- `resolutionKind`, required when resolving: `fixed` means the underlying issue
  was addressed in code, typically detected in a later round; `dismissed` means
  the finding was agreed to be a non-issue or intentionally not actioned,
  typically concluded in discussion;
- `resolvedByKind`: `agent` or `operator`;
- `resolutionNote`: optional short rationale;
- `resolvedAt`;
- resolution provenance: `resolvedInRunId` for review-round resolutions,
  `resolvedViaDiscussionTurnId` for discussion resolutions, neither for operator
  resolutions;
- source-thread mapping: `remoteThreadId` and `remoteResolveWriteId` per the
  source propagation rules below.

Resolution rules:

- only an accepted comment in a `published` draft is resolvable; `skipped` and
  unpublished comments are never resolvable because they were never posted;
- resolving is idempotent: resolving an already-resolved finding with the same
  kind is a success no-op; changing the kind updates the stored kind, actor, and
  note;
- operators may reopen a finding (`resolved` to `open`), which clears the
  resolution fields and, when applicable, enqueues a source un-resolve;
- open-finding tallies exclude resolved findings; resolved findings remain stored
  and visible for audit.

Agent contract. The canonical agent contract is a Runtime Dynamic Tool named
`oratorio_run.ResolveReviewFinding`, declared on every Oratorio-created AppServer thread
alongside `oratorio_run.SubmitDiscussionReply` so both the originating review run and later
Discussion Turns on the same thread can call it, and so the thread tool set stays
prompt-cache friendly. Its input is:

- `findingId`: the published `ReviewDraftComment` to resolve;
- `resolutionKind`: `fixed` or `dismissed`;
- `note`: optional rationale.

`oratorio_run.ResolveReviewFinding` succeeds only when the call is bound to the current thread
and the finding belongs to the same Item as the calling run or Discussion Turn.
Mismatched thread, cross-Item findings, unknown findings, and non-resolvable
findings must fail with stable errors (`reviewFindingNotFound`,
`reviewFindingNotResolvable`). On success it records the resolution with
`resolvedByKind` `agent`, sets the matching provenance, appends a timeline event,
publishes a board update, and returns the `findingId` and resulting
`resolutionState`. Resolving a finding is the only source-affecting state change
an Agent Discussion Turn may make.

A prompt that offers `oratorio_run.ResolveReviewFinding` must list the open
published findings the call may target, with their `findingId` values, and must
constrain the resolution kind to the flow it is running: a review round may
resolve only as `fixed`, and only for findings addressed at the current head; a
Discussion Turn may resolve only as `dismissed`, and only when the discussion has
concluded the finding is a non-issue or already handled. Resolution must never
substitute for answering the operator's question.

Source propagation. Resolution propagates to the source review thread only when
Oratorio knows the finding's source thread identity. To map findings to threads,
each accepted inline comment published under §6.1 carries a stable hidden marker
referencing its `findingId`. During publish reconciliation Oratorio records
`remoteThreadId` per finding:

- GitHub: each published `COMMENT` review inline comment maps to a pull request
  review thread; Oratorio records the GraphQL review-thread node id per finding;
- GitLab: each inline discussion created during publication maps to one finding;
  Oratorio records the discussion id per finding.

When a finding becomes resolved and a `remoteThreadId` exists, Oratorio enqueues
a `SourceWrite` of canonical kind `resolveReviewThread` that resolves the thread
through the same source-write audit and retry machinery as other writes:

- GitHub resolves with the `resolveReviewThread` GraphQL mutation and un-resolves
  with `unresolveReviewThread`;
- GitLab resolves the discussion with `resolved=true` and un-resolves with
  `resolved=false`.

Source resolution requirements:

- it only toggles the thread resolved flag and never changes review decision,
  approval, merge, close, or branch-protection state;
- it operates only on PRs/MRs Oratorio published to; if no `remoteThreadId` is
  known — for example a draft published before mapping existed, or a comment that
  was `skipped` — resolution stays internal-only and records an operator-visible
  note that the source thread was not resolved;
- a failed `resolveReviewThread` write retries only that write record per §4 and
  never alters the stored resolution state;
- propagation follows the same provider write controls as other source writes;
  disabled writes or invalid credentials record a failed source write rather than
  silently keeping the resolution internal-only.

---

## 7. Automation Policies

Review Draft publication may be manual or automatic by Draft auto-publish
policy. Draft auto-publish is globally gated and repository opt-in by exact
`owner/name`. It must publish only a GitHub `COMMENT` review, never `APPROVE`,
`REQUEST_CHANGES`, merge, close, issue-close, or branch-protection decision
events. Draft auto-publish does not resolve the Oratorio item; the item remains
`AwaitingReview` until an operator records an Oratorio decision. Draft warnings,
skipped inline comments, stale head SHA, missing GitHub write authentication, or
disabled GitHub writes block auto-publication and create failed source-write
records tied to the draft.

GitHub publication uses a single `COMMENT` pull request review with the summary
body plus accepted inline comments. Only concrete code suggestions render a
fenced `suggestion` block; comment-only findings publish as prose comments.
GitLab publication creates a summary note plus inline discussions. Multi-line
GitLab code suggestions render offset-aware fence openings such as
`suggestion:-N+M` when the final anchor line needs to cover preceding lines.
The Review Draft UI must show code-suggestion and comment-only finding counts
separately and display the `commentOnlyReason` for comment-only findings.

Draft auto-publish is configured as a repository allowlist over the configured
GitHub repositories, under `Automation.AutoReviewPublishEnabled` and its
allowlist. A non-empty allowlist enables the policy for exactly those
repositories; an empty allowlist disables it.

Repository-level Auto Review is a separate policy, configured under
`Automation.AutoReviewRepositories` and exposed as
`automation.autoReviewRepositories` in the Settings API. Each entry is an exact
`owner/name`, and a configured repository is either off or on. Label-based PR
review triggers are not part of the Auto Review contract and must not affect
Issues implementation auto-dispatch policy.

Settings manages implementation auto-dispatch allow and block label lists as
free-form label controls rather than multiline text. Labels are trimmed, empty
entries are ignored, and duplicates are removed case-insensitively while
preserving the first entered spelling. An empty allow list continues to mean
all otherwise eligible, unblocked GitHub Issues and local tasks may dispatch.

Auto Review scheduler requirements:

- when a repository is first enabled or re-enabled, baseline current open
  non-draft PRs and do not queue historical reviews;
- after enablement, a new open non-draft PR queues an AppServer
  `reviewAnalysis` run;
- after enablement, each observed PR head SHA change queues one new review
  round for the latest head;
- auto re-review must match manual `reReview` in every respect (§3);
- skip draft, closed, merged, archived, rejected, active-run, non-PR, non-GitHub
  and missing-workspace-route items, and record operator-visible skip or error
  state;
- if a new head appears while a review run is active, record the latest
  observed head and queue exactly one follow-up round for that latest head after
  the active run completes.

---

## 8. AppServer, Hub, and Prompt Contract

Oratorio uses DotCraft AppServer as the runtime boundary.

Required AppServer interactions:

- establish an initialized SDK connection with automatic Wire reconnect disabled;
- resolve a workspace AppServer endpoint through Hub when available and fall
  back to explicit configuration when Hub cannot provide one;
- start a new thread or reuse a compatible existing thread for an item;
- declare Oratorio-owned Runtime Dynamic Tools through `thread/start.dynamicTools`
  when a round requires thread-scoped callbacks such as PR review drafts,
  implementation drafts, follow-up drafts, or discussion replies;
- declare Oratorio-owned, versioned, thread-stable runtime guidance through
  `thread/start.additionalContext` and rebind the same guidance through
  `thread/resume.additionalContext` for reused threads;
- for accepted App Binding board-tool grants, attach the tools and upsert a
  model-visible App Context Block that tells DotCraft to search/load Oratorio
  board tools before answering board or task-management requests;
- attach per-thread or plugin-bundled MCP tools through `mcpServers` only when a
  round uses external reusable services that are not submitting back into
  a specific Oratorio run;
- submit the rendered prompt as a turn;
- subscribe to thread and turn events;
- map turn completion, failure, cancellation, timeout, and disconnection into
  Oratorio run status;
- treat an SDK Run disconnect as `appServerDisconnected`, then use the
  bounded retry flow to resume and subscribe without replaying the original
  `turn/start`;
- reconstruct an empty Status drawer from one bounded page of the newest
  persisted Items; the drawer is a recent-activity surface and must not page
  back through complete thread history;
- when an Oratorio AppServer run timeout fires after a turn has started, request
  a DotCraft turn interrupt and wait for a terminal notification or a short
  bounded acknowledgement window before closing the run;
- keep Oratorio's timeout budget authoritative: a DotCraft completion that
  arrives after Oratorio has timed out is recorded as a late terminal signal and
  must not convert the Oratorio run back to success;
- record thread ID, turn ID, prompt context, summary, and error details.

Prompt context for real AppServer rounds must include:

- current source item snapshot;
- current round and attempt metadata;
- operator dispatch note;
- imported source comments;
- Oratorio operator comments;
- prior run summaries and errors;
- workspace, repository, branch, and head SHA metadata when available.

For an implementation run on an originating item that has a linked generated pull
request (the Implementation Follow-up loop, §3, §6.2), the per-turn prompt's
feedback section must additionally include the generated PR's still-open published
review findings — `findingId`, severity, title, path, line, and the
`suggestionReplacement` text for concrete code suggestions — together with the
human PR review comments added since the previous follow-up round. The prompt
instructs the agent to address them on the existing PR branch. The implementation
agent does not resolve findings itself: finding resolution stays bound to the
generated PR per §6.4, so the follow-up push changes the PR head and the
subsequent PR review round resolves the findings it confirms fixed. This is the
only place where one item's prompt references another item's review
state, and it is bounded to the originating-item → generated-PR parent link. Only
findings from a published review draft participate; unpublished drafts do not
trigger or feed the loop. This per-run, cross-item feedback stays in the user-turn
request, never in thread-stable runtime additional context.

The stored prompt context is audit data and may contain full structured
context. The operator-visible prompt is compact prose covering the review
target, the source description, new operator feedback, the current task, and the
available tools.

The agent-facing prompt must not include a full serialized `Context JSON:`
section, full round history, full source snapshot payload, full imported comment
history, or all prior summaries unless a specific product change intentionally
changes the prompt contract. Stable Oratorio run rules, source-write boundaries,
Review Draft formatting rules, implementation draft submission rules, follow-up
draft submission rules, and Discussion Turn tool-use rules belong in runtime
additional context rather than in each turn's user request. Runtime additional
context is thread-lifecycle context: it must be stable for the thread and must
not include per-run or per-turn facts such as concrete discussion turn IDs,
operator questions, open finding state, source head SHAs, or whether a specific
review diff snapshot is currently available. If the AppServer does not advertise
`runtimeAdditionalContext`, Oratorio-created runs must fail with
`runtimeAdditionalContextUnsupported` instead of falling back to prompt
injection.

Thread reuse contract:

- Oratorio may reuse the latest successful AppServer run thread for the same
  item when the previous run used compact prompt mode, the workspace path is the
  same, and required Dynamic Tools match exactly.
- If no compatible thread is found, Oratorio creates a new compact-prompt
  thread and records the reason in the timeline.
- Reused threads still create a new turn and a new Oratorio run.
- New threads receive full compact context. Reused threads receive only the
  incremental user request, new operator feedback, source deltas, and required
  metadata needed for the next turn; they must not repeat the full compact
  prompt that was already injected into the thread.
- Re-review runs caused by a changed pull request head use ordinary review run
  prompts. When a compatible thread is reused, the incremental operator input
  must state the old and new head SHAs and ask the agent to re-review the latest
  head while focusing on new changes when useful.
- Before starting a turn on a reused thread, Oratorio resumes the AppServer
  thread with the current run's Dynamic Tools and the same versioned Oratorio
  runtime additional context used for the thread lifecycle. If the server cannot
  rebind tools, Oratorio creates a fresh thread instead of reusing a stale
  callback binding. If the server cannot accept runtime additional context,
  Oratorio fails the run. Threads whose stored prompt context has an older
  runtime context version are not eligible for reuse.
- Tool calls remain bound to the current run, round, connection, and thread.
  Stale or mismatched calls must fail with a stable error.
- `oratorio_run.SubmitDiscussionReply` is declared on every new Oratorio-created
  AppServer thread, but it is accepted only for the currently bound
  Discussion Turn thread and turn. Calls made during ordinary review or
  implementation runs without a pending Discussion Turn must fail with a stable
  error.
- Hub is used for AppServer endpoint discovery, not as a message relay or
  security boundary. Oratorio resolves a configured repository workspace path,
  asks Hub for the workspace's AppServer endpoint, then connects directly to that
  AppServer.
- In remote-controlled Desktop topology, Hub discovery and explicit AppServer
  endpoints are evaluated from the remote backend host or container, not from the
  operator's local Desktop machine.
- Repository workspace routing must support a single configured workspace and
  explicit `owner/name` to absolute workspace path mappings. There is no
  implicit fallback workspace route.
- A configured absolute workspace path may be temporarily unavailable. Settings
  preserves that route so an operator can rebind or remove it; runtime probes and
  execution, rather than configuration persistence, determine availability.
- When Hub is unavailable, an explicit AppServer endpoint may be used for mapped
  workspaces; it does not imply a fallback workspace path. If no endpoint can be
  resolved for a mapped workspace, status surfaces and runs must report the
  stable reason `workspaceNotRegisteredInHub`.

Managed worktree and concurrency contract:

- AppServer runs use Oratorio-managed Git worktrees by default. Mock runs do
  not require a worktree.
- Hub and AppServer endpoint discovery use the mapped repository checkout.
  DotCraft thread state remains owned by that base workspace; the execution
  workspace uses the managed worktree through `executionWorkspaceOverride`.
- If the base checkout is missing or is not a Git repository, preparation fails
  clearly and Oratorio must not silently dispatch against a shared workspace.
- Fetching a review target ref that is absent from the mapped checkout uses the
  same Oratorio-owned source credentials as the write path in §5.2. When no
  credential is configured for the project, the fetch falls back to anonymous
  transport and therefore succeeds only for public repositories; preparation
  then fails with `reviewTargetFetchFailed` and names the missing credential.
- Worktree identity is deterministic per work item so repeated rounds can reuse
  the same isolated workspace when it is clean and valid.
- Worktrees live under the configured Oratorio-managed root. By default this is
  `<repositoryWorkspace>/.craft/oratorio/worktrees`.
- Managed work branches use the `oratorio/run` prefix. Their full deterministic
  name is `oratorio/run/<work-item-key>`; `oratorio/run` is not itself a shared
  default branch.
- Reuse must validate the existing worktree. Dirty or invalid worktrees fail
  preparation with operator-visible errors instead of destructive cleanup.
- Each run records the base workspace path, worktree path, worktree branch,
  requested base ref, resolved base SHA, worktree status, error details, retry
  count, next retry time, lease owner, and lease acquisition time.
- A run's worktree is in exactly one state: not required, preparing, ready,
  cleanup pending, cleaned, or failed.
- AppServer scheduling uses explicit leases and configurable capacity limits at
  global, repository, and source levels.
- AppServer runs interrupted by backend restart are reconciled as failed or
  retried according to the retry policy. Stale heartbeats trigger stalled-run
  handling.
- Transient preparation, AppServer, timeout, disconnection, and stalled-run
  failures may schedule bounded retries with exponential backoff capped at five
  minutes.
- `MaxRunAttempts` is the maximum total number of runs in the automatic retry
  set, including the initial run. Intermediate attempt failures keep the round
  and source review gate pending; only success or exhausted retries complete
  the gate.
- A queued PR/MR review retry remains bound to its target head SHA. If source
  sync observes a different head before the retry starts, Oratorio cancels the
  queued run with `reviewRetrySuperseded`, marks the round `Superseded`, closes
  the old head's review gate, and leaves the new head eligible for a new review
  round with a fresh attempt budget.
- Successful managed worktrees are retained briefly for inspection and then
  cleaned. Failed or timed-out worktrees are retained longer for debugging.
  Cleanup is allowed only for persisted Oratorio-managed paths under the
  configured root.

Review analysis runs must preserve the read-only safety posture: no GitHub
writes by the agent, no merges, no branch or commit creation, no pull requests,
and no workspace file mutation. Implementation runs are the narrow exception:
the agent may modify only the selected Oratorio-managed execution worktree, and
Oratorio remains responsible for commit, push, pull request creation, and source
write audit.

When PR review suggestion drafting is enabled, prompts must instruct the agent
to call the available `oratorio_run.SubmitReviewDraft` tool instead of embedding
machine-readable review JSON in the final answer. GitHub PR and GitLab MR
review runs must always call the tool, including clean reviews with zero inline
comments. Inline findings must target commentable changed/context lines from
the PR/MR diff rather than arbitrary full-file line numbers. If
`oratorio_run.SubmitReviewDraft` fails with `reviewDraftAnchorNotCommentable`, the prompt
must require the agent to choose from the returned ranges and call the tool
again before completing the turn. Final summaries should describe what was
submitted and any warnings that remain, while the tool call remains the
canonical structured delivery channel.

---

## 9. Desktop Renderer Behavior Contract

The Desktop renderer is Oratorio's operator surface for the domain and API
capabilities defined in this document. It must make the queue, source identity,
round history, run status, review decisions, source writes, review drafts,
local tasks, source status, and settings visibility accessible to operators.

Core renderer behavior:

- The board uses one vocabulary for columns, cards, filters, empty states, drag
  feedback, and undo feedback.
- The Active board renders only active columns; cancelled and archived work is
  reached through explicit list views with paged loading.
- The Status Drawer uses compact sections for task metadata, latest run state,
  source metadata, artifact counts, and board-safe actions.
- The board header keeps the Oratorio logo visible and exposes Settings as the
  only non-board navigation entry.
- Hover, pressed, focus, selected, busy, success, disabled, validation, and error
  states follow the shared DotCraft Desktop design system.

Oratorio-specific layout, navigation information architecture, density,
responsiveness, and frontend acceptance criteria are owned by
[`oratorio-frontend.md`](./oratorio-frontend.md). Shared components, theming,
tokens, and interaction styling are owned by
[`DESIGN.md`](../../architecture/DESIGN.md).

This document owns product transitions, lifecycle states, API validation,
source/write semantics, prompt and AppServer behavior, audit records, and
capability boundaries. The renderer must not invent product transitions,
lifecycle states, domain fields, or validation rules that are not defined in
the contracts above.

---

## 10. Operations and Validation

Operational requirements:

- Self-hosted Oratorio deployments require operator authentication, encrypted
  secret handling for GitHub App and AppServer credentials, health checks, logs,
  backup and restore guidance, and a documented single-node operating model.
- Enterprise SSO, hosted SaaS assumptions, and broad deployment administration
  UX are out of scope unless a separate product contract selects them.

Validation expectations:

- Every contract in this document — lifecycle transitions, API validation, the
  Review Draft and resolution contracts, prompt and thread-reuse behavior,
  scheduler eligibility, and delivery — is verified against the persisted audit
  record, not only against the API response.
- Source writes are exercised against fake source adapters before any
  credentialed run, and delivery must be shown to use Oratorio-owned GitHub App
  credentials rather than agent-owned or ambient local credentials.
- Settings diagnostics payloads are verified redacted, and configuration rows
  must degrade visibly when a backend capability is unavailable.
- Frontend changes must additionally satisfy the acceptance checklist in
  [`oratorio-frontend.md`](./oratorio-frontend.md), including
  `npm run build`, light/dark parity, breakpoint coverage, and the loading,
  empty, and error states defined for every surface.
