---
name: automations
description: Create, inspect, update, pause, resume, run, or delete DotCraft automations using the Automation tool. Use for reminders, recurring work, scheduled monitoring, and conversation follow-ups.
allowed-tools: Automation
---

# Automations

Use `Automation` for work that should run later or repeatedly. Keep the user's requested task and timing in the editable definition; do not create a second workflow inside the prompt.

## Create

1. Use `thread` for follow-ups that depend on this conversation and `independent` for standalone work that starts a fresh conversation each run.
2. Resolve the schedule and, for calendar schedules, its time zone. Ask only when timing is genuinely ambiguous.
3. Call `Automation` with `action: "create"` and one complete definition.

| Kind | Required schedule fields |
|---|---|
| `at` | `kind`, absolute ISO 8601 string `at` |
| `every` | `kind`, positive `everyMs` |
| `daily`, `weekdays` | `kind`, `hour`, `minute`, `timeZone` |
| `weekly` | `kind`, `hour`, `minute`, `timeZone`, ISO weekday `days` |

Example:

```json
{"action":"create","automation":{"name":"Build reminder","prompt":"Check the build and summarize failures.","status":"active","executionMode":"independent","schedule":{"kind":"at","at":"2030-01-15T09:00:00Z"},"notificationPolicy":"all"}}
```

`schedule.at` is a string, never a CLR-style object. `notificationPolicy` belongs inside `automation`. Keep unrelated schedule fields out. Omit `workspaceMode` and `agentProfileId` when unset; `approvalPolicy` is `workspaceScope` or `fullAuto`. Thread mode binds the current conversation when `targetThreadId` is omitted. Independent Git work defaults to a new worktree per run. Notifications default to important changes for thread mode and every result for independent mode.

## Manage

Use `list` or `read` before changing a definition when its version is unknown. Updates require `expectedVersion`. Use `pause`, `resume`, or `delete` for lifecycle changes. Completed is assigned by DotCraft after a one-shot run and is read-only. `run` queues an execution; report it as queued.

Use supported schedule fields instead of arbitrary RRULE expressions. Do not retry task execution because result delivery failed.

## Validation recovery

- Put a rejected root `notificationPolicy` inside `automation`.
- For an invalid `schedule.at`, retry with one ISO 8601 string; do not invent object fields such as `kind` or `dateTime` inside `at`.
