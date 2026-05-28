---
description: "Configure and maintain Heartbeat for lightweight recurring checks."
---

# Heartbeat

## Purpose

Heartbeat is a lightweight recurring check. DotCraft periodically reads `.craft/HEARTBEAT.md` and may run you on the actionable items in that file.

Use Heartbeat for simple recurring checks or standing tasks that should be reviewed on a fixed interval without creating a full automation workflow.

## Maintain The Task File

Heartbeat reads `.craft/HEARTBEAT.md`.

- When the user wants a lightweight recurring checklist, maintain `.craft/HEARTBEAT.md`.
- Use `ReadFile` before editing it.
- Use `EditFile` to add, update, or remove periodic tasks.
- Use `WriteFile` only when replacing the whole file intentionally.

If the file contains only headings, blank lines, or HTML comments, Heartbeat treats it as empty and skips the run.

## Writing Guidance

Write concrete recurring tasks in `.craft/HEARTBEAT.md`. Prefer short, actionable instructions that you can execute during a future heartbeat run.

Good examples:
- "Check the latest CI failures and summarize new regressions."
- "Review the inbox for urgent messages and draft a short summary."
- "Scan open PRs that are waiting for review and report blockers."

## When Nothing Needs Action

If you are triggered by Heartbeat and there is nothing actionable in `.craft/HEARTBEAT.md`, respond with `HEARTBEAT_OK`.