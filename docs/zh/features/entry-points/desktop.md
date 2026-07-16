# Desktop

Desktop 是上手 DotCraft 最省事的方式。它把一切都放在一个窗口里——工作区、会话、Diff、计划、模型配置、自动化审核和运行状态——让你用图形界面驱动 Agent，而不用敲命令行。（它底层是个 AppServer 客户端，和其他入口共用同一个工作区。）

第一次使用先按 [快速开始](../../getting-started) 完成下载、选工作区和配模型；本页只讲 Desktop 自己的特有面板与设置。

## 安装

### 直接使用 Release

1. 从 [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) 下载安装包。
2. 启动 DotCraft。
3. 选择项目目录作为工作区。

### 从源码运行

```bash
cd desktop
npm install
npm run dev
```

从源码运行时，应用会在 `PATH` 中查找 `dotcraft`。如果找不到，请在设置中指定 AppServer / `dotcraft` 二进制路径。打包安装包可运行 `npm run dist`，产物位于 `desktop/dist/`。

## 启动参数

```bash
DotCraft --app-server /path/to/dotcraft
DotCraft --workspace /path/to/project
```

## Desktop 特有设置

| 配置入口 | 说明 |
|---|---|
| **Settings → Profile** | 当前工作区的 Token 活动热力图、累计/峰值/连续天数统计，以及可选的 GitHub 身份 |
| **Settings → General** | 当前 Workspace 路径、AppServer binary 路径、语言 |
| **Settings → Personalization** | 长期记忆与 Dreams 的开关、立即运行、自动更新、重置记忆 |
| **Settings → Model Providers** | 个人 provider、凭证、Endpoint，以及各 provider 的 MainAgent/SubAgent 模型 |
| **Settings → Sub Agents** | 复用外部 CLI 会话（详见 [SubAgents](../agent-system/subagents)） |
| **Settings → Connection** | 本地 Hub vs 远程 AppServer 切换 |

### Profile

- **Token 活动** — 类似 GitHub 贡献图的热力图，展示当前工作区所有会话的每日 Token 用量；可在**每日**、**每周**、**累计**三种着色方式间切换。
- **统计** — 累计 Token、单日峰值、最长任务（最长的单次 Agent turn）、当前连续天数与最长连续天数。
- **身份（可选）** — 关联一个 GitHub 用户名即可在头部显示其公开头像与 handle；未关联时显示首字母头像。检测到已登录的 ChatGPT provider 时，会以徽章显示其套餐（如 Pro）。
- 需要为该工作区启用 tracing，否则活动视图不可用。

### Personalization → Dreams

- **立即运行** — 强制触发一次后台 Dreams 整理。
- **自动更新梦境** — 关闭：新 Dreams 仅作为 pending；开启：未来成功运行自动应用为 active Dream store。
- **管理梦境** — 列出最近运行记录，每条记录可打开 Dashboard 完成 diff、trace、应用、丢弃、取消、归档。
- **重置记忆** — 一次性清空 `MEMORY.md`、`HISTORY.md`、`.craft/dreams/` 与派生缓存；不会删除会话、配置、技能或自动化任务。

详见 [长期记忆与 Dreams](../agent-system/memory)。

### Model Providers

- Provider 凭据与 endpoint 写入个人 `~/.craft/config.json`，**不**写入工作区。
- 工作区保存 `ProviderId`、`Model`、`ProviderModels` 与 `SubAgent.ProviderModels`，共享配置仍不会包含密钥。
- Welcome picker 只设置未来线程的默认值；已有线程保留创建时的 provider/model，也可以在自己的 composer 中独立切换。
- 原生 SubAgent 使用父线程 provider 对应的 SubAgent 模型偏好；没有对应项时继承父线程 MainAgent 模型。
- Desktop 当前支持 OpenAI 与 Anthropic provider。
- 用 **Test** 检查凭据和模型列表可达性；如果 provider 不支持列模型，仍可保存并手动输入模型名。

### Connection（Local vs Remote）

- **Local（默认）**：Desktop 通过 Hub 自动启动或发现工作区 AppServer，多入口共享同一个进程。
- **Remote**：连接已有 WebSocket AppServer。Desktop 不重启远端进程，只测试连接 + 切换。
- 远端 URL/token 切换前会先做草稿连接探测，失败时不会保存，避免下次启动卡在坏配置上。
- 通过 `--remote` 启动时，Settings 中的持久化连接切换不可用。

## What's New

Desktop 升级后，会在进入可用工作区主界面时显示一次 **What's New**，介绍当前版本的新能力。动图预览会从 DotHarness resources 仓库下载、校验并缓存在本机；自动弹窗会等预览准备好再出现，手动打开则会先显示文字和占位预览。也可以随时通过 **Help → What's New** 或侧边栏底部的版本号重新打开。最新版本默认展开，历史版本会折叠在 **历史亮点** 按钮后，需要查阅时再展开即可。

## 更新

启动后，DotCraft 会从 [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) 检查是否存在更新的 release tag。如果当前平台有可用安装包，标题栏会出现高亮下载按钮。点击后可查看 release 信息、下载安装包并看到进度；下载完成后 DotCraft 会退出并打开下载好的安装包。

## 使用示例

| 场景 | Desktop 中的路径 |
|---|---|
| 第一次使用 | 选择工作区 → 配置模型 → 新建会话 |
| 查看 Agent 做了什么 | 打开会话详情、Diff、Trace 或 Dashboard |
| 审核自动化任务 | 打开 Automations 面板，查看待审核任务 |
| 切换项目 | 选择另一个 workspace，让配置和任务跟随项目隔离 |
| 收回 SubAgent 控制权 | 打开 Settings → Sub Agents，关闭复用外部 CLI 会话 |

## 进阶

- Desktop 是一个 AppServer 客户端，与 ACP、外部渠道共享同一个 [会话核心](../../developing/architecture/session-core)——在这里开的线程，可以在其他 AppServer 客户端中继续。
- 图片附件在重启后仍然保留；重新打开会话，缩略图依旧在。
- Markdown 内容区会把标记为 `mermaid` / `mmd` 的 fenced code block 渲染为 Mermaid 图。图表无法渲染时，Desktop 会回退显示源码块。

## 相关文档

- [快速开始](../../getting-started)
- [入口总览](./)
- [可观测性](../self-hosted/observability)
- [设置生效层级](../../developing/lifecycle/settings-lifecycle)
