# DotCraft Exec CLI Specification

| Field | Value |
|-------|-------|
| **Version** | 0.9.1 |
| **Status** | Draft |
| **Date** | 2026-10-19 |
| **Parent Spec** | [AppServer Protocol](../protocols/appserver-protocol.md) |
| **Related Specs** | [Hub Architecture](../architecture/hub-architecture.md), [Session Core](../architecture/session-core.md) |

Purpose: define `dotcraft exec`, the non-interactive command that runs agent work for scripts, CI, and other coding agents, and returns its result to the caller.

## 1. Overview

`dotcraft exec` submits one Turn in a DotCraft workspace and reports its outcome. It is an AppServer client. It connects through the local Hub, or to `--remote`, and never runs its own Session Core. Threads it creates are ordinary workspace threads, so Desktop and other clients see them and can continue them.

A typical caller is another coding agent driving DotCraft from its shell tool: it sends a task, reads back the answer, changed files, and generated images, and may later continue the thread or stop a running Turn.

## 2. Goals

- Make the result capturable: the final message goes to stdout, and failure is reported in the exit status.
- Continue a thread across invocations, by id or by "the latest one".
- Let the caller choose the model and attach images.
- Run unattended without approval prompts.
- Report generated images and file changes.
- Offer a machine-readable event stream.
- Stop a running Turn from a separate invocation.

## 3. Non-Goals

- No interactive approvals or user-input prompts.
- No sandbox modes. DotCraft has no OS sandbox.
- No automatic approval review.
- No structured-output schema.
- No running outside a DotCraft workspace.
- No MCP server.
- No AppServer protocol additions. Exec uses existing methods and fields only.
- No job store, job listing, or result cache.
- No agent profile or provider selection.

## 4. Command Contract

```text
dotcraft exec [options] [PROMPT | -]
dotcraft exec resume [options] (THREAD_ID | --last) [PROMPT | -]
dotcraft exec interrupt [-C DIR] [--remote URL [--token TOKEN]] THREAD_ID
```

**Prompt input:**

- When `PROMPT` is omitted or is `-`, the prompt is read from stdin.
- When a prompt is given and stdin is also piped, stdin is appended to the prompt as a `<stdin>` block.
- An empty prompt is a usage error.

### 4.1 Options

| Option | `exec` | `resume` | Meaning |
|--------|:------:|:--------:|---------|
| `-C, --cd <dir>` | yes | yes | Run in this workspace directory instead of the current directory (§5). |
| `-m, --model <id>` | yes | yes | Model on the workspace's default provider. With `resume`, it replaces the thread's model for this Turn and later ones. |
| `-i, --image <file>[,<file>…]` | yes | yes | Attach local images to the prompt. Repeatable, and also accepts a comma-separated list. Relative paths resolve against the current directory, not against `--cd`. A missing file is a usage error. |
| `--auto-approve` | yes | yes | Set the thread's approval policy to `autoApprove` (§7). With `resume`, it changes the thread's policy for this Turn and later ones. |
| `--ephemeral` | yes | no | Create the thread with `ephemeral: true`. It is not persisted and cannot be resumed after the AppServer exits. |
| `--json` | yes | yes | Write the JSONL event stream (§6.3) to stdout instead of the final message. |
| `-o, --output-last-message <file>` | yes | yes | Also write the final message to this file (§6.2). |
| `--remote <url>`, `--token <token>` | yes | yes | Connect to this AppServer instead of going through the Hub. |

With `resume`, `-m` and `--auto-approve` take effect through `thread/config/update` before the Turn starts. Exec reads the thread's configuration with `thread/read` and writes it back with only those values changed. For `-m`, it keeps the provider and clears `reasoning` and `speed`, so the new model starts from its own defaults.

### 4.2 Exit status

| Code | Meaning |
|------|---------|
| `0` | The Turn completed. |
| `1` | Any of: the Turn failed or was cancelled; the directory is not a DotCraft workspace; no thread matched; exec could not reach or use the AppServer. |
| `2` | Usage error, before any thread is created. |

`exec interrupt` exits `0` when it interrupted a running Turn or found none. It exits `1` when it cannot reach the AppServer or the thread does not exist.

## 5. Workspace And Thread Identity

### 5.1 Workspace

The workspace is `--cd` or the current directory, and it must contain `.craft`. Otherwise exec reports `DotCraft workspace not found: <dir>/.craft` and exits `1`. Exec never creates a workspace. `dotcraft setup` or Desktop does.

Threads are started without `cwd`, so they run at the workspace root. Generated images and other state live in the workspace's `.craft`.

With `--remote`, the workspace is the one the remote AppServer owns.

### 5.2 Identity

Every exec thread uses `identity = { channelName: "cli", userId: "local" }`. Exec omits `workspacePath`, so the connected AppServer supplies its own workspace, both through the Hub and with `--remote`. Desktop lists these threads with the CLI origin badge.

### 5.3 `--last`

`--last` selects the first thread from `thread/list` with scope `workspace`. Archived and subagent threads are excluded. Results are ordered by `lastActiveAt` descending, so this is the workspace's most recently active thread, whichever client created it. If the workspace has no threads, exec reports it on stderr and exits `1`.

### 5.4 Continuing a thread

`resume` submits `turn/start` directly on the persisted thread. If the thread already has a running or waiting Turn, exec reports the conflict and exits `1`. It does not queue or steer. Archived threads are rejected the same way.

## 6. Output

### 6.1 Human-readable mode (default)

**stdout** holds only the final message, followed by a newline. It is written only when the Turn completed. When the Turn fails or is cancelled, stdout stays empty.

The final message is the text of the last `agentMessage` item in the terminal Turn snapshot whose `deliveryMode` is not `async`.

**stderr** holds everything else, one line per event:

- the thread id, as soon as it is known
- tool and command starts and completions
- one line per image saved in this Turn, with its absolute path
- system events
- the failure reason

Exec never writes generated images to stdout, either as paths or as content, and it never prints base64 image data.

### 6.2 `-o, --output-last-message`

When the Turn completed, exec writes the final message to the file as UTF-8 without a byte-order mark, replacing any existing content.

- If there is no final message, exec writes an empty file and prints a warning on stderr.
- When the Turn fails or is cancelled, exec does not write the file.

### 6.3 `--json`

stdout carries one JSON object per line. Fields use `snake_case`.

| Event | Fields |
|-------|--------|
| `thread.started` | `thread_id` |
| `turn.started` | none |
| `item.started` | `item` |
| `item.updated` | `item` |
| `item.completed` | `item` |
| `turn.completed` | `usage: { input_tokens, cached_input_tokens, cache_write_input_tokens, output_tokens, reasoning_output_tokens }` |
| `turn.failed` | `error: { message }` |
| `error` | `message` |

Use `error` for failures outside a Turn. A cancelled Turn emits no `turn.*` event; items it left open, including the to-do list, are closed with `item.completed`.

Each `item` has an `id`, a `type`, and type-specific fields:

| `type` | Fields | DotCraft source |
|--------|--------|-----------------|
| `agent_message` | `text` | `agentMessage` |
| `reasoning` | `text` | `reasoningContent` |
| `command_execution` | `command`, `aggregated_output`, `exit_code`, `status` (`in_progress`, `completed`, `failed`) | `commandExecution` |
| `file_change` | `changes: [{ path, kind }]` (`add`, `delete`, `update`), `status` | a tool result whose structured content is a file change |
| `mcp_tool_call` | `server`, `tool`, `arguments`, `result`, `error`, `status` | `mcpToolCall` |
| `web_search` | `query` | `webSearch` |
| `todo_list` | `items: [{ text, completed }]` | `plan/updated`; `item.updated` follows each later snapshot in the Turn |
| `image_generation` | `status`, `saved_path`, `revised_prompt`, `error` | `imageGeneration` |

Other item types are omitted. `image_generation` never includes image bytes. stderr keeps the progress lines from §6.1.

### 6.4 Turns that carry images

A Turn's `item/completed` and `turn/completed` notifications carry generated images as base64. Exec must receive them without losing the connection, whatever the image size.

## 7. Approvals

Exec initializes with `approvalSupport: false` and `requestUserInputSupport: false`. The AppServer then settles the approval requests of the Turns exec starts according to the thread's approval policy.

**Approval policy:**

- `autoApprove` accepts every request.
- `default` follows the workspace's default approval policy. When that is `autoApprove`, every request is accepted.
- Every other policy declines. The tool receives the rejection, and the Turn continues.

New exec threads use `default`, so the outcome depends on the workspace:

- Work inside the workspace that needs no approval always runs. This includes ordinary shell commands and file writes.
- Operations that need approval, such as dangerous commands, access outside the workspace, and gated MCP tools, run when the workspace default is `autoApprove` and are refused otherwise.
- A declined request does not fail the run.

**With `--auto-approve`:**

- The thread's policy becomes `autoApprove`.
- Commands that the safety rules forbid stay forbidden.

User-input requests receive empty answers, as the protocol defines for clients that lack that capability.

## 8. Lifecycle And Interruption

- **Waiting.** Exec waits for the Turn's terminal notification, with no time limit of its own.
- **Ctrl+C / SIGINT.** Exec sends `turn/interrupt` for its running Turn, keeps waiting for the terminal notification, then exits `1`.
- **Killed without a chance to interrupt.** The Turn keeps running on the AppServer, because Turn execution outlives the submitting connection. `dotcraft exec interrupt` stops it. It resolves the workspace as in §5, then finds the thread's running Turn and sends `turn/interrupt`.
- **Hub lifetime.** A Hub or AppServer that exec started keeps running after exec exits, whether exec finished normally, was interrupted, or had its process tree terminated. The Hub is detached so that it is neither a descendant of exec nor reached by exec's console signals.

## 9. Constraints And Compatibility

- **Provider.** Exec requires a configured provider. It does not map model ids to providers. An unknown model fails when the provider is called, and exec reports that as a failed Turn.
- **Machine contracts.** Callers parse only stdout, `-o` files, and `--json` events. The stderr wording is for humans and may change.
