<div align="center">

[![Release](https://img.shields.io/github/v/release/DotHarness/dotcraft)](https://github.com/DotHarness/dotcraft/releases)
[![NuGet](https://img.shields.io/nuget/v/DotCraft.Harness?logo=nuget&label=NuGet)](https://www.nuget.org/profiles/DotHarness)
[![npm](https://img.shields.io/npm/v/%40dotcraft%2Fsdk?logo=npm&label=npm)](https://www.npmjs.com/org/dotcraft)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](./LICENSE)
[![Platforms](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)](https://github.com/DotHarness/dotcraft/releases)
[![Discussions](https://img.shields.io/badge/community-Discussions-brightgreen)](https://github.com/DotHarness/dotcraft/discussions)

![DotCraft — an agent runtime you embed and extend](https://github.com/DotHarness/resources/raw/master/dotcraft/intro.png)

[中文](./README_ZH.md) · [Documentation](https://www.dotcraft.net/) · [Quick Start](https://www.dotcraft.net/getting-started) · [Releases](https://github.com/DotHarness/dotcraft/releases) · [License](./LICENSE)


DotCraft is an open-source, self-hosted **AI agent runtime** built with C#/.NET. Run it as a desktop app, add it to your own .NET application, and extend both with plugins.

</div>

## Why DotCraft?

DotCraft turns your projects into **extensible environments for AI agents**.

![One agent runtime, three ways to make it yours — Desktop App, AppServer + SDK, Harness Package](https://github.com/DotHarness/resources/raw/master/dotcraft/entry.png)

- **Complete agent capabilities, ready to use:** Plan, subagents, Automations, Goals, Dreams, and Dynamic Workflows are built in, so you do not have to assemble agent infrastructure from scratch.
- **Agent customization and remote collaboration:** Agent Builder and Agent Profiles let you create reusable, specialized agents. Remote tool calls let them work across computers.
- **Your work travels with the project:** Conversations, memory, agents, skills, and plugins live with the workspace. Move between Desktop, the CLI, editors, and bots, and pick up where you left off.
- **Bring a complete agent runtime into your product:** Embed the same runtime behind DotCraft Desktop in .NET tools, services, and automations, or connect existing products through AppServer, SDKs, and App Binding.
- **Let the agent extend its own runtime:** Ask the agent to create and build a .NET plugin in the workspace. It uses the same extension model as built-in features to add tools, prompts, commands, and lifecycle logic, and can be replaced while the host keeps running.
- **Shape Desktop around your workflow:** TypeScript and React plugins use native UI components to extend Desktop's interface and interactions.
- **Own your deployment, models, and costs:** Run locally or on your own server, and choose a compatible model provider or use your ChatGPT subscription. DotCraft keeps reusable prompt prefixes byte-stable to improve provider cache reuse and lower repeated-input costs.

## Explore DotCraft

### [Desktop](https://www.dotcraft.net/features/entry-points/desktop)

[![Your projects. Your agents. One desktop.](https://github.com/DotHarness/resources/raw/master/dotcraft/product-desktop.png)](https://www.dotcraft.net/features/entry-points/desktop)

Work with agents on your projects in one desktop app.

### [DotCraft.Harness](https://www.dotcraft.net/developing/harness/)

[![Build agents into your .NET apps.](https://github.com/DotHarness/resources/raw/master/dotcraft/product-harness.png)](https://www.dotcraft.net/developing/harness/)

Embed a complete agent runtime in your .NET applications.

### [Oratorio](https://www.dotcraft.net/features/oratorio)

[![From task to reviewed delivery.](https://github.com/DotHarness/resources/raw/master/dotcraft/product-oratorio.png)](https://www.dotcraft.net/features/oratorio)

Manage agent tasks from assignment to review on one board.

### [Satellite](https://www.dotcraft.net/features/agent-system/satellite)

[![Your agents. Another machine.](https://github.com/DotHarness/resources/raw/master/dotcraft/product-satellite.png)](https://www.dotcraft.net/features/agent-system/satellite)

Let your agents work in an approved shared folder on another computer.

### [Avatar](https://www.dotcraft.net/developing/sdks/typescript#avatar-package)

[![Give your agents character.](https://github.com/DotHarness/resources/raw/master/dotcraft/product-avatar.png)](./sdk/typescript/packages/avatar/README.md)

Give your agents personality with expressive, customizable avatars.

## Quick start

DotCraft supports OpenAI, Anthropic model providers, or you can sign in using your ChatGPT subscription.

![Every model has a way in — OpenAI protocol, Anthropic protocol, or a ChatGPT subscription](https://github.com/DotHarness/resources/raw/master/dotcraft/providers.png)

### Desktop

1. Download the latest build from [GitHub Releases](https://github.com/DotHarness/dotcraft/releases).
2. Open a real project folder as a workspace.
3. Configure model providers and preferences.

### CLI

macOS / Linux:

```bash
curl -fsSL https://www.dotcraft.net/install.sh | bash
```

Windows PowerShell:

```powershell
irm https://www.dotcraft.net/install.ps1 | iex
```

See [Getting Started](https://www.dotcraft.net/getting-started) for the complete guide.


## Contributing

Start with [CONTRIBUTING.md](./CONTRIBUTING.md).

## Credits

Inspired by [nanobot](https://github.com/HKUDS/nanobot), [codex](https://github.com/openai/codex) and [agent-framework](https://github.com/microsoft/agent-framework).

Special thanks to:

- [HKUDS/nanobot](https://github.com/HKUDS/nanobot)
- [openai/codex](https://github.com/openai/codex)
- [microsoft/agent-framework](https://github.com/microsoft/agent-framework)
- [alibaba/OpenSandbox](https://github.com/alibaba/OpenSandbox)
- [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk)
- [openai/symphony](https://github.com/openai/symphony)

## License

Apache License 2.0
