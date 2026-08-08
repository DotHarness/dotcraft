# DotCraft Desktop development

This directory contains the Electron client for DotCraft. User-facing installation, features, and settings are documented in the Desktop guide:

- [Desktop](../docs/features/entry-points/desktop.md)
- [Desktop 中文文档](../docs/zh/features/entry-points/desktop.md)

## Prerequisites

- Node.js 20+ LTS and npm
- A DotCraft AppServer binary at `../build/dotcraft/dotcraft` (`dotcraft.exe` on Windows)

Package DotCraft from the repository root before starting Desktop. Rebuild DotCraft and restart the development server after backend changes.

## Develop

```bash
npm install
npm run dev
```

Pass a workspace with `--workspace` when needed. Development mode otherwise uses the current working directory.

## Validate and package

| Command | Purpose |
|---|---|
| `npm test` | Run the Vitest unit tests |
| `npm run e2e` | Run the smoke end-to-end test |
| `npm run build` | Build the production application |
| `npm run pack` | Create an unpacked application |
| `npm run dist` | Create platform installers and verify the package |

Installer artifacts are written to `desktop/dist/`.
