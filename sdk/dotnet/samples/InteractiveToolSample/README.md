# Interactive Tool UI — Sample App

A minimal DotCraft **App Binding** app that ships an MCP-Apps **Interactive Tool UI**.
It declares a tool `sample.ShowCard` whose `_meta.ui.resourceUri` points at a `ui://`
resource, handles the tool call, and serves the resource HTML on `item/resource/read`.
DotCraft Desktop renders that HTML in a sandboxed `dotcraft-app://` iframe and drives the
postMessage bridge (`ui/initialize` → `ui/notifications/tool-result`). See
`specs/protocols/tool-result-presentation.md`.

## App-author rules (M-v)

- **Always return a text result.** Every UI-bearing tool MUST return model- and human-usable text via
  `contentItems` (and/or `structuredResult`) — the interactive UI is an enhancement, never required for
  correctness. Non-Desktop clients (TUI, chat channels) render that text; UI-only fields (`_meta`,
  `widgetState`, `ui`) are filtered out and never shown to the model or non-Desktop clients.
- **Serve a folder of `ui://` resources with one call.** For an app with several tool UIs, use the SDK
  helper instead of per-URI `RegisterResourceHandler` + inline HTML:

  ```csharp
  // Each file under ./ui is served as ui://oratorio/<relative-path> (board.html, item.html, …),
  // read on demand with the right MIME. Point each tool's _meta.ui.resourceUri at the matching URI.
  client.ServeStaticUiResources("ui://oratorio", Path.Combine(appDir, "ui"));
  ```

  This sample keeps a single inline HTML card for self-containedness; real/multi-tool apps should prefer
  `ServeStaticUiResources`.

## What it exercises (M-ii + M-iii + M-iv)

- `_meta.ui` declaration on an attached dynamic tool → `dynamicToolCall` item carries the `ui` descriptor.
- `dotcraft-app://` scheme handler brokers `ui/resource/read` and serves the HTML with its own CSP.
- Sandboxed iframe + host bridge (host context handshake + tool-result push).
- **M-iii bridge actions** (live): the `Open in Sample` button issues `ui/open-link` (gated to `https:`/`mailto:` by host policy), and `Tell the model` issues `ui/update-model-context` (pushes the card's UI state into the model's next turn with no visible conversation item). `tools/call` and `ui/message` are also serviced by the host (the sample doesn't wire buttons for them).
- **M-iv** (live): the `note` input persists `widgetState` via `ui/set-widget-state` (a UI-only blob stored server-side, keyed to the item) and is restored from the `ui/initialize` result — type a note, reload the thread or restart Desktop, and it returns. The card re-themes live (`ui/notifications/host-context-changed`) when you toggle Desktop light/dark. The `Expand` button issues `ui/request-display-mode` (`fullscreen`); the host renders the card in a portal overlay (or a floating window for `pip`) and returns the granted mode — and because the iframe re-mounts in the expanded surface, the `widgetState` note survives the expand.

## Data path B (direct `fetch` to your loopback backend)

M-iii widens the iframe CSP from `_meta.ui.csp` so a UI can `fetch` its own backend directly
(`connectDomains` → `connect-src`). The CSP is built host-side from your **server-validated**
descriptor — never from the iframe — and stays network-denied when you declare no `connectDomains`.

Because the iframe is sandboxed **without** `allow-same-origin`, its requests carry an **opaque
(`null`) origin**. Your loopback backend must therefore answer CORS for that origin:

- Respond with **`Access-Control-Allow-Origin: *`** (an opaque origin cannot be matched by a
  specific allow-list, and `*` is safe here only because the surface is loopback-bound).
- Do **not** rely on credentials (`Access-Control-Allow-Credentials` cannot combine with `*`); keep
  the loopback surface unauthenticated or token-in-body.
- Bind the backend to **loopback only** (`127.0.0.1`) and list its exact origin in `_meta.ui.csp.connectDomains`.

(An SDK helper that pre-configures this CORS posture ships in M-v.)

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

> The `Open in Sample` and `Tell the model` buttons exercise the M-iii bridge actions
> (`ui/open-link` and `ui/update-model-context`). After clicking `Tell the model`, the card's
> state reaches the agent on its next turn — ask the agent what the sample card shows to confirm.
