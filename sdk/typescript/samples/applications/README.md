# TypeScript application samples

Start with one focused example:

| Example | Purpose |
| --- | --- |
| `first-run.ts` | Connect locally, start a thread, run once, and close cleanly. |
| `continue-thread.ts` | Resume a thread by ID and continue its conversation. |
| `multimodal-input.ts` | Send text and a local image as structured input parts. |
| `models-and-mcp.ts` | List models and inspect MCP runtime status for a thread. |
| `application.ts` | Comprehensive local/remote, streaming, callback, and Runtime Dynamic Tool example. |

`application.ts` is the complete application example and uses only the public `@dotcraft/sdk` entry point. It demonstrates:

- Hub-managed local and direct remote connections;
- streamed Run events;
- an approval handler;
- a user-input handler;
- a validated Runtime Dynamic Tool handler; and
- clean shutdown in a `finally` block.

The example is intentionally non-interactive. It declines approval requests and returns no answers to user-input requests, so replace those callbacks with your application's UI or policy before using the pattern in production.

## Run locally

From `sdk/typescript`, install dependencies and build the SDK and examples:

```bash
npm install
npm run build:example
node samples/applications/dist/first-run.js /absolute/path/to/workspace
node samples/applications/dist/continue-thread.js /absolute/path/to/workspace
node samples/applications/dist/multimodal-input.js /absolute/path/to/workspace /absolute/path/to/image.png
node samples/applications/dist/models-and-mcp.js /absolute/path/to/workspace
```

Run the comprehensive example with:

```bash
npm run example -- local /absolute/path/to/workspace
```

## Connect to a remote AppServer

Pass the WebSocket URL. Keep an authentication token in the environment rather than embedding it in source:

```bash
DOTCRAFT_TOKEN=your-token npm run example -- remote ws://server:9100/ws
```

```powershell
$env:DOTCRAFT_TOKEN = "your-token"
npm run example -- remote ws://server:9100/ws
```

Omit `DOTCRAFT_TOKEN` when the server does not require one.

## Validate without connecting

```bash
npm run typecheck:example
```
