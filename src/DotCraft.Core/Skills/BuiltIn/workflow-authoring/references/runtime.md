# DotCraft Workflow Runtime

Use this reference for every Workflow script. DotCraft's installed runtime and feature specification override examples from other products.

## Script envelope

Make the first executable declaration a literal metadata export:

```js
export const meta = {
  name: "review-change",
  description: "Review a change from independent perspectives",
  whenToUse: "Use for a substantive code review",
  phases: ["inspect", "review", "synthesize"]
};
```

`name` and `description` are required non-empty strings. `whenToUse` and `phases` are optional; phases are strings, not objects. Keep all metadata literal: do not use imports, variables, computed values, or function calls. The remaining body supports top-level `await`. Return a JSON-serializable result explicitly.

## Available globals

- `agent(input, options?)` starts one fresh native SubAgent and resolves to text, a schema-validated value, or `null`.
- `parallel(thunks)` runs an array of deferred functions concurrently and preserves declaration order.
- `pipeline(items, ...stages)` processes items concurrently while running each item's stages in order. A stage receives `(previous, original, index)`.
- `phase(name, detail?)` records a progress boundary and makes `name` the current phase.
- `log(value)` records bounded diagnostic JSON.
- `args` is immutable structured input for the invocation.
- `budget` exposes `maxAgentCalls`, `maxConcurrency`, `tokenBudget`, `inputTokens`, and `outputTokens` as read-only values.
- `cwd` and restricted `process.cwd()` return the workspace root.

Pass functions rather than promises to `parallel()`:

```js
const results = await parallel(items.map((item, index) =>
  () => agent(`Inspect ${item.path}`, { label: `inspect-${index}` })
));
```

If a pipeline stage returns `null`, later stages for that item do not run and its final result stays `null`.

## Agent calls

Pass either a prompt string or an object containing `prompt` and JSON-serializable `context`. Supported options are:

- `label`: stable human-readable operation label.
- `phase`: explicit phase association; otherwise inherit the latest `phase()` call.
- `schema`: JSON Schema for a structured result.
- `model`: invocation-specific child model override.
- `effort`: invocation-specific reasoning override; `xhigh` and `max` normalize to `extraHigh`.
- `isolation`: `shared` or managed `worktree`.
- `agentType`: a native Agent role name supplied by the current context.

Use `model` or `agentType` only when the environment supplies an exact valid name. A Workflow child starts with fresh conversation context and does not inherit the parent's dialogue history.

When using `schema`, ask for only the fields the next stage needs. Read the returned value only after checking it is not `null`. A stopped child or unrecoverable child execution failure contributes `null`; preserve the intended work ID so missing coverage remains visible.

## Runtime boundaries

Write plain deterministic JavaScript. Do not use imports, `require`, filesystem or network modules, shell/process APIs, CLR access, `fetch`, timers, `Date`, `Math.random`, `eval`, or dynamic function constructors. Delegate repository reads, edits, commands, and web access to Agents.

Return only JSON-compatible objects, arrays, strings, numbers, booleans, and `null`. Do not return functions, promises, cycles, symbols, `BigInt`, non-finite numbers, or runtime handles.
