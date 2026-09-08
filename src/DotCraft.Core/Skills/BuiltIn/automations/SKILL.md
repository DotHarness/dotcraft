---
name: automations
description: Create, inspect, update, pause, resume, run, or remove DotCraft automations using the Automation tool. Use for reminders, recurring work, scheduled monitoring, and follow-ups tied to a conversation.
allowed-tools: Automation
---

# Automations

Use `Automation` for work that should run later or repeatedly. Keep the user's requested task and timing in the editable definition; do not create a second workflow inside the prompt.

## Create

1. Decide whether the automation follows the current conversation (`thread`) or starts a fresh conversation for each run (`independent`). Use `thread` for follow-ups that depend on this conversation. Use `independent` for standalone scheduled work.
2. Resolve the schedule and time zone. Ask only when the requested timing is genuinely ambiguous.
3. Call `Automation` with `action: "create"` and one complete definition.

Supported schedules:

- `at`: set an absolute ISO 8601 `at` timestamp.
- `every`: set a positive `everyMs` interval.
- `daily` or `weekdays`: set `hour`, `minute`, and an explicit `timeZone`.
- `weekly`: also set ISO `days`, where Monday is `1` and Sunday is `7`.

For thread mode, the tool binds the current conversation when `targetThreadId` is omitted. Independent Git work defaults to a new worktree for each run. Notifications default to important changes for thread mode and every result for independent mode.

## Manage

Use `list` or `read` before changing a definition when its current version is unknown. Updates require `expectedVersion`. Use `pause`, `resume`, or `delete` for lifecycle changes. Completed is a read-only state assigned by DotCraft after a one-shot automation runs. `run` queues an execution; report it as queued rather than completed.

Use supported schedule fields instead of arbitrary RRULE expressions. Do not retry task execution because result delivery failed.
