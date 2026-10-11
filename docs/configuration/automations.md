# Automations and goals

For the concept and everyday use, read [Automations & Goals](../guide/automations).

## Fields

| Field | Description | Default |
|-------|-------------|---------|
| `Automations.Enabled` | Enables the Automations orchestrator | `true` |
| `Automations.PollingInterval` | Polling interval | `00:00:10` |
| `Automations.MaxConcurrentTasks` | Maximum concurrent local tasks | `3` |
| `Automations.TurnTimeout` | Single-turn timeout | `00:30:00` |
| `Automations.WorktreeRetentionEnabled` | Enables retention cleanup for idle automation task worktrees | `true` |
| `Automations.WorktreeRetentionIdlePeriod` | Idle period before a clean automation task worktree is eligible for cleanup | `21.00:00:00` |
| `Goals.Enabled` | Enables goal storage, AppServer methods, goal context injection, usage accounting, and model goal tools | `true` |
| `Goals.AutoContinueEnabled` | Allows active goals to continue when a Thread is idle | `true` |

`Automations.WorktreeRetentionIdlePeriod` must be at least `14.00:00:00`. The retention sweep only removes managed automation task worktrees that are idle, clean, and have no commits ahead of their base.

Automation AppServer methods:

| Method | Description |
|---|---|
| `automation/list` | List definitions |
| `automation/read` | Read a definition |
| `automation/create` | Create an automation |
| `automation/update` | Save with expectedVersion |
| `automation/run` | Queue one run |
| `automation/delete` | Delete a definition |
| `automation/runs/list` | Read run history |
| `automation/presets/list` | List conversational presets |

Goal AppServer methods:

| Method | Description |
|---|---|
| `thread/goal/set` | Set, replace, pause, or resume a Thread goal |
| `thread/goal/get` | Read current Thread goal state |
| `thread/goal/clear` | Clear current Thread goal |
