---
max_rounds: 10
---

You are running a **local automation** workflow.

## Task

- **ID**: {{ task.id }}
- **Title**: {{ task.title }}

## Instructions

{{ task.description }}

## Workspace

The agent working directory is:

`{{ task.workspace_path }}`

Use file and shell tools as needed. When finished, call the **`CompleteLocalTask`** tool with a short summary of what you did. That sets `task.md` to `agent_completed` so the orchestrator stops the workflow (instead of running until `max_rounds`).
