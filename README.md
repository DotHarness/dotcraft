<div align="center">

![intro](https://github.com/DotHarness/resources/raw/master/dotcraft/intro.png)

[中文](./README_ZH.md) · [Documentation](https://www.dotcraft.net/) · [Getting Started](https://www.dotcraft.net/getting-started) · [Download Release](https://github.com/DotHarness/dotcraft/releases) · [DeepWiki](https://deepwiki.com/DotHarness/dotcraft) · [License](./LICENSE)

AI Agent lives in your project. All in one workspace.

</div>

## About

DotCraft is a .NET 10 / C# Agent Harness. It organizes AI workflows around a real project folder, allowing multiple entry points to share one session core, configuration, skills, tools, tasks, and observability surface.

- Project first: plugins, skills, sessions, and memory are integrated with the project, the agent can better understand your project.
- Unified session model: CLI, Desktop, TUI, chatbots, etc, all applications reuse the same execution engine.
- Observability and governance: approvals, traces, Dashboard, Hooks, and sandbox settings make agent workflows easier to inspect and control.
- Extensibility and integration: AppServer, SDKs, and plugins support custom entry points and business workflows.
- Bring your own model: works with any OpenAI- or Anthropic-protocol provider, reuses an active ChatGPT subscription, and natively supports DeepSeek V4 and MiMo V2.5 reasoning models.

## Highlights

DotCraft's capabilities, grouped by what they help you do.

### Project-First Workspaces

*Each project is its own workspace — running locally or on your own server.*

#### Multi-Workspace — Every Project Gets Its Own Agent

![DotCraft multi-workspace](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/multi-workspace.gif)

Most agents reuse one setup across every project; DotCraft gives each project its own agent — its own conversations, memory, skills, plugins, and settings — so it truly understands what it's working on.

[Learn more →](https://www.dotcraft.net/features/project-first)

#### Remote Servers — Drive a Server-Hosted Agent from Desktop

![DotCraft remote servers](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/servers.gif)

Run DotCraft on your own server and connect from Desktop anytime to drive it remotely.

[Learn more →](https://www.dotcraft.net/features/self-hosted/server-deployment)

### Agents That Keep Working

*Hand off a goal or a mission, and DotCraft carries it forward.*

#### Agent Profiles — Personalized Agents for Every Workflow

![DotCraft Agent Profiles](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/agent-profile.gif)

Save purpose-built agents with their own model, tools, skills, and permissions. Pick one in a chat, assign it to a Team member, or bind it to an automation task so the right agent shows up where the work starts.

[Learn more →](https://www.dotcraft.net/features/agent-system/agent-profiles)

#### Agent Builder — Customize Your Agent by Chatting

![DotCraft Agent Builder](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/agent-builder.gif)

Tell DotCraft what kind of agent you want, then refine its instructions, tools, skills, model, and approval style in a guided conversation.

[Learn more →](https://www.dotcraft.net/features/agent-system/agent-profiles#agent-builder)

#### Teams — Multi-agent Mission Board

![DotCraft Teams](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/teams.gif)

For a complex request, DotCraft assembles a small team to split up the work, dispatch it in parallel, and bring the results back together. You hand over one ask; you get the finished mission.

[Learn more →](https://www.dotcraft.net/features/agent-system/teams)

#### Goal — Persistent Conversation Objectives

![DotCraft Goals](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/goal.gif)

Pin a long-term goal to a conversation and give it a time or usage budget — whenever things go quiet, DotCraft quietly keeps working toward it. You decide when it pauses, resumes, or wraps up.

[Learn more →](https://www.dotcraft.net/features/agent-system/automations#goals)

#### Thread Tools — The Agent Manages Its Own Conversations

![DotCraft thread tools](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/thread-tools.gif)

From inside one conversation, DotCraft can look after all the others — start a new one in the background, search and read through past ones, pass a task along, or rename, pin, and tidy them away. It handles the housekeeping itself, without ever pulling you out of the conversation you're in.

[Learn more →](https://www.dotcraft.net/features/entry-points/desktop)

#### Dreams — Background Memory Consolidation

![Dreams review flow](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/dreams.gif)

While you work, Dreams quietly reviews recent activity in the background and drafts memory entries worth keeping. You approve them at your own pace, so the agent only remembers what you've actually agreed to.

[Learn more →](https://www.dotcraft.net/features/agent-system/memory)

### Extend & Integrate

*Bring your own tools, UIs, and services into the conversation.*

#### Plugin Registry — Add Official Plugins

![DotCraft Plugin Registry](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/plugin-registry.gif)

Choose from the official plugin library and add the capabilities you want: tools, skills, apps, hooks, or Desktop views.

[Learn more →](https://www.dotcraft.net/features/agent-system/plugins-tools)

#### Lifecycle Hooks — Run Safety Steps Automatically

![DotCraft Lifecycle Hooks](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/lifecycle-hooks.gif)

Use Lifecycle Hooks to run trusted checks or project scripts at the right moment, so routine safeguards happen automatically.

[Learn more →](https://www.dotcraft.net/features/agent-system/hooks)

#### Desktop Extensions — Plugins with a Full View Inside Desktop

![DotCraft Desktop extensions](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/desktop-extensions.gif)

A plugin doesn't have to stop at adding tools — it can bring its own full interface right inside Desktop.

[Learn more →](https://www.dotcraft.net/developing/integrations/desktop-extensions)

#### Interactive Tool Cards — Tool Results You Can Act On

![DotCraft interactive tool cards](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/dynamic-tools-card.gif)

Some tool results are things you act on, not just read — a board to scan, an item to operate, a status to refresh. Those render as live, interactive cards in the conversation, where your clicks and inputs drive the agent.

[Learn more →](https://www.dotcraft.net/developing/integrations/interactive-tool-ui)

#### App — SDK-driven App Binding for External Extensions

![DotCraft App Binding](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/app.gif)

Have a service of your own? With the DotCraft SDK you can turn it into an App and plug it straight into the agent — its tools, data, and workflows become part of the conversation, with nothing extra in between.

[Learn more →](https://www.dotcraft.net/developing/integrations/app-binding)

### Connect On Your Terms

*Bring the agent to the chat apps you use, on the model you already pay for.*

| Cross Channels — One Agent Across Your Chat Apps | Channel Handoff — Continue a Desktop Conversation in Chat |
|---|---|
| ![DotCraft Channels configuration and conversations](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/channels.gif) | ![DotCraft Channel Handoff](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/channel-handoff.gif) |
| Run the same agent as a bot in QQ, WeCom, Feishu, Telegram, or WeChat — sharing one memory, skill set, and approval policy. | Hand off a Desktop conversation to a connected social channel, then keep talking in the same thread. |

[Learn more →](https://www.dotcraft.net/features/entry-points/channels)

#### ChatGPT Plan — Sign in with ChatGPT, No Extra API Costs

![Sign in with ChatGPT](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/chatgpt.gif)

Already paying for ChatGPT Plus, Pro, Team, Business, or Enterprise? Sign in with your ChatGPT account and DotCraft will run on that subscription — no separate API key, no extra usage fees.

[Learn more →](https://www.dotcraft.net/getting-started)

## Get Started

![Setup](https://github.com/DotHarness/resources/raw/master/dotcraft/setup.gif)

1. Download the desktop app from [GitHub Releases](https://github.com/DotHarness/dotcraft/releases).
2. Choose a real project folder as your workspace.
3. Follow the initialization guide to configure the workspace and model provider.
4. Create a session and send your first repository-understanding request.

See [Getting Started](https://www.dotcraft.net/getting-started) for the full guided flow.

## Documentation

| Goal | Document |
|------|----------|
| Install, configure, and run DotCraft for the first time | [Getting Started](https://www.dotcraft.net/getting-started) |
| Use the graphical desktop client | [Desktop](https://www.dotcraft.net/features/entry-points/desktop) |
| Use the full terminal interface | [TUI](https://www.dotcraft.net/features/entry-points/tui) |
| Run local automation tasks | [Automations & Goals](https://www.dotcraft.net/features/agent-system/automations) |
| Add official plugins | [Plugins & Tools](https://www.dotcraft.net/features/agent-system/plugins-tools) |
| Manage Lifecycle Hooks | [Lifecycle Hooks](https://www.dotcraft.net/features/agent-system/hooks) |
| Connect clients, bots, or custom adapters | [Channels & Bots](https://www.dotcraft.net/features/entry-points/channels) |
| Deploy DotCraft and channel bots on a server | [Server Deployment](https://www.dotcraft.net/developing/lifecycle/server-deployment) |
| Architecture, SDKs, and protocols | [Architecture](https://www.dotcraft.net/developing/architecture/overview) |

## Contributing

We welcome code, documentation, and integration contributions. Start with [CONTRIBUTING.md](./CONTRIBUTING.md).

## Credits

Inspired by [nanobot](https://github.com/HKUDS/nanobot) and [codex](https://github.com/openai/codex), and built on [agent-framework](https://github.com/microsoft/agent-framework).

Special thanks to:

- [HKUDS/nanobot](https://github.com/HKUDS/nanobot)
- [openai/codex](https://github.com/openai/codex)
- [microsoft/agent-framework](https://github.com/microsoft/agent-framework)
- [alibaba/OpenSandbox](https://github.com/alibaba/OpenSandbox)
- [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk)
- [openai/symphony](https://github.com/openai/symphony)

# Articles

Technical articles related to dotcraft.

[Why is your agent so expensive: Troubleshooting records of a 0% Prompt Cache hit rate.](https://zhuanlan.zhihu.com/p/2044201072466588522)

## License

Apache License 2.0
