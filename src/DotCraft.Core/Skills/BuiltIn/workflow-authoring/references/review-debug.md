# Workflow Review and Debugging

## Review checklist

- Confirm the first executable declaration is literal `export const meta` with non-empty `name` and `description`, optional literal `whenToUse`, and a string-array `phases` value.
- Confirm every declared phase is used, every discovered phase name is intentional, and phase-dependent work runs after the matching `phase()` call or supplies `options.phase`.
- Confirm `parallel()` receives functions and dependent stages do not start before their inputs exist.
- Confirm every intended Agent operation has a stable unique label and a stable work ID before results are filtered or deduplicated.
- Confirm every `null` is handled as missing coverage and remains visible to downstream synthesis or the final result.
- Confirm prompts, context, schemas, logs, phase detail, and the final return value are JSON-serializable where required.
- Confirm fan-out, input size, retries expressed in script logic, and loops have task-appropriate finite bounds.
- Confirm repository, command, network, and tool work is delegated to Agents rather than attempted in the Workflow runtime.
- Confirm `model` and `agentType` use exact names supplied by current context; do not invent routes.
- Confirm the script uses no unsupported pi, Claude, Node.js, browser, or CLR APIs.

## Debugging map

| Symptom | Inspect | Repair |
|---|---|---|
| Metadata rejected before launch | First declaration and literal metadata values | Use one literal `export const meta`; make phases a string array |
| Script syntax or prohibited-syntax error | Imports, runtime globals, time/random APIs, dynamic code | Keep orchestration in plain deterministic JavaScript and delegate external work |
| `parallel()` rejects input | Array elements were promises or values | Wrap every call in a function |
| Agent result is `null` | Child stop, cancellation, or unrecoverable execution failure | Record missing coverage and continue only when the workflow can report it honestly |
| Structured value is unavailable | Prompt/schema mismatch or no valid structured submission | Narrow the schema and prompt, then guard the result before reading fields |
| Serialization failure | Host-bound payload or final return contains unsupported values | Convert to plain JSON data and remove cycles, functions, handles, and non-finite values |
| Budget or Agent-call limit failure | Unbounded fan-out/loop or exhausted configured gate | Reduce or batch work; do not silently invent a larger budget |
| Phase view is incomplete | Missing declaration, `phase()` call, or `options.phase` | Declare intended phases and associate each operation explicitly or by current-phase inheritance |

## Resume-sensitive edits

Resume replays the longest usable unchanged prefix of Agent calls. Preserve the order, prompt, label, schema, model, effort, isolation, role, and input of earlier successful calls that should replay. The first changed, new, failed, or unusable call and later calls execute live.

Do not reorder earlier calls merely for readability during a resume repair. Add new work after the reusable prefix unless changing earlier behavior is the purpose of the edit.
