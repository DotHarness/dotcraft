# DotCraft Generative UI Specification

| Field | Value |
|-------|-------|
| **Version** | 0.8.4 |
| **Status** | Living |
| **Date** | 2026-10-12 |
| **Related Specs** | [Design System](../architecture/DESIGN.md), [Desktop Client](../clients/desktop-client.md), [Desktop Styles](../architecture/desktop-styles.md), [AppServer Protocol](../protocols/appserver-protocol.md) |

Purpose: define Generative UI, the small interactive answers a model writes as a program and Desktop renders with its own components inside the conversation.

## 1. Scope

A Generative UI program is a short JSX program. Desktop runs it in an isolated sandbox and renders the component tree it produces with native Desktop components. The program never touches Desktop's DOM; it only describes a tree and reacts to events.

This specification covers:

- The authoring contract: what a program may use and how it starts.
- The runtime boundary: where the program runs and what it cannot reach.
- The host protocol between Desktop and the sandbox.
- The component set and how Desktop validates and renders it.
- Limits and failure reporting.
- How blocks appear in assistant messages, how their actions reach the host, and how their state is saved.
- How the model learns that Generative UI is available.

Generative UI works with any model provider. Desktop compiles the program itself; no server-side compiler or provider feature is involved.

## 2. Goals and Non-goals

### 2.1 Goals

- A model can answer with a working calculator, form, quiz, or chart instead of describing one.
- The answer looks and behaves like Desktop: same controls, tokens, and quiet visual language.
- A broken or hostile program cannot read Desktop state, reach the network, or freeze the app.
- A failure says where it happened, so the author or the user can fix it.

### 2.2 Non-goals

- Arbitrary HTML, CSS, or DOM access. Visualizations cover free-form HTML.
- Network access, storage, or imports from inside a program.
- Third-party components or custom styling.

## 3. Authoring contract

A program is the content of a fenced block whose info string is `dotcraft-ui`.

- The program is JSX. Built-in components are capitalized globals (`Card`, `Row`, `Text`, ...) and need no import. A program may define its own function components.
- The `DotCraft` global provides the hooks and host actions:

| Member | Meaning |
|--------|---------|
| `useState`, `useEffect`, `useMemo`, `useRef`, `useId` | Component state and lifecycle, with React semantics. |
| `useTheme()` | `'light'` or `'dark'`. |
| `useBreakpoint()` | `'compact'` below 480px of block width, `'regular'` below 900px, otherwise `'wide'`. |
| `render(element)` | Mounts the root element. A program that never calls it fails with `worker_timeout`. |
| `sendMessage(prompt)` | Asks Desktop to send `prompt` as the user's next message. |
| `prefillComposer(text)` | Asks Desktop to put `text` in the composer without sending. |
| `copy(text)` | Asks Desktop to copy `text` to the clipboard. |
| `openUrl(url)` | Asks Desktop to open an `http` or `https` link. |

- Props are JSON values or functions. A function prop is an event handler; its arguments are JSON values. Elements are valid only as children.
- `import`, `export`, `fetch`, `XMLHttpRequest`, `WebSocket`, storage, and nested workers are unavailable. Timers are available.
- Images are embedded only: an `Image` source must be a base64 `data:image/png`, `jpeg`, `gif`, or `webp` URL. Any other scheme fails validation.
- Controls may be controlled (`value` plus `onChange`) or uncontrolled (`defaultValue`). Unlike React, a controlled field shows the reader's edits even when the program never changes `value`: the field keeps its own draft while its edits are in flight, adopts a `value` the program sets on its own, and ignores snapshots that echo an older edit, so a late snapshot never rewinds typing.
- Programs should be deterministic: the same program renders the same first tree.
- `useState` values are saved and restored by the host (§11); programs do not read or write saved state themselves.

```dotcraft-ui
function Tip() {
  const [bill, setBill] = DotCraft.useState(80)
  const [rate, setRate] = DotCraft.useState(18)
  return (
    <Card title="Tip">
      <Slider label="Bill" min={0} max={300} value={bill} onChange={setBill} valueText={`$${bill}`} />
      <Slider label="Tip" min={0} max={30} value={rate} onChange={setRate} valueText={`${rate}%`} />
      <Text size="xl" weight="semibold" tabular>{`$${(bill * rate / 100).toFixed(2)}`}</Text>
    </Card>
  )
}
DotCraft.render(<Tip />)
```

## 4. Runtime boundary

```
Desktop renderer ──iframe──► sandbox proxy (dotcraft-mcp-sandbox:) ──srcdoc──► runner frame ──► Worker per block (program)
       ▲                                                                            │
       └───────────────────── one MessagePort per block (host protocol) ◄───────────┘
```

- **Runner frame.** Desktop keeps one hidden runner frame per renderer and shares it between blocks. It creates the frame when the first block needs it, or earlier when a message with blocks mounts, so a block usually starts without waiting for the sandbox. Desktop reuses the MCP App sandbox proxy. The proxy loads a runner document into a nested `sandbox="allow-scripts"` frame with an opaque origin. Its CSP allows only inline scripts, `eval` for the compiled program, and `blob:` workers; `connect-src`, `img-src`, and `style-src` are `'none'`.
- **Worker.** The runner frame starts one dedicated Worker per block from a `blob:` URL, so blocks never share a global scope or an event loop. The worker removes network, storage, and worker constructors from its global scope before it accepts a program. The CSP it inherits is the backstop.
- **Compiler.** The worker compiles JSX with Sucrase into calls to an internal element factory. Compilation happens inside the sandbox, so a malformed program never runs in Desktop.
- **Hooks runtime.** The worker renders function components, keeps hook state per component instance, batches state updates, runs effects after each committed render, and posts a snapshot only when the tree changed. An instance is identified by its position in the tree and its function's name, so a component redefined on each render keeps its state.
- **Renderer.** Desktop validates every snapshot and renders it with its own components. Event handlers in the tree are callback references; Desktop invokes them back over the port.
- **Lifecycle.** Removing a block, retrying, or failing closes the block's port and terminates its Worker; other blocks keep running. Desktop removes the frame once no block has used it for 60 seconds. When the sandbox closes the frame, every running block fails with `host_error`, and the next block creates a new frame.

## 5. Host protocol

The protocol version is `1`. The runner frame announces itself to the host once with a window message `generativeUi/runnerReady { protocolVersion }`. For each block the host sends `generativeUi/connect { protocolVersion }` and transfers a new `MessagePort`; the sandbox proxy forwards the port, and the frame starts a Worker for it. Every later message travels over the port and carries `protocolVersion`.

### 5.1 Host to runner

| Type | Fields | Meaning |
|------|--------|---------|
| `start` | `code`, `theme`, `breakpoint`, `initialState?` | Compile and run the program. `initialState` is a saved-state snapshot (§11). Accepted once. |
| `callback` | `id`, `args` | Invoke the event handler that `id` names in the latest tree. Unknown ids are ignored. |
| `globals` | `theme`, `breakpoint` | The theme or block width changed; the program re-renders. |
| `restore` | `state` | Replace hook state with a saved-state snapshot from another client and re-render (§11). |
| `dispose` | | Run effect cleanups and stop. The runner frame then terminates the block's Worker. |

### 5.2 Runner to host

| Type | Fields | Meaning |
|------|--------|---------|
| `snapshot` | `version`, `tree` | A committed tree. `version` increases strictly. |
| `action` | `kind`, `payload` | A host action request: `sendMessage`, `prefillComposer`, or `copy` with `payload.text`; `openUrl` with `payload.url`. |
| `state` | `state` | The saved-state snapshot after a state change that followed a delivered callback (§11). |
| `error` | `stage`, `message`, `line?`, `column?` | The program stopped. Positions are 1-based and refer to the program source. |

### 5.3 Snapshot tree

A tree is a list of nodes. A node is either a text string or `{ type, key?, props, children }`, where `type` is a component name, `props` holds only JSON values, and `children` is a list of nodes. A function prop becomes `{ "$callback": id }`; the id is derived from the node's position and prop name, so it stays stable while the tree keeps its shape.

### 5.4 Host actions

Desktop accepts an action only within one second after it delivered a callback, and only one action per callback, so a program cannot send messages or open links on its own. Desktop decides how each accepted action is carried out (§10).

## 6. Components

Desktop validates each node's props against the component's schema before rendering it. An unknown component or invalid props render a one-line inline error in that node's place; the rest of the tree still renders. Unknown props are ignored.

| Group | Components |
|-------|------------|
| Layout | `Card` (title, subtitle, padding), `Row`, `Col`, `Box` (gap, alignment, surface), `Spacer`, `Divider` |
| Text | `Title` (level 1–3), `Text` (tone, size, weight, mono, tabular), `Caption`, `Markdown` (the conversation Markdown renderer), `Badge` (status tone) |
| Media | `Icon` (a Lucide icon name, loaded on demand), `Image` (base64 `data:image/*` only) |
| Controls | `Button`, `Input`, `Textarea`, `Select`, `Checkbox`, `RadioGroup`, `SegmentedControl`, `Slider`, `Form` |
| Data | `Table` (columns and rows of scalar cells, with a number format and currency per column), `List` (title, description, icon, trailing text), `Chart` (line, bar, or area), `PieChart` |

- `Form` collects the current value of every field with a `name` and calls `onSubmit` with them; a `Button` with `submit` submits its form. Validation is the program's job; fields show the `error` and `hint` they are given.
- Visual treatment of every component and state follows [DESIGN.md: Generative UI](../architecture/DESIGN.md#generative-ui).

## 7. Presentation states

| State | Presentation |
|-------|--------------|
| Streaming | The block's closing fence has not arrived; the loading skeleton shows with a "Working" label and the runner does not start. |
| Waiting | A closed block that has not started yet, outside the preload range or while its saved state loads; the same skeleton. |
| Loading | From runner creation to the first snapshot, the same skeleton. |
| Ready | The latest snapshot, rendered natively in the reading column. |
| Locked | Ready, but the block's message is still streaming, its turn is still running, or the transcript is read-only; every control is disabled and nothing is dimmed. |
| Failed before rendering | The failure notice alone, with the stage, position, and message, and a Retry that starts a fresh runner. |
| Failed after rendering | The last good snapshot stays, inert: callbacks are no longer delivered and every control is disabled. The failure notice and Retry sit directly below it. A `host_error` drops the frame, since the snapshot itself could not render. |

## 8. Limits and failures

| Limit | Value | Stage when exceeded |
|-------|-------|---------------------|
| Program source | 64 KB | `payload_too_large` |
| Saved state | 16 KB | `payload_too_large` when restoring; a larger snapshot is not saved |
| Snapshot | 256 KB of JSON | `payload_too_large` |
| Components per tree | 1,500 | `payload_too_large` |
| Tree depth | 32 | `payload_too_large` |
| `sendMessage` prompt | 16 KB | invalid call (`callback_error`) |
| `prefillComposer` and `copy` text | 64 KB | invalid call (`callback_error`) |
| `openUrl` URL | 2,048 characters | invalid call (`callback_error`) |
| Sandbox start | 10 s from a block's request to the shared frame's `runnerReady` | `frame_timeout` |
| First render | 5 s after `start` | `worker_timeout` |
| Unresponsive worker | 3 s without answering one of the runner frame's pings; other worker messages are not answers | `worker_timeout` |
| Synchronous re-renders | 50 without yielding | `runtime_error` |
| Callbacks | 30 per second; the excess is coalesced per handler and delivered later | none |

| Stage | Meaning |
|-------|---------|
| `frame_timeout` | The sandbox never announced itself. |
| `worker_start_error` | The worker could not be created or failed while loading. |
| `worker_timeout` | No first render in time, or the worker stopped responding. |
| `protocol_mismatch` | The host and runner speak different protocol versions. |
| `compile_error` | The program is not valid JSX; carries line and column. |
| `runtime_error` | The program threw while running, rendering, or in an effect or timer; carries the line when known. |
| `callback_error` | An event handler threw. |
| `host_error` | Desktop failed to render a valid snapshot, or the sandbox closed the runner. |
| `invalid_payload` | A message or tree broke the protocol shape. |
| `payload_too_large` | A limit in the first table was exceeded. |

Every failure stops the runner; §7 defines what stays on screen. Messages are English diagnostic text for the author; Desktop localizes the notice and stage names.

## 9. Message integration

An assistant message may hold any number of Generative UI blocks between its Markdown and visualization references. Only a top-level fence counts: a `dotcraft-ui` fence nested in another fence or indented as code is ordinary Markdown. Blocks are numbered from 0 in source order; the number identifies a block's saved state.

- While a message streams, a block whose closing fence has not arrived shows the skeleton and never its source. Once the fence closes the block starts, even while the rest of the message streams. A completed message whose last block never closed runs it to the end of the message.
- Blocks in completed turns start only when they come within 320px of the visible conversation, so long threads do not run every block at once. A started block keeps running when scrolled away.
- A block is locked while its message streams or its turn runs.
- Copying a message omits block source and keeps the surrounding prose. A client that cannot render blocks shows each one as a tool row without its source.

[Desktop Client §5.8.4](../clients/desktop-client.md#584-generative-ui-in-assistant-messages) defines the Desktop behavior in detail.

## 10. Host actions

| Action | Desktop behavior |
|--------|------------------|
| `sendMessage(prompt)` | Starts a user turn in the block's thread with `prompt` as the message text, without confirmation. While a turn is running or waiting for approval or input, it shows a quiet notice and sends nothing. |
| `prefillComposer(text)` | Puts `text` in the composer. |
| `copy(text)` | Copies `text` and shows the Copied notice. |
| `openUrl(url)` | Shows the URL in a trusted confirmation dialog and opens it in the system browser only after the user confirms. |

The user-activation window of §5.4 applies to every action. A locked block delivers no callbacks, so it requests no actions.

## 11. Saved state

The runtime saves every `useState` value that is JSON: strings, finite numbers, booleans, `null`, arrays, and plain objects. A snapshot maps each component instance key (§4) to its state values by hook position:

```json
{ "r#Quiz": { "0": 2, "1": { "q1": "b" } }, "r#Quiz.1#Step": { "0": true } }
```

- The runtime posts a `state` message only after a render that follows a delivered callback within the action window and changes the snapshot. Rendering, restoring, and timers alone never save.
- When `start` carries `initialState`, an instance created with a key in the snapshot starts each listed `useState` slot with the saved value instead of its initial value. A key is consumed once, so an instance that unmounts and mounts again starts fresh.
- Desktop persists the snapshot per assistant item and block index through `generativeUi/state/update` and reads a thread's snapshots once with `generativeUi/state/read` ([AppServer Protocol §22.10B](../protocols/appserver-protocol.md#2210b-generative-ui-state)). Both peers must advertise the `generativeUi` capability; without it blocks run without saved state.
- When another client saves a block's state, Desktop receives `generativeUi/state/updated` and sends the snapshot to the block's running program as `restore`. Each listed `useState` slot of a mounted instance takes the saved value, a key whose instance is not mounted waits for it as with `initialState`, and the program re-renders without restarting. Slots the snapshot does not list keep their values, effects run only as their dependencies change, and a restore never saves. The last writer wins: an arriving state drops the block's pending local save, so an interaction in progress continues from the restored values. A `null` state changes no running program.
- Saved state is client view data. It is never sent to the model.

## 12. Model instructions

When the client bound to a thread advertises `generativeUi`, the server adds a thread context item stating that Generative UI is available and that the model should load the built-in `generative-ui` skill before writing a block. The item disappears once that client disconnects. The skill is the authoring contract for models: when to use Generative UI rather than a visualization or Markdown, the program contract, the component reference, layout and accessibility rules, state rules, limits, and examples.
