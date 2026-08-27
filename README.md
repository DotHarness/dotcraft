<div align="center">

[![Release](https://img.shields.io/github/v/release/DotHarness/dotcraft)](https://github.com/DotHarness/dotcraft/releases)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](./LICENSE)
[![NuGet](https://img.shields.io/nuget/v/DotCraft.Harness?logo=nuget&label=NuGet)](https://www.nuget.org/profiles/DotHarness)
[![npm](https://img.shields.io/npm/v/%40dotcraft%2Fsdk?logo=npm&label=npm)](https://www.npmjs.com/org/dotcraft)

![DotCraft — the agent runtime for real projects](https://github.com/DotHarness/resources/raw/master/dotcraft/intro.png)

[中文](./README_ZH.md) · [Documentation](https://www.dotcraft.net/) · [Quick Start](https://www.dotcraft.net/getting-started) · [Releases](https://github.com/DotHarness/dotcraft/releases) · [License](./LICENSE)


DotCraft is an open-source, self-hosted, and embeddable **project-native AI agent
runtime** built with C#/.NET.

</div>

---

## Why DotCraft?

DotCraft turns projects into **extensible environments for AI agents**.

![One agent runtime, three ways to make it yours — Desktop App, AppServer + SDK, Harness Package](https://github.com/DotHarness/resources/raw/master/dotcraft/entry.png)

- **Modern agent capabilities, ready to use:** Plan, Subagents, Agent Teams, Automations, Goals, Dreams, Dynamic Workflows, and more are built in.
- **Your work travels with the project:** Conversations, memory, agents, skills, and plugins move with the project, so you can switch entry points and keep going.
- **Lower token costs:** DotCraft maximizes prefix cache reuse, and Subagents can reuse the parent session's cache.
- **Run it your way:** Run DotCraft locally or on your own server and choose a compatible model provider.
- **A complete Agent Harness for .NET:** Bring complete agent capabilities into the .NET applications you already build, from desktop tools to services and automation.
- **Easy to integrate with existing products:** APIs, SDKs, App Binding, and plugins bring DotCraft directly into existing applications.

## Quick start

DotCraft supports OpenAI, Anthropic model providers, or you can sign in using your ChatGPT subscription.

![Every model has a way in — OpenAI protocol, Anthropic protocol, or a ChatGPT subscription](https://github.com/DotHarness/resources/raw/master/dotcraft/providers.png)

### Desktop

1. Download the latest build from [GitHub Releases](https://github.com/DotHarness/dotcraft/releases).
2. Open a real project folder as a workspace.
3. Configuration model providers and preferences.

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
