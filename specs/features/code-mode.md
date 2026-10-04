# DotCraft Code Mode Specification

| Field | Value |
|-------|-------|
| **Version** | 0.7.10 |
| **Status** | Draft |
| **Date** | 2026-10-04 |
| **Parent Specs** | [Tool Architecture](../architecture/tools-architecture.md), [Session Core](../architecture/session-core.md), [Prompt Cache](../architecture/prompt-cache.md), [Dynamic Workflows](dynamic-workflows.md) |
| **Related Specs** | [AppServer Protocol](../protocols/appserver-protocol.md), [Desktop Client](../clients/desktop-client.md), [Shell Command Safety](../architecture/shell-command-safety.md) |

Purpose: define the `CodeMode` tool, which lets the model write one JavaScript program that calls the
thread's other tools as functions, filters their results, and returns only what the model needs. It
also defines the shared script host that runs both code mode programs and Dynamic Workflow scripts.

Code mode reduces model round trips and keeps intermediate tool results out of model context. It adds
no capability: every effect of a program happens through a nested tool call that passes the same
authority, approval, hook, and safety checks as a direct call.

## 1. Scope and Ownership

This specification owns:

- the `Tools.CodeMode` configuration and its Desktop setting;
- the model-facing `CodeMode` tool, its description, and the nested tool surface;
- the program runtime contract, nested invocation, output, and per-thread store;
- the shared script host and its worker process, which Dynamic Workflows also uses;
- Session projection and Desktop presentation of code mode activity.

[Tool Architecture](../architecture/tools-architecture.md) owns registration, exposure, dispatch, and
item projection. [Dynamic Workflows](dynamic-workflows.md) keeps its script API, journal, replay, and
child-agent contracts and runs its scripts on the shared host.

Out of scope for this version: yielding a running program back to the model and resuming it with a
`wait` tool, calling `tools.*` from the Desktop `NodeReplJs` runtime, per-model default modes, and
engines other than Jint.

## 2. Definitions

| Term | Meaning |
|------|---------|
| Program | The JavaScript source the model passes to one `CodeMode` call. |
| Cell | One execution of one program, from start to its terminal outcome. |
| Nested call | A tool call a cell makes through `tools.<name>(...)`. |
| Nested surface | The set of registrations a cell can call. |
| Script host | AppServer-side service that owns script worker processes, frames, limits, and host calls. |
| Script worker | A hidden child process of the DotCraft executable that evaluates scripts with Jint. |

## 3. Configuration and Enablement

```json
{
  "Tools": {
    "CodeMode": {
      "Mode": "only"
    }
  }
}
```

`Mode` is `off`, `on`, or `only`; the default is `only`.

| Mode | Model tool list |
|------|-----------------|
| `off` | No `CodeMode`. |
| `on` | `CodeMode` is added. Every other tool stays directly visible. |
| `only` | `CodeMode` is added. Registrations on the nested surface are removed from the direct and deferred lists but stay in the snapshot, so nested calls still dispatch; tools that are not on the nested surface stay direct. |

The mode is a workspace setting read when a thread's tool snapshot is built. Changing it marks tool
snapshots dirty; a running Turn keeps its snapshot. AppServer exposes it as the `toolsCodeModeMode`
field of `workspace/config/update`, and the change notification uses the `codeMode` region.

Desktop presents the setting as one row in the Tools group of Settings › General, with three choices
labelled by outcome rather than by mechanism: off, scripts alongside tools, and scripts only. The setting
describes what the agent can do, not the engine behind it. Desktop reads the current value from the
workspace config and refreshes it on the `codeMode` change region.

In `only` mode the deferred-search registration disappears once every deferred registration is on the
nested surface, because those tools are reached through `ALL_TOOLS` and could not be called directly.

Code mode requires the script worker (§9), which starts lazily. An `CodeMode` call whose worker cannot start
fails with `code_mode_unavailable`. A later snapshot build retries the start before applying the mode;
when it still fails, `on` falls back to direct tools for that snapshot and records one warning for the
thread, and `only` fails the Turn with a stable `code_mode_unavailable` error rather than silently
removing tools.

## 4. Model-Facing Surface

### 4.1 The `CodeMode` tool

`CodeMode` is registered by the code mode module with canonical identity `ToolName(null, "CodeMode")`, the
standard `ToolCall`/`ToolResult` projection, and exposure `DirectModelOnly`, so a program can never call
`CodeMode` itself. It is runtime-managed: Agent Profile tool lists do not remove it. Threads that use only
their profile's tools do not get it.

Projection depends on the provider protocol and on the provider's `SupportsFreeformTools` capability:

- **OpenAI Responses, when the provider supports freeform tools**: a freeform (`custom`) tool
  constrained by this grammar, so the model writes raw source without JSON escaping:

  ```lark
  start: pragma_source | plain_source
  pragma_source: PRAGMA_LINE NEWLINE SOURCE
  plain_source: SOURCE

  PRAGMA_LINE: /[ \t]*\/\/ @exec:[^\r\n]*/
  NEWLINE: /\r?\n/
  SOURCE: /[\s\S]+/
  ```

  The grammar text is sent byte for byte, including a leading and a trailing newline, and no schema
  sanitization applies to it.
- **Every other provider**, including a Responses provider without freeform tool support: a function
  tool with one required string parameter `code`.

Either way the call reaches the dispatcher as the arguments `{ "code": <source> }`. The freeform input
mechanism is generic; [Tool Architecture](../architecture/tools-architecture.md#61-freeform-input)
defines its projection and replay.

The optional first line `// @exec: {"timeout_ms": 60000, "max_output_tokens": 10000}` sets per-call
limits. Only those two keys are accepted; `timeout_ms` is at most 3,600,000 and `max_output_tokens` at
most 100,000, counted at 4 characters per token. Unknown keys, invalid JSON, or values outside these
limits fail the call before execution. The pragma line is replaced by an empty line so stack trace
line numbers match the submitted source. The `timeout_ms` clock starts once the worker has accepted the program. The
worker runs one empty program through its engine before it reports ready, so neither process start
nor first-use engine warm-up counts against a program's timeout.

### 4.2 Description

The `CodeMode` description states, in this order:

1. what `CodeMode` is for: batching independent calls, chaining dependent calls, and reducing large
   results before they reach the model;
2. the runtime: raw JavaScript source rather than JSON, a quoted string, or a Markdown code fence, run
   as an async program body with top-level `await` and `return`, and no Node.js, file system, network,
   timers, modules, or `console`;
3. the calling convention: `await tools.<name>(args)` resolves to the result shape in that tool's
   declaration and rejects with an `Error` on failure, and calls still pending when the program ends
   are cancelled;
4. the globals in §5.1, and each pragma field with its default;
5. that unlisted tools exist and are found through `ALL_TOOLS`, when there are any;
6. the shared MCP preamble, when any nested tool is an MCP tool;
7. in `only` mode, the declarations of §4.3 for every non-deferred nested registration, grouped by
   namespace with the namespace description; in `on` mode, a pointer to the line attached to each
   direct tool.

The description is built from the snapshot and is identical for every request that uses the same
snapshot. It never contains volatile values such as call ids or timestamps.

### 4.3 Nested surface and declarations

The nested surface is computed from the Turn's `EffectiveToolSnapshot`: every `Direct` or `Deferred`
registration except `DirectModelOnly`, `Hidden`, `CodeMode`, and the deferred-search registration.

Each nested registration has a JavaScript name: `namespace__name` when the canonical name has a
namespace (MCP tools become `mcp__<server>__<tool>`), otherwise the bare name, with every character
outside `[A-Za-z0-9_$]` replaced by `_`. When two registrations map to the same name, the first in
snapshot order, the ordinal order of (namespace, name), keeps it and the other is omitted with a
warning. `tools[<provider flat name>]` also resolves.

A declaration is the tool's description followed by a signature of the form
`declare const tools: { name(args: T): Promise<R>; };`. `T` is generated from the input JSON Schema:
object properties in ordinal order with `?` for optional members and their descriptions as `//`
comments, `additionalProperties` as an index signature, enums and `const` as literal unions,
`anyOf`/`oneOf` as unions, `allOf` as intersections, arrays and tuples, and local `$ref` expanded with a
recursion guard that renders a repeated reference as `unknown`. An input or output type larger than
16,000 characters renders as `unknown`, and an MCP result type of that size as `CallToolResult`. `R`
follows §6: `CallToolResult<T>` for MCP tools, with `T` from the output schema; the output type for
other tools with an output schema;
`{ sessionId: string; status: "running" | "completed" | "failed"; output: string; exitCode: number | null; truncated: boolean; outputPath?: string }` for command
execution; otherwise `string`. One shared preamble defines `ContentBlock`, `TextContent`,
`ImageContent`, and `CallToolResult` once when any nested tool is an MCP tool.

Deferred registrations are callable but not listed. `ALL_TOOLS` lists `{ name, description }` for the
entire nested surface, where `description` is the tool's declaration, so a program finds a deferred or
MCP tool by filtering it.

In `on` mode, each direct tool that is on the nested surface gains one line at the end of its
description naming its call form and result shape, for example
``Code mode: `tools.ReadFile(args)` resolves to a string.``

Declarations and these lines are derived only from the snapshot, like the `CodeMode` description.

## 5. Program Runtime Contract

### 5.1 Globals

| Global | Behavior |
|--------|----------|
| `tools` | Frozen object of nested-call functions. Reading an unknown member throws a `TypeError` that names close matches and points to `ALL_TOOLS`. |
| `ALL_TOOLS` | Frozen array of `{ name, description }` for the nested surface. |
| `text(value)` | Appends a text output item; non-strings are JSON-stringified. |
| `image(value)` | Appends an image output item from a base64 `data:` URL or an MCP image content block. Remote URLs are rejected. |
| `exit()` | Ends the cell successfully, keeping output and store writes. |
| `store(key, value)` / `load(key)` | Per-thread JSON store (§8). |

`console` is not defined. The program's `return` value is ignored; output comes only from `text` and
`image`.

### 5.2 Boundaries

A program has no ambient capability. The engine runs without CLR interop, modules, `require`,
`import`, `eval`, `Function`, timers, `fetch`, or `WebAssembly`. `Date` and `Math.random` remain
available because cells, unlike workflows, are never replayed.

Arguments and results cross the boundary as JSON. A nested call whose argument is not JSON-serializable
rejects without being dispatched. Freeform tools take a string argument; function tools take an object.

### 5.3 Concurrency and completion

Each `tools.<name>(...)` call returns a Promise immediately and is dispatched as soon as the host
receives it, so `Promise.all` and `Promise.allSettled` run calls concurrently. A registration that the
snapshot marks as sequential is still serialized by the dispatcher.

A cell ends when its top-level program settles, when `exit()` runs, on timeout, on Turn cancellation, or
on a limit violation. Nested calls still pending at that point are cancelled and recorded as cancelled.

## 6. Nested Invocation

Every nested call goes through `IToolDispatcher.DispatchAsync` with the Turn's snapshot as it was before
code mode finalization (so tools hidden from the model in `only` mode stay dispatchable), a call id of
the form `exec-<uuid>`, audience `Model`, and origin `ToolInvocationOrigin("codeMode", <CodeMode call id>)`.
Authority, argument validation, mode guards, `PreToolUse` and post hooks, approval, file access checks,
the shell command safety kernel, and remote tool routing all apply unchanged. A nested call routed to a
remote workspace carries the `codeMode` origin kind to the Host, so command execution there returns
the same result object as it does locally.

An approval requested by a nested call pauses only that call; the cell's timeout is suspended while an
approval is pending, as it is for other approval-gated tools. A rejected approval rejects the nested
Promise with the rejection message.

The value a nested Promise resolves to is:

| Tool | Value |
|------|-------|
| Tool with an output schema | Its structured result. |
| MCP tool | The complete `CallToolResult`, including `isError`. |
| Command execution (`Exec`, `WriteStdin`) | `{ sessionId, status, output, exitCode, truncated, outputPath? }`; `sessionId` lets a program pass a still-running command to `WriteStdin`. The shell tools produce this object only for calls with the `codeMode` origin; direct calls and their items are unchanged. |
| Any other tool | Its text result. |

A failed dispatch rejects the Promise with an `Error` whose message is the stable error code and
message. A nested result entering the engine is at most 1 MiB; an oversized result is truncated with a
marker and the full value stays in the item.

## 7. Output

The `CodeMode` result is a header followed by the output items:

```text
Script completed
Wall time 1.2 seconds
Output:
...
```

The first line is `Script completed`, `Script failed`, or `Script timed out`. A failed cell appends
`Script error:` with the stack trace, then lists the nested calls that ran before the failure with
their status under `Tool calls made before the failure:`, because their effects are not undone.

A completed, failed, or timed-out cell is a successful `CodeMode` call, as a nonzero exit code is for a
command. Only an invalid pragma or an unavailable worker fails the call itself.

Text output is limited to `max_output_tokens`, default 10,000, using the common truncation policy:
the beginning and end are kept around an elision marker, and the full text is written to the thread's
output directory with its path included. Images are kept.

## 8. Store

`store` and `load` read and write a per-thread map of JSON values. Each cell sees a snapshot taken at
start; its writes are merged by key when the cell completes successfully, and discarded when it fails.
`load` returns a copy. Storing `undefined` deletes a key. A value is at most 256 KiB of JSON and the
map at most 1 MiB; larger writes throw inside the program. The merge re-checks the 1 MiB cap against
the current map, because concurrent cells start from separate snapshots; a merge that would exceed it
saves none of the cell's writes, and the `CodeMode` result says so. The map lives in AppServer runtime state
for the thread and is not persisted across AppServer restarts.

## 9. Script Host and Worker

### 9.1 Shared host

`DotCraft.Scripting` owns the script host used by both code mode and Dynamic Workflows:

- launching the DotCraft executable as `dotcraft script-worker <kind>`, where `kind` is `workflow` or
  `code-mode`, and supervising it;
- JSONL framing on redirected standard input and output, each frame carrying a version, sequence
  number, scope, type, and payload, with stderr bounded for diagnostics;
- the Jint engine factory and its constraints (§9.3);
- host calls: a worker request with an id, answered by exactly one host result or cancellation;
- RSS monitoring, wall-clock deadlines, cancellation, and process-tree termination.

Each feature supplies its own globals and host-call handlers. Dynamic Workflows keeps a worker per
execution attempt, its journal, and its deterministic replay; its `agent.request` and `agent.result`
messages are host calls on the shared protocol. Code mode uses one long-lived worker per AppServer
process.

### 9.2 Code mode worker

The worker starts on the first `CodeMode` call and serves cells from every thread in that AppServer. Each
cell runs in its own Jint `Engine` on its own thread, so cells share no JavaScript state.

| Direction | Messages |
|-----------|----------|
| Host to worker | `cell.start` (cell id, source, `{ name, description }` entries for the nested surface, provider-flat-name aliases, store snapshot, limits), `call.result`, `cell.cancel` |
| Worker to host | `ready`, `call.request` (cell id, request id, JavaScript name, arguments), `cell.output`, `cell.done` (outcome, error, store writes) |

The host is authoritative for cell state. If the worker exits, exceeds its memory limit, or violates
the protocol, the host terminates it, fails every running cell with a `Script failed` result that
names the cause, and starts a new worker on the next `CodeMode`. It never retries a failed cell.

### 9.3 Engine constraints

Every engine is created with strict mode, string compilation disabled, task interop, a statement
limit, a recursion limit, a `LimitMemory` ceiling, a regex timeout no longer than the cell deadline,
and a Promise timeout. Because Jint's allocation limit restarts across asynchronous continuations, the
worker's resident memory is also capped by the host; that cap is the guarantee, and the engine limit
only stops a single runaway segment early.

Default cell limits are a 300-second deadline, 256 MiB per cell, 50,000,000 statements, recursion
depth 256, 2,048 nested calls, 1 MiB per nested result entering the engine, and 8 MiB of total output.
At most 64 nested calls run at once; further calls wait for a slot, and only exceeding the 2,048 total
is a limit violation. The worker as a whole is capped at 1 GiB resident memory.

## 10. Session Projection and Presentation

`CodeMode` is recorded as a standard `ToolCall` and `ToolResult`, so model history replays it exactly.
Each nested call is recorded through its own registration's projection (`ToolCall`/`ToolResult`,
`McpToolCall`, `CommandExecution`, and so on) with its `exec-` call id. Every item of a nested call
persists the invocation origin kind `codeMode`; the wire item gains no field.

When model history is rebuilt from items, nested call items are skipped, because the model saw only the
`CodeMode` result. Compaction works on model history, so nested calls never reach it as model-visible tool
calls.

Desktop does not render the `CodeMode` call or its result. Nested calls appear as ordinary items in time
order, including approval cards and command output. Channels present no tool items and are unaffected.

## 11. Cancellation and Limits

Interrupting the Turn cancels its running cells; each cell cancels its pending nested calls through the
dispatcher's cancellation path before the Turn completes. Thread deletion cancels the thread's cells
and clears its store. AppServer shutdown terminates the worker.

A cell that exceeds its deadline is terminated and returns `Script timed out` with the output produced
so far. Limit violations return `Script failed` with the limit that was hit.
