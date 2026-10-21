# Conversations

Queueing and interrupts while a turn runs, composer suggestions, context compaction, and custom commands.

## Queue and interrupts

| Field | Description | Default |
|-------|-------------|---------|
| `MaxSessionQueueSize` | Maximum queued requests per session; `0` means unlimited | `3` |
| `InstantInterruptEnabled` | Interrupt the current model request, even while it is thinking or replying, and restart it with a mid-turn message; also cuts short a wait to retry | `true` |
| `AgentInterruptMessageEnabled` | Tell the model when a turn is intentionally interrupted, including an active-turn fork | `true` |

## Suggestions

| Field | Description | Default |
|-------|-------------|---------|
| `WelcomeSuggestions.Enabled` | Generates welcome-screen suggestions from workspace memory. Requires `Memory.Enabled` | `true` |
| `PromptSuggestions.Enabled` | Suggests one next message in an empty conversation composer after a completed turn. Independent of workspace memory | `true` |

Both settings can be changed in **Settings → Personalization**. A prompt suggestion remains editable and is sent only when the user submits it.

## Compaction

| Field | Description | Default |
|-------|-------------|---------|
| `Compaction.AutoCompactEnabled` | Enables threshold-based auto compaction | `true` |
| `Compaction.ReactiveCompactEnabled` | Enables reactive compaction for `prompt_too_long` errors | `true` |
| `Compaction.MaxContextWindow` | Client context budget in tokens. `-1` uses the model catalog window; a positive value caps it | `-1` |
| `Compaction.SummaryReserveTokens` | Tokens reserved for summary output | `20000` |
| `Compaction.SummaryMaxOutputTokens` | Maximum output tokens for a compaction summary request | `12000` |
| `Compaction.AutoCompactBufferTokens` | Token buffer below the hard limit that triggers auto compaction | `13000` |
| `Compaction.PostTurnCompactThresholdPercent` | Percentage of the effective context window that also triggers compaction at the end of a Turn; `0` disables it | `0` |
| `Compaction.WarningBufferTokens` | Token buffer before auto threshold that emits warning | `20000` |
| `Compaction.ErrorBufferTokens` | Token buffer before auto threshold that emits error | `10000` |
| `Compaction.ManualCompactBufferTokens` | Headroom below the effective context window used for the reported context-pressure limit | `3000` |
| `Compaction.KeepRecentMinTokens` | Minimum recent tail tokens after partial summary | `10000` |
| `Compaction.KeepRecentMinGroups` | Minimum recent API groups after partial summary | `3` |
| `Compaction.KeepRecentMaxTokens` | Maximum recent tail tokens after partial summary | `40000` |
| `Compaction.MicrocompactEnabled` | Enables micro-compaction | `true` |
| `Compaction.MicrocompactKeepRecent` | Recent tool results kept during micro-compaction | `8` |
| `Compaction.MicrocompactGapMinutes` | Also triggers after this many minutes since last assistant message; `0` disables it | `20` |
| `Compaction.MaxConsecutiveFailures` | Consecutive failures before circuit breaking compaction | `3` |

Set the context budget in global or workspace `config.json`. For example, limit the client to 256K tokens:

```json
{
  "Compaction": {
    "MaxContextWindow": 256000
  }
}
```

Omitting the setting uses the model catalog window. Workspace settings override global settings;
set `MaxContextWindow` to `-1` in the workspace to clear an inherited limit. The budget cannot increase
model capacity. Summary reserves and safety buffers are deducted afterward, so this value is not
the exact automatic-compaction trigger. Reasoning effort does not change the budget.

A model's context window comes from the [model capability catalog](./models#model-capability-catalog).

## Custom commands

Custom commands are Markdown files rather than a `config.json` field. DotCraft loads them from the global `~/.craft/commands/` directory and the workspace `.craft/commands/` directory, and a workspace file wins a name collision. Each file becomes a `/name` command usable from the CLI, Desktop, and other entry points.

A command file may open with YAML frontmatter, which is stripped before the body is sent. Inside the body, `$ARGUMENTS` expands to the full argument string and `$1` through `$9` expand to positional arguments.
