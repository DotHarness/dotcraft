<div align="center">

<img alt="DotCraft: an agent runtime you embed and extend. Open-source agent desktop app, and the runtime behind it. The desktop app on the orbit project, where two subagents have finished and the agent is running the contrast audit." src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/hero.webp" width="830">

[![Release](https://img.shields.io/github/v/release/DotHarness/dotcraft)](https://github.com/DotHarness/dotcraft/releases)
[![NuGet](https://img.shields.io/nuget/v/DotCraft.Harness?logo=nuget&label=NuGet)](https://www.nuget.org/profiles/DotHarness)
[![npm](https://img.shields.io/npm/v/%40dotcraft%2Fsdk?logo=npm&label=npm)](https://www.npmjs.com/org/dotcraft)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](./LICENSE)
[![Platforms](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux%20%7C%20Android-lightgrey)](https://github.com/DotHarness/dotcraft/releases)

[中文](./README_ZH.md) · [Documentation](https://www.dotcraft.net/) · [Live demo](https://www.dotcraft.net/demo/) · [Quick Start](https://www.dotcraft.net/start/)

</div>

DotCraft is an open-source agent runtime that runs on your own machine. Use the desktop app to code and get work done, keep up with it from your phone, or build the same agent into your own apps and extend it with plugins.

## Why DotCraft

- **One agent, many doors.** Desktop, the CLI, your IDE, your phone and chat bots share one workspace, so a task started in one can be picked up in another. Connect Desktop to DotCraft on a server over SSH, or let the agent work on another computer through Satellite.
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

See [Getting started](https://www.dotcraft.net/start/) for the full walkthrough.

## Desktop

<p align="center"><a href="https://www.dotcraft.net/start/desktop"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/desktop.webp" width="830" alt="DotCraft Desktop on Plan: ship dark mode. Two subagents, Theme toggle and Snapshot key screens, have finished, and the agent is running npm run test:contrast before it calls the work done."></a></p>

A workbench where the agent plans in your project, hands parts to subagents, and checks the result before it says done.

**Ready out of the box**

- **Plan** drafts the steps first and waits for your go-ahead.
- **Subagents** split a task and work on the parts side by side.
- **In-app Browser** opens your app so the agent can check its own work, and you can point at anything to change.
- **Computer Use** lets the agent operate the Windows apps you allow.
- **Agent Builder** turns a description into a reusable agent.
- **Automations**, **Goals**, **Dreams** and **Dynamic Workflows** keep work moving when you are away.
- **Your pet** lives on the composer and finds things to wear while agents work.

[Desktop guide](https://www.dotcraft.net/start/desktop) · [Subagents](https://www.dotcraft.net/guide/subagents) · [Agent profiles](https://www.dotcraft.net/guide/agent-profiles) · [Automations](https://www.dotcraft.net/guide/automations)

## Mobile

<p align="center"><a href="https://www.dotcraft.net/start/mobile"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/mobile.webp" width="830" alt="The DotCraft phone app asking Run this command? npm run test:contrast in the orbit project, with Allow once, Allow for session and Reject."></a></p>

Keep up with your agent from your phone while it works on your computer. See what's running, answer approvals and questions, add a message or stop a chat, and start new chats in any project.

**Get it:** [download](https://github.com/DotHarness/dotcraft/releases) the Android app, then scan the code under **Settings → Connections → Phones** in Desktop.

[Phone app guide](https://www.dotcraft.net/start/mobile) · [Access from anywhere](https://www.dotcraft.net/contribute/lifecycle/hub#access-from-anywhere)

## CLI

<p align="center"><a href="https://www.dotcraft.net/start/entry-points"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/cli.webp" width="830" alt="A terminal running dotcraft exec &quot;Which colors in src/components are still hard-coded?&quot;: the answer lists the hard-coded colors left in Card.css and Chart.css and notes that Button.css already uses theme tokens."></a></p>

Run a task with one command and get the answer in your terminal, on your laptop, a server or in CI.

```bash
dotcraft exec "Which colors in src/components are still hard-coded?"
```

[Entry points](https://www.dotcraft.net/start/entry-points) · [IDEs](https://www.dotcraft.net/start/editors)

## Build it into your product

<p align="center"><a href="https://www.dotcraft.net/extend/harness/"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/harness.webp" width="830" alt="Program.cs opening a thread with ISessionService and streaming the agent's reply to Check contrast in dark mode."></a></p>

**Harness** puts the whole agent runtime behind DotCraft Desktop inside your own .NET app: sessions, tools, approvals and model providers, on your host's lifecycle.

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

Already have an app? The **SDKs** make it a DotCraft client: connect to a workspace, start a thread and run a turn, from TypeScript or .NET.

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

[Harness guide](https://www.dotcraft.net/extend/harness/) · [.NET plugins](https://www.dotcraft.net/extend/dotnet-plugins) · [SDK quickstart](https://www.dotcraft.net/extend/sdks/quickstart) · [App Binding](https://www.dotcraft.net/extend/app-binding) · [Desktop plugins](https://www.dotcraft.net/extend/desktop-plugins)

## More from DotCraft

<p align="center"><img src="https://github.com/DotHarness/resources/raw/master/dotcraft/readme/more.webp" width="830" alt="Satellite asking for access to run npm run test:contrast, the Oratorio board with Chart labels fail contrast in progress, connected chat bots, and three avatars."></p>

- [**Satellite**](https://www.dotcraft.net/guide/satellite): let your agent work on another Windows PC. The person sharing it chooses full access or one folder, sees every request at the top of the screen, and can pause sharing at any time. In Desktop, open **Settings → Connections → Satellites → Invite** and send the link; the other PC downloads DotCraft Satellite from that page.
- [**Oratorio**](https://www.dotcraft.net/guide/oratorio/): your project board, built into Desktop. Local tasks, GitHub issues and pull requests, and GitLab issues and merge requests land on one board; hand a card to the agent, follow the run, review the result. Select **Oratorio** in the Desktop sidebar. [Workflow](https://www.dotcraft.net/guide/oratorio/workflow)
- [**Chat bots**](https://www.dotcraft.net/guide/channels/): put DotCraft in the group chat so teammates can ask about the project without opening Desktop. Feishu, QQ, Telegram, WeCom and WeChat connect, and their conversations share the workspace with every other entry point. Open **Channels** in Desktop, pick a platform and fill in its credentials.
- [**Avatar**](https://www.dotcraft.net/extend/sdks/typescript#avatar-package): the characters behind Desktop's built-in pet and every Agent Profile's look, published as an npm package of React components so you can show the same characters in your own app. They move with the agent's state, 160+ collectible items dress them up, and the same name always draws the same avatar. `npm install @dotcraft/avatar`

## Contributing

Start with [CONTRIBUTING.md](./CONTRIBUTING.md).

## Credits

DotCraft is inspired by and thankful to:

- [HKUDS/nanobot](https://github.com/HKUDS/nanobot)
- [openai/codex](https://github.com/openai/codex)
- [microsoft/agent-framework](https://github.com/microsoft/agent-framework)
- [openai/symphony](https://github.com/openai/symphony)

## License

Apache License 2.0
