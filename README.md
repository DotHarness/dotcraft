<div align="center">

<img alt="DotCraft: an agent runtime you embed and extend. You hand the desktop app a task, it plans the work and ticks it off." src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/hero.webp" width="830">

[![Release](https://img.shields.io/github/v/release/DotHarness/dotcraft)](https://github.com/DotHarness/dotcraft/releases)
[![NuGet](https://img.shields.io/nuget/v/DotCraft.Harness?logo=nuget&label=NuGet)](https://www.nuget.org/profiles/DotHarness)
[![npm](https://img.shields.io/npm/v/%40dotcraft%2Fsdk?logo=npm&label=npm)](https://www.npmjs.com/org/dotcraft)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](./LICENSE)
[![Platforms](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)](https://github.com/DotHarness/dotcraft/releases)
[![Discussions](https://img.shields.io/badge/community-Discussions-brightgreen)](https://github.com/DotHarness/dotcraft/discussions)

[中文](./README_ZH.md) · [Documentation](https://www.dotcraft.net/) · [Quick Start](https://www.dotcraft.net/getting-started) · [Releases](https://github.com/DotHarness/dotcraft/releases) · [License](./LICENSE)

</div>

DotCraft is an open-source AI agent that runs on your own machine. Use the desktop app to code and get work done, or build the same agent into your own apps and extend it with plugins.

## Why DotCraft

- **One agent, many doors.** Desktop, the CLI, your IDE and chat bots share one workspace, so a task started in one can be picked up in another. Connect Desktop to DotCraft on a server over SSH, follow and answer your chats from an Android phone, or let the agent work on another computer through Satellite.
- **Yours to build on.** Embed the runtime behind DotCraft Desktop in your .NET app, or connect an existing product through the SDKs and App Binding. .NET plugins add tools, commands and lifecycle logic, and the agent can write one and swap it in while the host keeps running. React plugins reshape Desktop's interface.
- **Your deployment, your costs.** Run it on your machine or your own server, with any compatible model provider or your ChatGPT subscription. Byte-stable prompt prefixes let providers reuse their cache.
- **One set of keys for the team.** One machine holds the API keys and sign-ins; DotCraft on everyone else's machine calls models through it, without storing keys or reaching providers directly.

## Quick start

**Desktop**

1. Download the installer for Windows, macOS or Linux from [Releases](https://github.com/DotHarness/dotcraft/releases).
2. Open a project folder as a workspace.
3. Connect a model: any OpenAI-compatible or Anthropic-compatible provider with an API key, or sign in with your ChatGPT subscription.

**CLI**

```bash
# macOS / Linux
curl -fsSL https://www.dotcraft.net/install.sh | bash
```

```powershell
# Windows PowerShell
irm https://www.dotcraft.net/install.ps1 | iex
```

See [Getting started](https://www.dotcraft.net/getting-started) for the full walkthrough.

## Desktop

<p align="center"><a href="https://www.dotcraft.net/features/entry-points/desktop"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/header-desktop.png" width="830" alt="DotCraft Desktop: the 'Ship dark mode' plan with two steps done, and the Subagents tab with two subagents working on the next steps."></a></p>

A workbench where the agent plans in your project, hands parts to subagents, and checks the result before it says done.

**Get it:** [Download](https://github.com/DotHarness/dotcraft/releases) for Windows, macOS or Linux.

<p align="center"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/desktop-strip.webp" width="830" alt="Four Desktop features in turn: the in-app browser checking your app's settings page, Computer Use working in another Windows app, Agent Builder setting up a new agent, and the pet finding Dragon wings to wear."></p>

**Ready out of the box**

- **Plan** drafts the steps first and waits for your go-ahead.
- **Subagents** split a task and work on the parts side by side.
- **In-app Browser** opens your app so the agent can check its own work, and you can point at anything to change.
- **Computer Use** lets the agent operate the Windows apps you allow.
- **Agent Builder** turns a description into a reusable agent.
- **Automations**, **Goals**, **Dreams** and **Dynamic Workflows** keep work moving when you are away.
- **Your pet** lives on the composer and finds things to wear while agents work.

[Desktop guide](https://www.dotcraft.net/features/entry-points/desktop) · [Subagents](https://www.dotcraft.net/features/agent-system/subagents) · [Agent profiles](https://www.dotcraft.net/features/agent-system/agent-profiles) · [Automations](https://www.dotcraft.net/features/agent-system/automations)

## Mobile

<p align="center"><a href="https://www.dotcraft.net/features/entry-points/mobile"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/header-mobile.png" width="830" alt="DotCraft on an Android phone asking to run npm run test:contrast, with Allow once."></a></p>

Keep up with your agent from your phone while it works on your computer. See what's running, answer approvals and questions, add a message or stop a chat, and start new chats in any project.

**Get it:** [download](https://github.com/DotHarness/dotcraft/releases) the Android app, then scan the code under **Settings → Connections → Phones** in Desktop.

[Phone app guide](https://www.dotcraft.net/features/entry-points/mobile) · [Access from anywhere](https://www.dotcraft.net/developing/lifecycle/hub#access-from-anywhere)

## CLI

<p align="center"><a href="https://www.dotcraft.net/features/entry-points/"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/header-cli.png" width="830" alt="A terminal running dotcraft exec: the agent answers with the hard-coded colors in Card.css and Chart.css."></a></p>

Run a task with one command and get the answer in your terminal, on your laptop, a server or in CI.

```bash
dotcraft exec "Which colors in src/components are still hard-coded?"
```

[Entry points](https://www.dotcraft.net/features/entry-points/) · [IDEs](https://www.dotcraft.net/features/entry-points/editors)

## Satellite

<p align="center"><a href="https://www.dotcraft.net/features/agent-system/satellite"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/header-satellite.png" width="830" alt="Another PC's screen with the Satellite capsule at the top: Ann is requesting access to run npm run test:contrast, with Deny and Allow once."></a></p>

Let your agent work on another Windows PC. The person sharing it chooses full access or one folder, sees every request at the top of the screen, and can pause sharing at any time.

**Get it:** in Desktop, open **Settings → Connections → Satellites → Invite** and send the link. The other PC downloads DotCraft Satellite from that page.

[Satellite guide](https://www.dotcraft.net/features/agent-system/satellite)

## Oratorio

<p align="center"><a href="https://www.dotcraft.net/features/oratorio"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/header-oratorio.png" width="830" alt="The Oratorio board: one issue in progress with the agent working on it, one pull request in review with checks passing."></a></p>

Your project board, built into Desktop. Local tasks, GitHub issues and pull requests, and GitLab issues and merge requests land on one board; hand a card to the agent, follow the run, review the result.

**Get it:** built in. Select **Oratorio** in the Desktop sidebar.

[Oratorio guide](https://www.dotcraft.net/features/oratorio) · [Workflow](https://www.dotcraft.net/features/oratorio/workflow)

## Chat bots

<p align="center"><a href="https://www.dotcraft.net/features/channels/"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/header-chat-bots.png" width="830" alt="The Channels page in Desktop with Feishu, QQ, Telegram and WeCom connected."></a></p>

Put DotCraft in the group chat so teammates can ask about the project without opening Desktop. Feishu / Lark, QQ, Telegram, WeCom and WeChat connect, and their conversations share the workspace with every other entry point.

**Get it:** built in. Open **Channels** in Desktop, pick a platform and fill in its credentials.

[Channels & bots](https://www.dotcraft.net/features/channels/)

## Harness

<p align="center"><a href="https://www.dotcraft.net/developing/harness/"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/header-harness.png" width="830" alt="Program.cs creating a thread with ISessionService and streaming the agent's reply as it is written."></a></p>

The whole agent runtime behind DotCraft Desktop, inside your own .NET app: sessions, tools, approvals and model providers, on your host's lifecycle.

```bash
dotnet add package DotCraft.Harness
```

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDotCraftHarness(appConfig, options =>
    options.WorkspacePath = workspacePath);

using var host = builder.Build();
await host.StartAsync();
```

[Harness guide](https://www.dotcraft.net/developing/harness/) · [.NET plugins](https://www.dotcraft.net/developing/integrations/dotnet-plugins)

## SDKs

<p align="center"><a href="https://www.dotcraft.net/developing/sdks/quickstart"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/header-sdks.png" width="830" alt="app.ts starting a thread with the TypeScript SDK and streaming the agent's reply as it is written."></a></p>

Make your own app a DotCraft client: connect to a workspace, start a thread and run a turn, from TypeScript or .NET.

```bash
npm install @dotcraft/sdk        # or: dotnet add package DotCraft.Sdk
```

```ts
import { DotCraft } from "@dotcraft/sdk";

const dotcraft = await DotCraft.local({ workspacePath: "/path/to/workspace" });
const thread = await dotcraft.threads.start({ userId: "me" });
const result = await thread.run("Check contrast in dark mode.");
console.log(result.text);
```

[SDK quickstart](https://www.dotcraft.net/developing/sdks/quickstart) · [App Binding](https://www.dotcraft.net/developing/integrations/app-binding) · [Desktop plugins](https://www.dotcraft.net/developing/integrations/desktop-plugins)

## Avatar

<p align="center"><a href="https://www.dotcraft.net/developing/sdks/typescript#avatar-package"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/header-avatar.webp" width="830" alt="A crowd of DotCraft robots in different outfits, with one in front wearing a cloud dragon and star glasses."></a></p>

The robot you see in Desktop, as a React component. It moves with the agent's state, 160+ collectible items dress it up, and the same name always draws the same avatar.

```bash
npm install @dotcraft/avatar
```

[Avatar package](https://www.dotcraft.net/developing/sdks/typescript#avatar-package)

## Contributing

Start with [CONTRIBUTING.md](./CONTRIBUTING.md).

## Credits

Inspired by [nanobot](https://github.com/HKUDS/nanobot), [codex](https://github.com/openai/codex) and [agent-framework](https://github.com/microsoft/agent-framework).

Special thanks to:

- [HKUDS/nanobot](https://github.com/HKUDS/nanobot)
- [openai/codex](https://github.com/openai/codex)
- [microsoft/agent-framework](https://github.com/microsoft/agent-framework)
- [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk)
- [openai/symphony](https://github.com/openai/symphony)

## License

Apache License 2.0
