# DotCraft Desktop development

This directory contains the Electron client for DotCraft.

## Prerequisites

- Node.js 20+ LTS and npm
- On Windows x64, Visual Studio Build Tools with Desktop development with C++ and a Windows SDK
- A DotCraft AppServer binary at `../build/dotcraft/dotcraft` (`dotcraft.exe` on Windows)

Publish DotCraft from the repository root before starting Desktop:

```bash
dotnet publish src/DotCraft.App/DotCraft.App.csproj -p:PublishProfile=ReleaseProfile
```

Rebuild DotCraft and restart the development server after backend changes.

## Develop

```bash
npm install
npm run dev
```

Pass a workspace with `--workspace` when needed. Development mode otherwise uses the current working directory.
Use `npm run dev:debug` only when attaching Playwright to Desktop through CDP.

## Validate and package

| Command | Purpose |
|---|---|
| `npm run typecheck` | Check the main, preload, and renderer TypeScript projects |
| `npm test` | Run the Vitest unit tests |
| `npm run build` | Build the production Electron bundles |
| `npm run build:native` | Build the Windows x64 browser focus module |
| `npm run test:browser-focus` | Verify browser input and composition isolation in a separate Electron process |
| `npm run pack` | Create an unpacked application |
| `npm run dist` | Create platform installers |

Installer artifacts are written to `desktop/dist/`.

Windows x64 development and packaging build the browser focus module automatically. Its native bindings target the pinned Electron version. An Electron upgrade requires updating the bindings and rerunning the native tests.
