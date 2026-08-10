<div align="center">

[![Release](https://img.shields.io/github/v/release/DotHarness/dotcraft)](https://github.com/DotHarness/dotcraft/releases)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](./LICENSE)
[![Platforms](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)](https://github.com/DotHarness/dotcraft/releases)
[![Discussions](https://img.shields.io/badge/community-Discussions-brightgreen)](https://github.com/DotHarness/dotcraft/discussions)

![DotCraft — the agent runtime for real projects](https://github.com/DotHarness/resources/raw/master/dotcraft/intro.png)

[中文](./README_ZH.md) · [Documentation](https://www.dotcraft.net/) · [Quick Start](https://www.dotcraft.net/getting-started) · [Releases](https://github.com/DotHarness/dotcraft/releases) · [License](./LICENSE)


DotCraft is an open-source, self-hosted, **project-native AI agent
runtime** built with C#/.NET.

</div>

---

## Why DotCraft?

DotCraft turns projects into **extensible environments for AI agents**.

- **Modern agent capabilities, ready to use:** Plan, SubAgents, Agent Teams, Automations, Goals, and more are built in.
- **Your work travels with the project:** Conversations, memory, agents, skills, and plugins move with the project, so you can switch entry points and keep going.
- **Lower token costs:** DotCraft maximizes prefix cache reuse, and SubAgents can reuse the parent session's cache.
- **Run it your way:** Run DotCraft locally or on your own server and choose a compatible model provider.
- **Easy to integrate with existing products:** APIs, SDKs, App Binding, plugins, and Desktop Extensions bring DotCraft directly into existing applications.

![desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/multi-workspace.gif)

## Quick start

### Desktop

1. Download the latest build from [GitHub Releases](https://github.com/DotHarness/dotcraft/releases).
2. Open a real project folder as a workspace.
3. Configure a supported provider or sign in with a supported ChatGPT plan.
4. Start with a concrete request:

```text
Map this repository, explain the main execution path, and tell me where a new contributor should start.
```

### CLI

macOS / Linux:

```bash
curl -fsSL https://www.dotcraft.net/install.sh | bash
```

Windows PowerShell:

```powershell
irm https://www.dotcraft.net/install.ps1 | iex
```

Run a one-shot project task:

```bash
dotcraft exec "Review this repository and identify the three highest-risk changes."
```

See [Getting Started](https://www.dotcraft.net/getting-started) for the complete setup guide.


## Contributing

We welcome code, documentation, and integration contributions. Start with [CONTRIBUTING.md](./CONTRIBUTING.md).

## Credits

Inspired by [nanobot](https://github.com/HKUDS/nanobot), [codex](https://github.com/openai/codex) and [agent-framework](https://github.com/microsoft/agent-framework).

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
