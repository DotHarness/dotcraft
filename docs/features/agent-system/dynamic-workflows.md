# Dynamic Workflows

A dynamic workflow turns one large task into a reusable orchestration script. The script runs several subagents in the background, keeps the intermediate work out of your main conversation, and sends the finished result back to it.

Reach for a workflow when the order, branching, and fan-out of a job should be the same every time. For a single delegation, one [subagent](./subagents) is simpler. When members need to coordinate with each other as they work, use [Teams](./teams).

![A reusable workflow script runs in the background, fans the task out to several subagents at once, and returns one finished result to the conversation](/dynamic-workflows-overview.svg)

## Run a workflow

Ask for a dynamic workflow explicitly in the conversation:

```text
Use a dynamic workflow to review this change for correctness, security, and test coverage, then
combine the findings into one ranked report.
```

DotCraft writes the orchestration script, asks for approval when the current permission policy requires it, and starts the run in the background. The conversation stays available while it runs, and the result comes back on its own.

## Save a reusable workflow

Store a script as a `.js` file under `.craft/workflows/` and it travels with the repository, available to everyone on the team. Store it under `~/.craft/workflows/` to keep it to yourself on this machine.

Every script is a block of metadata followed by a JavaScript body:

```js
export const meta = {
  name: "review-change",
  description: "Review a change from independent perspectives",
  whenToUse: "Use for a substantive code review",
  phases: ["review", "synthesize"]
};

const reviews = await parallel([
  () => agent("Review the change for correctness.", {
    label: "correctness",
    phase: "review"
  }),
  () => agent("Review the change for missing tests.", {
    label: "tests",
    phase: "review"
  })
]);

return agent({
  prompt: "Combine the reviews into one ranked report.",
  context: reviews
}, {
  label: "synthesis",
  phase: "synthesize"
});
```

`meta.name` and `meta.description` are required. `whenToUse` and `phases` are optional. Metadata must be literal data — imports, computed values, and function calls are not accepted. The body supports top-level `await` and must return a JSON-serializable value.

Once saved, the script becomes a slash command that takes extra direction:

```text
/review-change focus on the authentication changes under src/
```

The text after the command reaches the script as `args`. When a workspace script and a personal script share a name, the workspace one wins.

## Orchestration API

The script has a small set of orchestration calls:

| API | Purpose |
|---|---|
| `agent(input, options?)` | Start one native subagent with fresh context and return its result or `null`. |
| `parallel(thunks)` | Start deferred calls concurrently and return results in declaration order. |
| `pipeline(items, ...stages)` | Process items concurrently while running each item's stages in order. |
| `phase(name, detail?)` | Record a named progress boundary. |
| `log(value)` | Record diagnostic data for the run. |
| `args` | Read the structured input for this invocation. |
| `budget` | Read the current run limits and usage so far. |
| `cwd` | Read the workspace root. |

Each pipeline stage receives `(previous, original, index)`. If a stage returns `null`, the later stages for that item don't run. `parallel()` and `pipeline()` keep results in input order even when subagents finish in a different one.

### Configure a single call

`agent()` accepts these options:

| Option | Purpose |
|---|---|
| `label` | Give the call a stable name in run records. |
| `phase` | Associate the call with a phase. |
| `schema` | Require a result that matches a JSON Schema. |
| `model` | Override the model used for the call. |
| `effort` | Override the reasoning effort for the call. |
| `isolation` | Use `shared` or a managed `worktree`. |
| `agentType` | Select a native Agent role. |

A call with `schema` returns the JSON value once it validates. Without `schema`, it returns the subagent's final text. A cancelled call, or one that hits an unrecoverable error, returns `null` — handle that before using the result.

## Share through plugins

An enabled plugin can contribute workflows too, from a root `workflows/` directory or from the directory named by its manifest `workflows` field. Plugin commands are always namespaced:

```text
/review-tools:review-change
```

The namespace keeps a plugin workflow from displacing a script of your own with the same name.

## The script only orchestrates

The JavaScript body coordinates work. It can't read files, open network connections, or start processes itself — hand those actions to `agent()`, where subagent tool calls go through the usual workspace boundaries and tool approvals.

Every `agent()` call starts with fresh context while inheriting the parent conversation's workspace, permission policy, and model defaults. Adding `isolation: "worktree"` gives that call a managed Git worktree. DotCraft removes the worktree afterwards if it's clean and keeps it for inspection if it holds changes or commits, without merging anything automatically.

## Related docs

- [Subagents](./subagents) — for a single delegation, one subagent is enough
- [Teams](./teams) — the alternative when members coordinate with each other while working
- [Plugins & Tools](./plugins-tools) — package a workflow into a plugin and share it further
