# 项目级工作区

在 DotCraft 里，工作区就是你的项目文件夹。Agent 在这里产生的一切——会话、长期记忆、技能、自动化、插件和模型选择——都存在项目根目录下的一个 `.craft/` 文件夹里。它跟着项目走：可以和代码一起提交、备份、同步到另一台电脑，或者分享给同事，对方打开后用的是和你同一个 Agent。

## 关键概念

| 概念 | 含义 |
|---|---|
| **Workspace** | 含 `.craft/` 子目录的项目根。Desktop、TUI、ACP、Bot、Automations 都围绕同一个 workspace 工作。 |
| **Bootstrap files** | `.craft/AGENTS.md`、`.craft/SOUL.md`、`.craft/USER.md`——分别约束 Agent 的行为规则、个性与表达、用户画像。 |
| **Memory & History** | `.craft/MEMORY.md`、`.craft/HISTORY.md`——长期记忆与历史记录，由 Agent 维护、用户可读。 |
| **Skills / Plugins** | `.craft/skills/` 与 `.craft/plugins/`——工作区内自带的能力包。 |
| **Sessions** | `.craft/sessions/` 与 `.craft/threads/`——所有入口共享的会话存档。 |
| **Config** | `.craft/config.json`——工作区级配置，叠加在全局 `~/.craft/config.json` 之上。 |

## 为什么这样组织

![DotCraft workspace topology](/workspace-topology.svg)

任何入口接入工作区时，**先读这一份目录，后做事**。这样：

- 切换入口不丢上下文：在 Desktop 开启的会话可以在 TUI 继续。
- 跨设备恢复零成本：把整个项目目录同步到另一台机器即可。
- 团队协作有共同基线：把 `.craft/` 的可分享部分（skills、commands、AGENTS.md 等）提交到仓库，团队成员一开 Desktop 就拿到同一份 Agent 行为约束。

## 全局 vs 工作区配置

DotCraft 用两层配置叠加：

| 层级 | 路径 | 用途 |
|---|---|---|
| 全局 | `~/.craft/config.json` | Provider 凭据、Endpoint、个人偏好——不进仓库 |
| 工作区 | `<workspace>/.craft/config.json` | 模型选择、入口开关、自动化、安全策略——可进仓库 |

如果你不确定字段放哪：**Provider 放全局，工作区只覆盖项目相关选择**。Provider 字段、入口开关、Memory、Skills、Automations 和安全策略都统一放在 [配置完整参考](../developing/configuration)。

## Bootstrap 三件套

`.craft/` 下面这三个 Markdown 决定了"这个项目里的 Agent 是谁、要遵守什么":

- `AGENTS.md` — 角色职责、行为边界、回答规则
- `SOUL.md` — 个性、语气、表达风格
- `USER.md` — 用户画像、受众背景、沟通偏好

这些文件是普通 Markdown，没有特殊语法，DotCraft 会在每个会话启动时把它们注入系统提示词。

## 第一次进入

| 步骤 | 命令或动作 |
|---|---|
| 用 Desktop 打开 | 启动 Desktop → 选择项目目录 → 按引导初始化 |
| 用终端初始化 | `cd <project>` → `dotcraft setup` 按提示填写 |
| 验证 | 在 Desktop 新建会话，问 Agent："读 README 和 docs/index.md，告诉我项目怎么启动" |

完整图文流程见 [快速开始](../getting-started)。

## 相关文档

- [快速开始](../getting-started) — 第一次安装、配置、运行
- [外部 Agent 协作](../developing/workflow/workspace-handoff) — 把 workspace 中的会话、记忆和 trace 整理成交接材料
- [长期记忆与 Dreams](./agent-system/memory) — `MEMORY.md` / `HISTORY.md` 怎么自动维护
- [统一会话核心](../developing/architecture/session-core) — 跨入口共享的 Thread/Turn/Item 模型
- [安全与沙箱](./self-hosted/security) — 工作区边界、黑名单、OpenSandbox
- [配置完整参考](../developing/configuration) — 所有字段
