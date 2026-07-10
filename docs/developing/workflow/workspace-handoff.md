# Workspace Handoff

DotCraft workspaces are designed to cooperate with other coding agents. The `.craft/` directory keeps the project rules, memory, session history, trace metadata, and tool evidence close to the repository, so you can hand off useful context without asking another agent to guess from chat fragments.

This page is for integrators and contributors who move a DotCraft thread into another coding agent. It covers finding the right thread, exporting it as a Markdown handoff, and what that export contains.

There are two collaboration modes:

| Mode | Use when | Interface |
|---|---|---|
| Live client | The other tool can integrate with DotCraft directly | AppServer, ACP, SDKs, Desktop |
| Handoff document | The other coding agent only accepts files or prompt context | `dotcraft context` Markdown export |

This page focuses on the handoff document flow. For live clients, see [Unified Session Core](../architecture/session-core) and [AppServer Mode](../lifecycle/appserver).

## What To Share

A good external-agent handoff usually includes:

- The repository or the exact files the external agent may inspect.
- A `dotcraft context export` Markdown file for the relevant thread.
- The concrete task you want the external agent to do next.
- Any privacy constraints, such as whether tool outputs or memory history may be included.

Do not share provider credentials, global `~/.craft/config.json`, or raw state databases unless you explicitly trust the receiver and need forensic debugging. The Markdown export is the safer default because it is curated and has redaction controls.

## Find The Right Thread

When you know only a symptom, error, tool name, model id, or partial user request, search the workspace first:

```bash
dotcraft context search --query "provider timeout gpt-5.3" --workspace "D:\path\to\project" --limit 5
```

Search reads the workspace state DB first, including thread metadata, trace session bindings, trace session counters, and trace events. It then adds short rollout snippets for candidates. Results include an export command when the matching thread has a rollout file.

Use `--json` when another script or tool should consume the result:

```bash
dotcraft context search --query "thread_20260601" --workspace "D:\path\to\project" --json
```

## Export A Handoff

Once you have a thread id:

```bash
dotcraft context export --thread thread_20260601_ab12cd --workspace "D:\path\to\project" --output handoff.md
```

By default the export uses:

| Option | Default | Why |
|---|---|---|
| `--profile` | `handoff` | Keeps the document shaped for another coding agent |
| `--tool-results` | `summary` | Preserves evidence without dumping full command or API output |
| `--history` | `tail` | Includes recent memory history without dragging in every old event |

For a stricter handoff:

```bash
dotcraft context export --thread thread_20260601_ab12cd --tool-results none --history tail --output handoff.md
```

For a full audit transcript:

```bash
dotcraft context export --thread thread_20260601_ab12cd --profile transcript --tool-results full --history full --output transcript.md
```

If `--output` is omitted, Markdown is written to stdout.

## What The Export Contains

The Markdown export includes:

- Export metadata: workspace, `.craft` path, rollout path, profile, and privacy modes.
- Thread metadata: status, timestamps, origin channel, display name, and turn count.
- Workspace memory: `MEMORY.md`, plus `HISTORY.md` according to the selected history mode.
- Continuity events: rollback and compaction records that affect context continuity.
- Current model-visible context: reconstructed from the latest usable compaction checkpoint plus surviving tail turns.
- Conversation: surviving turns replayed from canonical rollout JSONL.

Reasoning content is omitted. Tool calls are kept. Tool and command results follow `--tool-results`.

## Rollback And Compaction

DotCraft does not treat rollout JSONL as a naive append-only transcript during export. It replays the canonical events:

- `thread_rolled_back` removes the rolled-back tail turns from the exported conversation and adds a continuity note.
- `context_compacted` records are listed as continuity events.
- The latest decodable compaction checkpoint whose covered turn still survives rollback is used for the "Current Model-Visible Context" section.
- If a checkpoint is corrupt or no longer applies, the exporter falls back to surviving rollout turns and prints a warning.

This matters because another coding agent needs the context that DotCraft would actually continue from, not stale turns that were already rolled back.

## Handoff Checklist

Before sending context to another coding agent:

1. Run `dotcraft context search` if the exact thread id is uncertain.
2. Export with `--tool-results summary` unless full outputs are needed.
3. Open the Markdown briefly and check for sensitive output.
4. Mention continuity warnings, especially rollback or ignored compaction checkpoints.
5. Tell the external agent what to do next and which files it may modify.

## Related

- [Project Workspace](../../features/project-first)
- [Unified Session Core](../architecture/session-core)
- [Observability](../../features/self-hosted/observability)
- [AppServer Mode](../lifecycle/appserver)
