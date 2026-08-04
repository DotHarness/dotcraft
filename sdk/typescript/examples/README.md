# TypeScript SDK example

`application.ts` is a minimal application that uses only the public `@dotcraft/sdk` entry point. It demonstrates:

- Hub-managed local and direct remote connections;
- streamed Run events;
- an approval handler;
- a user-input handler;
- a validated Runtime Dynamic Tool handler; and
- clean shutdown in a `finally` block.

The example is intentionally non-interactive. It declines approval requests and returns no answers to user-input requests, so replace those callbacks with your application's UI or policy before using the pattern in production.

## Run locally

From `sdk/typescript`, install dependencies and pass an absolute workspace path:

```bash
npm install
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
