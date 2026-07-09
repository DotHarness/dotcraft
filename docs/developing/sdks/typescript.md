# TypeScript SDK reference

Package identity and language-specific details for `@dotcraft/sdk`. For how-to, start with the [Quickstart](./quickstart).

## Package

| | |
|---|---|
| Package | `@dotcraft/sdk` (npm) |
| Module format | ESM (`"type": "module"`) |
| Runtime baseline | Node.js 20+ |
| Version | `version`, `sdkContractVersion` exported from the package |

```bash
npm install @dotcraft/sdk
```

## Entry points

The package is split into subpath exports so apps only pull in what they use:

| Entry point | Purpose |
|-------------|---------|
| `@dotcraft/sdk` | High-level application API (`DotCraft`, `DotCraftThread`, run, events). |
| `@dotcraft/sdk/wire` | Low-level JSON-RPC client, transports, raw DTOs. |
| `@dotcraft/sdk/hub` | Hub discovery, startup, and SSE helpers. |
| `@dotcraft/sdk/channel` | Channel adapter and hosted module runtime. |
| `@dotcraft/sdk/testing` | Conformance test helpers. |

`@dotcraft/sdk/channel` also exports media source helpers for channel modules. They normalize approved path, base64, and URL sources into bytes, temporary files, or upload URI strings while preserving stable tool schemas.

## Top-level exports

`DotCraft`, `DotCraftThread`, `DotCraftRunResult`, `DotCraftRunEvent`, `DotCraftError`, the typed error classes (`TurnInProgressError`, `TurnFailedError`, …), input part builders (`textPart`, `imageUrlPart`, `localImagePart`, `skillRefPart`, `commandRefPart`, `fileRefPart`), App Binding helpers (`parseAppBindingHandoff`, `appBindingToolError`, `APP_BINDING_ERROR_CODES`), and the approval decision constants.

## Channel modules

TypeScript owns the first-party hosted channel modules, each depending on `@dotcraft/sdk`:

`@dotcraft/channel-feishu`, `@dotcraft/channel-weixin`, `@dotcraft/channel-telegram`, `@dotcraft/channel-qq`, `@dotcraft/channel-wecom`. See [Channel adapters](./channels).

## Validation

```bash
cd sdk/typescript
npm run typecheck:all
npm run test:all
```

## See also

- [Quickstart](./quickstart) · [Threads & runs](./runs) · [Tools & approvals](./tools) · [Channel adapters](./channels)
- TypeScript binding spec: `specs/sdk/typescript.md`
