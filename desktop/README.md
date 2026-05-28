# DotCraft Desktop

**[中文](./README_ZH.md) | English**

Electron client for [DotCraft](../README.md). Open a workspace, chat with the agent, review file changes, and run automation tasks when the server exposes that capability.

---

## Prerequisites

- **Node.js 18+** and **npm**
- **DotCraft AppServer** (`dotcraft` / `dotcraft.exe`) on `PATH` or set in app settings — [Releases](https://github.com/DotHarness/dotcraft/releases) or [build from source](../README.md#build-from-source).

---

## Quick start

```bash
cd desktop
npm install
npm run dev
```

The window uses the current workspace folder (or the path you pass with `--workspace`).

In development mode, the local Hub/AppServer is started from the repository build output at `../build/release/dotcraft(.exe)`. Rebuild DotCraft and restart the app dev server after changing backend code.

**Language:** UI strings support English (default) and Simplified Chinese (`zh-Hans`). Change under **Settings** (Ctrl+,).

---

## npm scripts

| Command | Description |
|---------|-------------|
| `npm run dev` | Dev mode with hot reload |
| `npm run build` | Production build |
| `npm run preview` | Preview built renderer in browser |
| `npm test` | Unit tests (Vitest) |
| `npm run e2e` | Smoke E2E |
| `npm run pack` / `npm run dist` | Package / installers (see below) |

---

## Installers

`npm run dist` outputs under `desktop/dist/` (NSIS installer on Windows, DMG on macOS, AppImage/deb on Linux).

On Windows, `DotCraft-Setup-<version>.exe` is the guided installer. It lets the user choose current-user vs all-users installation and change the installation directory.

```bash
npx electron-builder --win   # or --mac / --linux
```

Packaged builds check GitHub Releases on startup. When a newer release has a matching installer, the title bar shows a highlighted download button with release review and download progress.

---

## Using the app

**Workspace** — Pick or switch folder from the menu / welcome flow. One window is one workspace.

**Chat** — Sidebar lists threads; create with **New thread** (`Ctrl+N`). Type in the composer; the agent streams replies and tool use in the main column.

**Markdown diagrams** — Fenced `mermaid` / `mmd` code blocks render as Mermaid diagrams in chat, Markdown file previews, skills, and automation summaries. If rendering fails, Desktop shows the original source block.

**Image attachments** — Pasted/dropped images are saved under `.craft/attachments/images/` and user message metadata stores attachment path + MIME/name, so switching threads or restarting the app can rehydrate thumbnails from disk.

**File viewer** — File chips and local file links in chat can open in the built-in viewer, including explicitly referenced files outside the workspace. External files are authorized one file at a time.

**Detail panel** (`Ctrl+Shift+B`) — **Changes**: diffs for edits; revert/re-apply where supported. **Plan** / **Terminal** when available.

**Git** — The app can **stage selected changed files and commit** with a message from the Changes flow (`window.api.git.commit`). It does **not** replace a full Git client (no clone, pull, or branch UI here).

**Automations** (sidebar **Automations**, only if the server reports this capability):

1. **New task** — Title, description, **Agent workspace** (`Project` = repo folder, `Isolated` = separate sandbox), and **Tool policy** (workspace-scoped tools vs full auto). Submit creates a task; the server-side orchestrator runs it according to server rules.
2. The task list shows local tasks, reusable templates, schedule state, manual run controls, and delete actions.
3. Select a task to open the activity panel for live or historical agent output, thread binding, and completion summary.

**Shortcuts** — `Ctrl+B` sidebar, `Ctrl+Shift+B` detail panel (may vary by platform).

---

## Settings

AppServer binary path is stored in `settings.json` under the app user data directory; first launch searches `PATH` for `dotcraft`.

```bash
DotCraft --app-server /path/to/dotcraft
DotCraft --workspace /path/to/project
```
