# Documentation Map

Paths are stable; page content is not. Fetch the page before quoting it.

## How the site is addressed

- Base: `https://www.dotcraft.net/`
- Clean URLs. Never append `.html`. Index pages keep their trailing slash: `/guide/channels/` is a page, `/guide/channels` is not.
- Chinese mirror: `/zh/` plus the same path. `/guide/channels/feishu` becomes `/zh/guide/channels/feishu`.
- Machine index: `https://www.dotcraft.net/llms.txt` lists every page with a one-line summary. Fetch it first when the right page is not obvious, then fetch the one page you chose. Do not crawl.
- Repository: `https://github.com/DotHarness/dotcraft`, default branch `main`.

Use `WebFetch` with `extractMode` `markdown` so the VitePress chrome does not come along.

## Two pages that do not exist

- **There is no CLI reference page.** Nothing on the site documents the `dotcraft` command surface. Use `cli.md` in this skill and `--help`, and say the site does not cover it.
- **There is no developer page on authoring skills.** `/guide/skills` is the user-facing feature page. For authoring, route to `$skill-authoring`.

## Question to page

### Getting started and product

| The user asks about | Page |
|---|---|
| Installing and first run | `/start/` |
| What the agent system is | `/guide/` |
| Memory and Dreams | `/guide/memory` |
| Skills | `/guide/skills` |
| Plugins and tools | `/guide/plugins-tools` |
| Satellite and Remote Tool Host | `/guide/satellite`, `/contribute/architecture/remote-tool-host` |
| Plugin marketplaces | `/guide/plugin-marketplaces` |
| Connected apps | `/guide/connected-apps` |
| Automations and goals | `/guide/automations` |
| Dynamic Workflows | `/guide/dynamic-workflows` |
| Lifecycle hooks | `/guide/hooks` |
| Agent profiles | `/guide/agent-profiles` |
| Subagents | `/guide/subagents` |
| Moving work between workspaces, context export | `/guide/workspace-handoff` |
| Observability | `/self-hosting/observability` |
| Security | `/self-hosting/security` |

### Entry points and channels

| The user asks about | Page |
|---|---|
| Which entry points exist | `/start/entry-points` |
| Desktop | `/start/desktop` |
| IDE and editor integration over ACP | `/start/editors` |
| Server deployment | `/self-hosting/server-deployment` |
| Chat bots in general | `/guide/channels/` |
| QQ / WeCom / Feishu / Telegram / Weixin | `/guide/channels/qq`, `/guide/channels/wecom`, `/guide/channels/feishu`, `/guide/channels/telegram`, `/guide/channels/weixin` |
| Channel config fields | `/configuration/channels` |
| Oratorio | `/guide/oratorio/`, `/guide/oratorio/workflow`, `/guide/oratorio/github`, `/guide/oratorio/gitlab`, `/guide/oratorio/settings` |

### Configuration and operation

| The user asks about | Page |
|---|---|
| Config files and how they layer | `/configuration/` |
| A config field in prose | `/configuration/models`, `/configuration/conversations`, `/configuration/memory-skills`, `/configuration/subagents`, `/configuration/automations`, `/configuration/hooks`, `/configuration/tools`, `/configuration/permissions`, `/configuration/plugins`, `/configuration/services` |
| Whether a setting is live yet, restart tiers | `/configuration/settings-lifecycle` |
| AppServer mode, transport, remote connections | `/contribute/lifecycle/appserver` |
| Hub and local coordination | `/contribute/lifecycle/hub` |
| Architecture | `/contribute/architecture/`, `/contribute/architecture/session-core`, `/contribute/architecture/session-persistence` |
| Debugging Desktop | `/contribute/debug-desktop` |
| Spec-driven development | `/contribute/spec-driven-development` |

### Building against DotCraft

Route these to `$dotcraft-api` rather than answering from the page list.

| Area | Page |
|---|---|
| SDKs | `/extend/sdks/`, then `/extend/sdks/quickstart`, `/extend/sdks/runs`, `/extend/sdks/tools`, `/extend/sdks/mcp-runtime`, `/extend/sdks/channels`, `/extend/sdks/typescript`, `/extend/sdks/dotnet` |
| In-process hosting | `/extend/harness/`, then `/extend/harness/hosting-lifecycle`, `/extend/harness/configuration-paths`, `/extend/harness/threads-turns`, `/extend/harness/tools-approvals`, `/extend/harness/model-providers`, `/extend/harness/nuget-package` |
| Protocols | `/extend/protocols/appserver`, `/extend/protocols/hub`, `/extend/protocols/dashboard-api` |
| Extending DotCraft | `/extend/app-binding`, `/extend/plugin-market`, `/extend/mcp-apps`, `/extend/dotnet-extensions`, `/extend/dotnet-extension-api`, `/extend/desktop-extensions`, `/extend/desktop-extension-api`, `/contribute/architecture/oratorio`, `/extend/channel-modules` |
