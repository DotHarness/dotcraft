# Documentation Map

Paths are stable; page content is not. Fetch the page before quoting it.

## How the site is addressed

- Base: `https://www.dotcraft.net/`
- Clean URLs. Never append `.html`. Index pages keep their trailing slash: `/features/channels/` is a page, `/features/channels` is not.
- Chinese mirror: `/zh/` plus the same path. `/features/channels/feishu` becomes `/zh/features/channels/feishu`.
- Machine index: `https://www.dotcraft.net/llms.txt` lists every page with a one-line summary. Fetch it first when the right page is not obvious, then fetch the one page you chose. Do not crawl.
- Repository: `https://github.com/DotHarness/dotcraft`, default branch `main`.

Use `WebFetch` with `extractMode` `markdown` so the VitePress chrome does not come along.

## Two pages that do not exist

- **There is no CLI reference page.** Nothing on the site documents the `dotcraft` command surface. Use `cli.md` in this skill and `--help`, and say the site does not cover it.
- **There is no developer page on authoring skills.** `/features/agent-system/skills` is the user-facing feature page. For authoring, route to `$skill-authoring`.

## Question to page

### Getting started and product

| The user asks about | Page |
|---|---|
| Installing and first run | `/getting-started` |
| What the agent system is | `/features/agent-system/` |
| Memory and Dreams | `/features/agent-system/memory` |
| Skills and self-learning | `/features/agent-system/skills` |
| Plugins and tools | `/features/agent-system/plugins-tools` |
| Remote Tool Host | `/features/agent-system/remote-tool-host` |
| Plugin marketplaces | `/features/agent-system/plugin-marketplaces` |
| Connected apps | `/features/agent-system/connected-apps` |
| Automations and goals | `/features/agent-system/automations` |
| Dynamic Workflows | `/features/agent-system/dynamic-workflows` |
| Lifecycle hooks | `/features/agent-system/hooks` |
| Agent profiles | `/features/agent-system/agent-profiles` |
| Subagents | `/features/agent-system/subagents` |
| Moving work between workspaces, context export | `/features/agent-system/workspace-handoff` |
| Observability | `/features/self-hosted/observability` |
| Security and sandbox | `/features/self-hosted/security` |

### Entry points and channels

| The user asks about | Page |
|---|---|
| Which entry points exist | `/features/entry-points/` |
| Desktop | `/features/entry-points/desktop` |
| IDE and editor integration over ACP | `/features/entry-points/editors` |
| Server deployment | `/features/self-hosted/server-deployment` |
| Chat bots in general | `/features/channels/` |
| QQ / WeCom / Feishu / Telegram / Weixin | `/features/channels/qq`, `/features/channels/wecom`, `/features/channels/feishu`, `/features/channels/telegram`, `/features/channels/weixin` |
| Channel config fields | `/features/channels/reference` |
| Oratorio | `/features/oratorio`, `/features/oratorio/workflow`, `/features/oratorio/github`, `/features/oratorio/gitlab`, `/features/oratorio/settings` |

### Configuration and operation

| The user asks about | Page |
|---|---|
| A config field in prose | `/developing/configuration` |
| Whether a setting is live yet, restart tiers | `/developing/lifecycle/settings-lifecycle` |
| AppServer mode, transport, remote connections | `/developing/lifecycle/appserver` |
| Hub and local coordination | `/developing/lifecycle/hub` |
| Architecture | `/developing/architecture/overview`, `/developing/architecture/session-core`, `/developing/architecture/session-persistence` |
| Debugging Desktop | `/developing/debugging/desktop` |
| Spec-driven development | `/developing/workflow/spec-driven-development` |

### Building against DotCraft

Route these to `$dotcraft-api` rather than answering from the page list.

| Area | Page |
|---|---|
| SDKs | `/developing/sdks/`, then `/developing/sdks/quickstart`, `/developing/sdks/runs`, `/developing/sdks/tools`, `/developing/sdks/mcp-runtime`, `/developing/sdks/channels`, `/developing/sdks/typescript`, `/developing/sdks/dotnet` |
| In-process hosting | `/developing/harness/`, then `/developing/harness/hosting-lifecycle`, `/developing/harness/configuration-paths`, `/developing/harness/threads-turns`, `/developing/harness/tools-approvals`, `/developing/harness/model-providers`, `/developing/harness/nuget-package` |
| Protocols | `/developing/protocols/appserver-protocol`, `/developing/protocols/hub-protocol`, `/developing/protocols/dashboard-api` |
| Extending DotCraft | `/developing/integrations/app-binding`, `/developing/integrations/plugin-market`, `/developing/integrations/mcp-apps`, `/developing/integrations/dotnet-plugins`, `/developing/integrations/dotnet-plugin-reference`, `/developing/integrations/desktop-plugins`, `/developing/integrations/desktop-plugin-api`, `/developing/integrations/oratorio`, `/developing/integrations/typescript-module` |
