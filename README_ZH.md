<div align="center">

[![Release](https://img.shields.io/github/v/release/DotHarness/dotcraft)](https://github.com/DotHarness/dotcraft/releases)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](./LICENSE)
[![Platforms](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)](https://github.com/DotHarness/dotcraft/releases)
[![Discussions](https://img.shields.io/badge/community-Discussions-brightgreen)](https://github.com/DotHarness/dotcraft/discussions)

![DotCraft — 面向真实项目的 Agent Runtime](https://github.com/DotHarness/resources/raw/master/dotcraft/intro.png)

[English](./README.md) · [官方文档](https://www.dotcraft.net/zh/) · [快速开始](https://www.dotcraft.net/zh/getting-started) · [下载 Release](https://github.com/DotHarness/dotcraft/releases) · [License](./LICENSE)

DotCraft 是一个基于 C#/.NET 构建的开源、自托管**项目原生 AI Agent Runtime**。
</div>

---

## 为什么选择 DotCraft？

DotCraft 将项目转变为 **AI Agent 的可扩展运行环境**。

- **现代 Agent 能力开箱即用：** 原生的 Plan、SubAgents、Agent Teams、Automations、Goal 等 Agent 能力，开箱即用。
- **项目走到哪，工作就跟到哪：** 会话、记忆、Agent、Skills 和 Plugins 随项目迁移，换个入口也能继续。
- **更少的 Token 消耗：** DotCraft 会最大化利用前缀缓存减少开销，SubAgent 也能延续父会话已有的缓存。
- **部署和模型都由你决定：** 可在本地或自己的服务器运行，并自由选择兼容的模型服务。
- **轻松接入现有产品：** API、SDK、App Binding、Plugins 和 Desktop Extensions，让 DotCraft 直接集成到现有应用中。

![desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/multi-workspace.gif)

## 快速开始

### Desktop

1. 从 [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) 下载最新版本。
2. 选择一个真实项目目录作为工作区。
3. 配置支持的 Provider，或使用受支持的 ChatGPT 计划登录。
4. 从一个具体问题开始：

```text
梳理这个仓库的架构，解释主要执行路径，并告诉新贡献者应该从哪里开始。
```

### CLI

macOS / Linux：

```bash
curl -fsSL https://www.dotcraft.net/install.sh | bash
```

Windows PowerShell：

```powershell
irm https://www.dotcraft.net/install.ps1 | iex
```

运行一次性项目任务：

```bash
dotcraft exec "检查这个仓库，并指出风险最高的三个改动。"
```

完整的安装说明请查看[快速开始](https://www.dotcraft.net/zh/getting-started)。


## 贡献代码

欢迎提交代码、文档与集成相关贡献。开始前请阅读 [CONTRIBUTING.md](./CONTRIBUTING.md)。

## 致谢

本项目受 [nanobot](https://github.com/HKUDS/nanobot) 、 [codex](https://github.com/openai/codex) 与 [agent-framework](https://github.com/microsoft/agent-framework) 启发。

特别感谢：

- [HKUDS/nanobot](https://github.com/HKUDS/nanobot)
- [openai/codex](https://github.com/openai/codex)
- [microsoft/agent-framework](https://github.com/microsoft/agent-framework)
- [alibaba/OpenSandbox](https://github.com/alibaba/OpenSandbox)
- [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk)
- [openai/symphony](https://github.com/openai/symphony)

# 文章

dotcraft 相关技术博客.

[为什么你的 Agent 这么贵：Prompt Cache 命中率为 0 的排查记录](https://zhuanlan.zhihu.com/p/2044201072466588522)

## License

Apache License 2.0
