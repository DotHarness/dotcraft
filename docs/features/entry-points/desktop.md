# Desktop

Desktop is the recommended way to start with DotCraft. It puts everything in one window — workspaces, sessions, diffs, plans, model configuration, automation review, and live status — so you can drive the agent visually instead of from the command line. (Under the hood it's an AppServer client, sharing the same workspace as every other entry point.)

For first-time setup, follow [Getting Started](../../getting-started) for download, workspace selection, and model configuration. This page focuses on Desktop-specific panels and settings.

## Installation

### Use a Release

1. Download an installer from [GitHub Releases](https://github.com/DotHarness/dotcraft/releases).
2. Start DotCraft.
3. Choose a project folder as the workspace.

### Run from Source

```bash
cd desktop
npm install
npm run dev
```

When running from source, Desktop looks for `dotcraft` on `PATH`. If it cannot find it, set the AppServer / `dotcraft` binary path in settings. Build installer artifacts with `npm run dist`; outputs land in `desktop/dist/`.

## Startup Arguments

```bash
DotCraft --app-server /path/to/dotcraft
DotCraft --workspace /path/to/project
```

## Desktop-Specific Settings

| Section | Purpose |
|---|---|
| **Settings → Profile** | Token-activity heatmap for this workspace, lifetime/peak/streak stats, optional GitHub identity |
| **Settings → General** | Current workspace path, AppServer binary path, language |
| **Settings → Personalization** | Long-term memory / Dreams switches, run-now, auto-update, reset memory |
| **Settings → Model Providers** | Personal providers, credentials, endpoints, workspace provider and model |
| **Settings → Sub Agents** | Reuse external CLI sessions (see [SubAgents](../agent-system/subagents)) |
| **Settings → Connection** | Switch between local Hub and remote AppServer |

### Profile

- **Token activity** — a GitHub-contribution-style heatmap of daily token usage across all traced sessions in the current workspace. Switch between **Daily**, **Weekly**, and **Cumulative** colorings.
- **Stats** — lifetime tokens, single-day peak, longest task (the longest single agent turn), current streak, and longest streak.
- **Identity (optional)** — link a GitHub username to show its public avatar and handle in the header; otherwise an initials avatar is shown. When a signed-in ChatGPT provider is detected, its plan (e.g. Pro) appears as a badge.
- Requires tracing to be enabled for the workspace; otherwise the activity view is unavailable.

### Personalization → Dreams

- **Run now** — trigger a Dreams consolidation immediately.
- **Auto-update Dreams** — off: new Dreams stay pending; on: future successful runs auto-apply as active.
- **Manage Dreams** — list of recent runs; each opens Dashboard for diff, trace, apply, discard, cancel, archive.
- **Reset memory** — clear `MEMORY.md`, `HISTORY.md`, `.craft/dreams/`, and derived caches in one click. It does not delete sessions, config, skills, or automations.

See [Memory & Dreams](../agent-system/memory) for the full picture.

### Model Providers

- Provider credentials and endpoints are written to personal `~/.craft/config.json`, **not** the workspace.
- The workspace only stores `ProviderId` and `Model`, so shared workspace config never holds secrets.
- Desktop currently supports OpenAI and Anthropic providers.
- Use **Test** to check credential and model-list reachability. If a provider cannot list models, save it and type the model name manually.

### Connection (Local vs Remote)

- **Local (default)**: Desktop launches or discovers a workspace AppServer through Hub; multiple entries share the same process.
- **Remote**: connect to an existing WebSocket AppServer. Desktop does not restart the remote process, only tests and switches.
- Remote URL/token changes are first probed against a draft connection. Failures are not saved, so a bad URL never traps you on the next start.
- When Desktop is launched with `--remote`, the persistent connection switch in Settings is disabled.

## What's New

After Desktop is upgraded, **What's New** appears once when you enter a ready workspace UI and highlights the current version's new capabilities. Animated previews are downloaded from the DotHarness resources repo, verified, and cached locally; the automatic prompt waits for previews, while manual reopen shows text and placeholders immediately. Reopen any time via **Help → What's New** or the version label at the bottom of the sidebar. The newest release is shown expanded; older releases are collapsed behind a **Past releases** toggle so you can review earlier highlights when you want them.

## Updates

On startup, DotCraft checks [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) for a newer release tag. When a platform installer is available, a highlighted download button appears in the title bar. Select it to review the release, download the installer with progress, then DotCraft quits and opens the downloaded installer.

## Usage Examples

| Scenario | Desktop path |
|---|---|
| First-time setup | Choose workspace → configure model → create session |
| Inspect agent work | Open session detail, diff, trace, or Dashboard |
| Review automation tasks | Open Automations and inspect pending review items |
| Switch projects | Choose another workspace so config and tasks stay project-scoped |
| Take back SubAgent control | Settings → Sub Agents → disable reuse external CLI sessions |

## Advanced

- Desktop is an AppServer client, so it shares the same [session core](../../developing/architecture/session-core) with ACP and external channels — a thread you start here can continue in another AppServer client.
- Image attachments survive a restart; reopen a session and its thumbnails are still there.
- Markdown surfaces render fenced `mermaid` / `mmd` code blocks as Mermaid diagrams. If a diagram cannot be rendered, Desktop falls back to the source block.

## Related docs

- [Getting Started](../../getting-started)
- [Entry Points Overview](./)
- [Observability](../self-hosted/observability)
- [Settings Lifecycle](../../developing/lifecycle/settings-lifecycle)
