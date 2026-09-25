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

![One agent runtime, three ways to make it yours — Desktop App, AppServer + SDK, Harness Package](https://github.com/DotHarness/resources/raw/master/dotcraft/entry.png)

**Run the app**

- **Ready out of the box:** Plan, subagents, Automations, Goals, Dreams, and Dynamic Workflows are built in. Agent Builder turns what you describe into a reusable agent.
- **Works in your apps:** With Computer use, agents operate the Windows apps you allow.
- **Pick up anywhere:** Desktop, the CLI, editors, and chat bots share one workspace. Connect to DotCraft on a server over SSH, or let agents work on another computer through Satellite.
- **Your deployment, your costs:** Run locally or on your own server with a compatible model provider or your ChatGPT subscription. Byte-stable prompt prefixes improve provider cache reuse.

**Embed and extend**

- **Build it into your product:** Embed the runtime behind DotCraft Desktop in your .NET apps, or connect existing products through AppServer, SDKs, and App Binding.
- **Extend it with plugins:** .NET plugins add tools, commands, and lifecycle logic. The agent can write one and swap it in while the host keeps running. React plugins reshape Desktop's interface.

## Explore DotCraft

![DotCraft Desktop, DotCraft.Harness, Oratorio, DotCraft Satellite and @dotcraft/avatar](https://github.com/DotHarness/resources/raw/master/dotcraft/products.webp)

- **[Desktop](https://www.dotcraft.net/features/entry-points/desktop):** Work with agents on your projects in one desktop app.
- **[Harness](https://www.dotcraft.net/developing/harness/):** Embed a complete agent runtime in your .NET applications.
- **[Oratorio](https://www.dotcraft.net/features/oratorio):** Manage agent tasks from assignment to review on one board.
- **[Satellite](https://www.dotcraft.net/features/agent-system/satellite):** Let your agents work in an approved shared folder on another computer.
- **[Avatar](https://www.dotcraft.net/developing/sdks/typescript#avatar-package):** Give your agents personality with expressive, customizable avatars.

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
- [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk)
- [openai/symphony](https://github.com/openai/symphony)

## License

Apache License 2.0
