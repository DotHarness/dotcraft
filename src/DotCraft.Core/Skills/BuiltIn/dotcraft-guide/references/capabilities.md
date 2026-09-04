# What DotCraft Can Do

## Answer from this session first

"What can you do", "what tools do you have", "which MCP servers are connected", "which skills are installed" are questions about this installation, not about the product. Answer from what is actually in front of you, then add product context only if it helps.

| The question | Read this |
|---|---|
| What can you do right now | Your own tool list, and the `<skills>` catalog in your system prompt |
| Which skills are available | The `<skills>` catalog. Each entry's `<location>` is the real directory on disk |
| Which MCP servers are configured | `McpServers` in `dotcraft config show --json`, plus `.mcp.json` under each directory in `<workspace>/.craft/plugins/` |
| Which MCP tools you can actually call | Your own tool list. A server can be configured but not connected |
| Which plugins are installed | `<workspace>/.craft/plugins/` and `~/.craft/plugins/`, and `Plugins.DisabledPlugins` in the merged config |
| Which model is in use | `dotcraft config show --json`: `ProviderId` and the matching `ProviderPreferences` entry |

State the difference when configuration and reality disagree — a server listed in `McpServers` whose tools are absent is worth reporting, not glossing over.

A capability you cannot see is not automatically missing, and a user asking whether something exists has not asked you to install it. Say what you can see, say what you cannot, and offer the install path only if they ask for it.

## Product catalog

Each line is a starting page, not a summary to recite. Fetch before quoting.

**Entry points.** Desktop app, IDE and editor integration over ACP, the `dotcraft` CLI, chat bots, GitHub and GitLab workflows through Oratorio, and self-hosted server deployment. All of them connect to one workspace and share its sessions, memory, skills, and tools.
`/features/entry-points/`

**Agent system.** Memory and Dreams, skills and self-learning, plugins and tools, Remote Tool Host, plugin marketplaces, connected apps, automations and goals, Dynamic Workflows, lifecycle hooks, Agent Builder, agent profiles, subagents, and workspace handoff.
`/features/agent-system/`

**Channels and bots.** QQ, WeCom, Feishu, Telegram, and Weixin adapters, configured through Desktop Settings > Channels.
`/features/channels/`

**Self-hosted.** Server deployment, observability, security and sandboxing.
`/features/self-hosted/server-deployment`

**Building on DotCraft.** TypeScript and .NET SDKs, in-process hosting, the AppServer JSON-RPC protocol, and the plugin APIs. Route these to `$dotcraft-api`.
`/developing/`

Prefix each path with `https://www.dotcraft.net`, or `https://www.dotcraft.net/zh` for Chinese.
