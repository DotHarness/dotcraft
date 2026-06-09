# Interactive Tool UI — Sample App

A minimal DotCraft **App Binding** app that ships an MCP-Apps **Interactive Tool UI**.
It declares a tool `sample.ShowCard` whose `_meta.ui.resourceUri` points at a `ui://`
resource, handles the tool call, and serves the resource HTML on `item/resource/read`.
DotCraft Desktop renders that HTML in a sandboxed `dotcraft-app://` iframe and drives the
postMessage bridge (`ui/initialize` → `ui/notifications/tool-result`). See
`specs/protocols/tool-result-presentation.md`.

## What it exercises (M-ii)

- `_meta.ui` declaration on an attached dynamic tool → `dynamicToolCall` item carries the `ui` descriptor.
- `dotcraft-app://` scheme handler brokers `ui/resource/read` and serves the HTML with its own CSP.
- Sandboxed iframe + read-only host bridge (host context handshake + tool-result push).

## Run it

1. **Install the plugin into a workspace** so AppServer discovers the app. Copy the
   `plugin/` folder into your workspace:

   ```
   <workspace>/.craft/plugins/dotcraft-sample-ui/
   ├── .craft-plugin/plugin.json
   ├── apps.json
   └── dotcraft-sample-ui.svg
   ```

2. **Open the workspace in DotCraft Desktop** and leave it running. This keeps the
   workspace AppServer alive so the sample's connection stays up. (If you run the sample
   with no Desktop, it may spin up a transient AppServer that exits and drops the binding.)

3. **Run the sample in auto mode** (recommended). One command connects to that AppServer,
   establishes the connection, **creates a thread, binds the app, attaches
   `sample.ShowCard`, and serves the card** — no manual handoff URLs or Desktop clicks:

   ```sh
   dotnet run --project sdk/dotnet/samples/InteractiveToolSample -- "<workspacePath>"
   ```

   It prints a thread id and stays running. (Pass an existing thread id as a 2nd arg to
   bind into that thread instead of a fresh one.)

4. **Open that thread in DotCraft Desktop** (same workspace — it appears in the sidebar)
   and ask the agent to use the tool (e.g. *"use the ShowCard tool with note 'hello'"*).
   The `dynamicToolCall` result renders as an interactive card (the iframe), themed to
   match Desktop, showing the note. Keep the sample process running.

> **Handoff mode** (the real external-app pattern): instead of auto mode, connect/bind the
> app from Desktop and run the sample per the issued handoff URL —
> `dotnet run … -- --handoff "<handoff-url>"` (run once for the `connect` URL, then again
> for the `bind` URL, leaving it running). Grab the URL from Desktop's "Copy URL"
> affordance, or DevTools (Ctrl+Shift+I) → the `handoff.uri` in the
> `app/connection/start` / `app/binding/request/create` response.

> The `Open in Sample` button is wired for `ui/open-link`, which the read-only M-ii host
> ignores; it becomes active in M-iii (bridge actions).
