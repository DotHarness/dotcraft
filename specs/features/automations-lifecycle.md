# Automations lifecycle

| Field | Value |
|---|---|
| Version | 1.0.0 |
| Status | Living |
| Date | 2026-09-08 |

Automations is the single workspace capability for scheduled agent work. The optional
DotCraft.Automations module owns storage, scheduling, execution, presets and delivery.
Hosts supply their existing ISessionService. Core and Runtime do not depend on the module.

## Contracts

JSON uses camelCase. AutomationDefinition has id, version (positive integer), name,
prompt, status (active/paused/completed), executionMode (thread/independent), nullable
targetThreadId, workspaceMode (project/worktree), optional agentProfileId,
approvalPolicy (workspaceScope/fullAuto), schedule, notificationPolicy (important/all/failures),
optional origin, createdAt, updatedAt and nullable nextRunAt (UTC ISO timestamps).
Origin preserves channel, userId, groupId and deliveryTarget independently of execution.
The host captures origin; models cannot impersonate a creator.

Completed is a server-owned terminal state for a one-shot definition after its attempt.
Create and update inputs accept active or paused only. A completed definition is read-only;
it may be run manually or deleted, but it cannot be edited, paused or resumed.

AutomationRun has id, automationId, definitionVersion, status
(queued/running/succeeded/failed/cancelled/interrupted), createdAt, nullable startedAt,
completedAt, scheduledAt (null for manual runs), threadId, turnId, summary, error and worktree, plus deliveryStatus
(pending/sent/skipped/failed) and nullable deliveryError. Worktrees belong to runs.

Store definitions atomically at .craft/automations/<id>/automation.json, memory.md and
runs/<runId>.json. Only this namespace is loaded; other `.craft` data remains untouched.
The prompt is the only execution content.

| Method | Parameters | Result |
|---|---|---|
| automation/list | {} | { automations } |
| automation/read | { automationId } | { automation } |
| automation/create | { automation } (editable fields) | { automation } |
| automation/update | { automationId, expectedVersion, automation } | { automation } |
| automation/delete | { automationId } | { ok } |
| automation/run | { automationId } | { run } |
| automation/runs/list | { automationId } | { runs } |
| automation/presets/list | { locale? } | { presets } |

All require the automations capability. Update checks expectedVersion and rejects
stale writes with `automation.versionConflict` (-32056). Missing definitions return
`automation.notFound` (-32051); unavailable lifecycle operations use -32052 with a
stable `automation.*` code. Invalid definitions use -32602 with a specific validation
code. Error data contains the stable code/messageKey and an English fallback. Server-owned id, version, origin and timestamps are not editable.
automation/updated sends { automationId, automation?, removed };
automation/run/updated sends { run }. Reconnection reloads a snapshot.

The Automation tool calls the same service for list/read/create/update/pause/resume/
delete/run, returning { operation, automation?, run? }. Its generated function schema
uses the same JSON representation as AppServer: `schedule.at` is an ISO 8601
`string` with `date-time` format, never an object shaped from CLR date properties.
`notificationPolicy` belongs to the nested editable automation definition. At the
model-tool boundary, blank optional identifiers and modes are treated as omitted,
blank approval policy uses `workspaceScope`, and thread mode without a target binds
the trusted thread from the tool planning snapshot. AppServer validation remains strict.
Trusted core.automation presentation selects client cards. Queue acceptance is not run success.
The deterministic /automate list|show|pause|resume|run|remove command is registered
by the module as ICommandHandler. Its registration supplies a stable description key
and module-owned English fallback for command discovery and dynamic channel menus.
Runtime registers DI handlers using the Core contract.
Disabled modules expose neither commands nor tools.

## Schedule and execution

AutomationSchedule has kind (at/every/daily/weekdays/weekly), nullable at (UTC ISO),
everyMs, hour, minute, timeZone, days (ISO Monday=1 through Sunday=7).
Wall-clock schedules require a valid explicit time zone. Desktop creation resolves the
current system IANA time zone, falling back to UTC, and persists it without exposing a
routine time-zone field. Editing preserves the stored zone. Weekly means calendar dates,
not 168 hours. Interval schedules advance from planned times, not completion times.
DST gaps advance to the next valid local instant; repeated local times fire once.

A definition runs serially. Missed triggers coalesce into at most one run, including
restart recovery. A durable run claim records scheduledAt so recovery advances an
occurrence even if the host stopped before saving the definition nextRunAt. Busy target conversations wait without interrupting user work.
Pause prevents subsequent dispatch and does not cancel active work. Edits apply to
later runs; current runs use a definition snapshot. Manual run preserves paused state
and periodic nextRunAt. Running definitions cannot be deleted. One-shot definitions
complete after the attempt and retain history. Failures are not automatically replayed;
periodic jobs continue at the next occurrence. Interrupted runs are recorded on restart.

Follow-ups default to thread mode and continue the specified conversation. Independent
runs create a new conversation each time and share automation memory. Record exact
turn ids. Git workspaces default to a fresh worktree per run; non-Git use project mode.
Explicit worktree provisioning failure is an error, never a silent fallback. Missing or
archived targets and missing profiles fail clearly. Bound threads inherit capabilities;
independent runs resolve the selected profile and apply unattended workspace policy.
Ordinary turn completion ends the run; no CompleteLocalTask or workflow iteration.

The host must be online. This feature provides no cloud execution. Deletion stops future
work but preserves historical conversations and dirty worktrees. Conservative clean
worktree retention remains available.

## Delivery and user experience

Follow-ups default to important notifications, independent runs to all results. Failures
qualify for notification. An optional structured outcome may mark unchanged successful
work as non-important; missing importance is conservatively important. Every run persists.
MessageRouter uses saved origin and existing channel delivery formats, including group
targets. Persist the execution terminal state before attempting delivery. Delivery failure is
distinct from execution failure and cannot replay agent work. If the host stops during
delivery, recovery marks the pending delivery as failed without resending or rerunning.

Creation defaults to conversation. The primary action opens the Welcome composer with a
localized starting instruction and an explicit `$automations` skill reference; the built-in
skill teaches the unified Automation tool and is available only when that tool is available.
The Automations page does not insert an intermediate creation banner. Manual creation and
editing share a detail panel:
editable title, prompt, detail rows, frequency rows and advanced settings. Dirty drafts
show Save/Cancel; failed saves preserve drafts. Fields depend on execution mode. Presets
contain name, prompt and optional defaults and seed conversation without creating a job.
The browse surface uses the DotCraft catalog title and search/filter controls. Existing
definitions precede suggestions when present; the empty surface leads with suggestions.
The detail rail preserves both the definition list and suggestions. Active uses the small
accent status treatment. Pause/resume is a direct toolbar action; the overflow contains
icon-labelled Run now and Delete actions. Completed definitions render as read-only history.
Desktop uses a menu-based, locale-formatted time selector rather than native time input
chrome, while the stored time zone remains explicit in the definition.
Cards preserve the operation snapshot and open the latest definition; deletion is handled
without stale navigation. Run navigation uses automation id and run id, locating the exact
turn for follow-ups. triggerKind is automation; triggerRefId is the definition id.
Production components mount in the maintained design system with deterministic fixtures.
