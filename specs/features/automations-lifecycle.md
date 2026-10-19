# Automations lifecycle

| Field | Value |
|---|---|
| Version | 0.9.1 |
| Status | Living |
| Date | 2026-10-19 |
| Parent Specs | [Session Core](../architecture/session-core.md), [AppServer Protocol](../protocols/appserver-protocol.md) |

Automations is the single workspace capability for scheduled agent work. The optional
Automations module owns storage, scheduling, execution, presets and delivery, running work
through the host's existing session service. Core and Runtime do not depend on the module;
a disabled module exposes neither commands nor tools.

## Contracts

An automation definition has a server-owned id, positive version, origin and timestamps, and
editable name, prompt, status, execution mode, optional target thread, workspace mode, optional
agent profile, approval policy, schedule and notification policy. Enumerations:

| Field | Values |
|---|---|
| status | active, paused, completed |
| executionMode | thread, independent |
| workspaceMode | project, worktree |
| approvalPolicy | workspaceScope, fullAuto |
| notificationPolicy | important, all, failures |

Runs auto-approve their tool calls. Under workspaceScope, file and shell tools are refused
outside the run's workspace; under fullAuto, access outside it is auto-approved.

Origin preserves channel, userId, groupId and deliveryTarget independently of execution. An
origin with a channel and a deliveryTarget is a channel origin. An origin captured from a chat may
also carry that chat's kind and label; without a kind, the run's chat is unspecified. The host
captures origin; models cannot impersonate a creator or change the chat a run speaks to.

Completed is a server-owned terminal state for a one-shot definition after its attempt. Create
and update accept only active or paused. A completed definition is read-only: it may be run
manually or deleted, but not edited, paused or resumed.

A run records its definition version, status (queued, running, succeeded, failed, cancelled,
interrupted), timestamps including scheduledAt (null for manual runs), thread and turn, summary,
error, worktree, and delivery status (pending, sent, skipped, failed) with delivery error.
Worktrees belong to runs.

Definitions are stored atomically at `.craft/automations/<id>/automation.json` with runs at
`runs/<runId>.json`; nothing else under `.craft` is touched. The prompt is the only execution
content.

| Method | Purpose |
|---|---|
| automation/list | List definitions. |
| automation/read | Read one definition. |
| automation/create | Create from editable fields. |
| automation/update | Update with `expectedVersion`. |
| automation/delete | Delete a definition. |
| automation/run | Queue a manual run. |
| automation/runs/list | List a definition's runs. |
| automation/runs/read | Set or clear read receipts for a batch of runs. |
| automation/presets/list | List localized presets. |

All require the automations capability. Stale updates fail with `automation.versionConflict`
(-32056); missing definitions with `automation.notFound` (-32051); unavailable lifecycle
operations with -32052 and a stable `automation.*` code; invalid definitions with -32602 and a
specific validation code. Error data carries the stable code/messageKey and an English fallback.
`automation/updated` reports a changed or removed definition; `automation/run/updated` reports a
run. Reconnection reloads a snapshot.

Run `readAt` is persisted separately from execution state; a missing receipt means unread. A
receipt refers to the observed completion timestamp, so execution writes never overwrite later
reads. A read validates the whole batch (1–200 distinct run IDs) before writing and publishes the
affected runs.

Previous runs derive archived state from their chat. Archiving and restoring use
`thread/archive` and `thread/unarchive`, affecting every run in that chat and never the schedule.
Running chats cannot be archived. Bulk archive deduplicates chats and reports partial failures.

The `Automation` model tool calls the same service for list, read, create, update, pause, resume,
delete and run, returning `{ operation, automation?, run? }`. Its schema uses the AppServer JSON
representation (`schedule.at` is an ISO 8601 date-time string), closed enums for every mode and
kind, and the same text and numeric bounds the service enforces. At the tool boundary, blank
optional identifiers are omitted, omitted modes use defaults, and thread mode without a target
binds the trusted thread from the tool planning snapshot; AppServer validation remains strict.
`notificationPolicy` applies only to definitions without a channel origin. The `core.automation`
presentation selects client cards. Queue acceptance is not run success.

The module registers the deterministic `/automate list|show|pause|resume|run|remove` command with
a stable description key and English fallback for command discovery and channel menus.

## Schedule and execution

A schedule has kind (at, every, daily, weekdays, weekly), `at` (UTC ISO), `everyMs`, `hour`,
`minute`, `timeZone`, and `days` (ISO Monday=1 through Sunday=7). Wall-clock schedules require a
valid explicit time zone; Desktop creation persists the system IANA zone (UTC fallback) and
editing preserves the stored zone. Weekly means calendar dates, not 168 hours. Intervals advance
from planned times, not completion times. DST gaps advance to the next valid local instant;
repeated local times fire once.

A definition runs serially. Missed triggers coalesce into at most one run, including after
restart. A durable run claim records scheduledAt so recovery advances the occurrence even if the
host stopped before saving nextRunAt. Busy target conversations wait without interrupting user
work. Pause prevents later dispatch without cancelling active work. Edits apply to later runs;
current runs use a definition snapshot. A manual run preserves paused state and periodic
nextRunAt. Running definitions cannot be deleted. One-shot definitions complete after the attempt
and keep history. Failures are never replayed; periodic jobs continue at the next occurrence.
Interrupted runs are recorded on restart.

Thread mode (the default for follow-ups) continues the target conversation, which carries
continuity. Independent runs create a new conversation each time, record the automation id as
their channel context, and name the automation as their memory scope so successive runs share one
agent-maintained long-term memory; memory extraction is disabled for them. A channel-origin run
conversation is bound to the origin chat; a thread-mode run uses the target conversation's own
chat binding. An independent channel-origin run may send to other chats only under fullAuto.

Exact turn ids are recorded. Git workspaces default to a fresh worktree per run; non-Git use
project mode. Worktree provisioning failure is an error, never a silent fallback. Missing or
archived targets and missing profiles fail clearly. Bound threads inherit capabilities;
independent runs resolve the selected profile and apply unattended workspace policy. Ordinary
turn completion ends the run. Reported summaries have a hard size limit.

The host must be online; there is no cloud execution. Deletion stops future work but preserves
historical conversations and dirty worktrees.

## Delivery and user experience

Every run persists. A channel-origin run speaks to its origin chat only through
`SendChannelMessage` and decides itself whether to say anything; the host delivers nothing after
success. When a channel-origin run fails, times out or is interrupted, the host sends one fixed
failure notice naming the automation, because the agent can no longer send. A run cancelled by a
pause before dispatch sends nothing. The summary of a channel-origin run is its last agent
message. Its deliveryStatus is sent when the failure notice was delivered, skipped on success,
and failed when the notice could not be delivered.

Runs without a channel origin use the Desktop result notification: follow-ups default to
important, independent runs to all. Failures always qualify. An optional structured outcome may
mark unchanged successful work as non-important; missing importance counts as important.

The execution terminal state persists before delivery. Delivery failure is distinct from
execution failure and never replays agent work. If the host stops during delivery, recovery marks
the delivery failed without resending or rerunning.

Creation defaults to conversation: the primary action opens the Welcome composer with a starting
instruction and an explicit `$automations` skill reference. The built-in skill teaches the
`Automation` tool and is available only with it. Presets contain a name, prompt and optional
defaults and seed a conversation without creating a job.

Manual creation and editing share a detail panel with editable title, prompt, schedule and
advanced settings whose fields depend on execution mode; channel-origin definitions show no
notification setting. Dirty drafts show Save/Cancel, and failed saves preserve drafts. Existing
definitions precede suggestions; an empty surface leads with suggestions. Pause/resume is a direct
action sharing pending state between list and detail; Run now and Delete are in the overflow.
Completed definitions render as read-only history. The task list shows the schedule and, for
active tasks only, the next-run countdown.

Cards keep the operation snapshot and open the latest definition, handling deletion without stale
navigation. Run navigation uses automation id and run id and locates the exact turn for
follow-ups; reading a run succeeds only after that turn is displayed. Run conversations use
`triggerKind = "automation"` and `triggerRefId` = the definition id.
