---
name: cron
description: Use when scheduling, listing, removing, or troubleshooting DotCraft Cron jobs with the Cron tool; choose scheduleKind at, every, or daily, set required schedule fields, configure delivery, and recover from Cron validation errors.
---

# Cron

Use `Cron` for scheduled background agent runs that should happen later or repeatedly. Do not use it for a short blocking wait inside the current turn.

## Workflow

1. Choose `action`: `add`, `list`, or `remove`.
2. For `add`, always include `scheduleKind`.
3. Keep the schedule fields for the selected kind readable; ignore unrelated auto-filled schedule fields because `scheduleKind` is authoritative.
4. Use `list` before `remove` when you do not know the job id.

## Add Jobs

| Kind | Required fields | Optional fields | Use for |
|------|-----------------|-----------------|---------|
| `at` | `message`, `delaySeconds` | `name`, `deliver`, `channel`, `toUser` | One-time delay jobs |
| `every` | `message`, `everySeconds` | `delaySeconds`, `name`, `deliver`, `channel`, `toUser` | Recurring interval jobs |
| `daily` | `message`, `dailyTime` or `dailyHour` | `dailyMinute`, `timeZone`, `name`, `deliver`, `channel`, `toUser` | Fixed local clock time jobs |

Examples:

```text
Cron(action: "add", scheduleKind: "at", message: "Remind me to check the build", delaySeconds: 600)
Cron(action: "add", scheduleKind: "every", message: "Summarize CI status", everySeconds: 3600)
Cron(action: "add", scheduleKind: "every", message: "Ping service", everySeconds: 3600, delaySeconds: 300)
Cron(action: "add", scheduleKind: "daily", message: "Prepare standup notes", dailyTime: "09:30", timeZone: "Asia/Hong_Kong")
```

## Delivery

By default, `deliver` is `true` and DotCraft sends the run result back to the creator's current channel or session target. Set `deliver: false` only when the job should update state without notifying the user. Use `channel` and `toUser` only when the user explicitly wants a different delivery target.

## List And Remove

```text
Cron(action: "list")
Cron(action: "remove", jobId: "abc12345")
```

## Validation Recovery

- Missing `scheduleKind`: retry with `at`, `every`, or `daily`.
- `scheduleKind: "at"` requires a positive `delaySeconds`.
- `scheduleKind: "every"` requires a positive `everySeconds`; `delaySeconds` is only the first-run delay.
- `scheduleKind: "daily"` requires `dailyTime` or `dailyHour`; `dailyMinute` defaults to `0`.
- Daily time zones should be IANA ids such as `Asia/Hong_Kong`; omitted means UTC.
