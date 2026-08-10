# 架构总览

DotCraft 是 .NET 10 / C# 编写的 Agent Harness。模块化的设计让 CLI、编辑器、机器人、自动化和 GitHub 工作流共享同一个工作区，并复用同一份会话、记忆、技能与工具。本页面向**集成方与贡献者**：解释代码层面的边界，便于扩展与排查。

## 顶层模块

![DotCraft runtime architecture topology](/runtime-architecture-topology.svg)

## 模块类型与发现机制

所有交互模式都实现 `IDotCraftModule`，由 `DotCraft.Generators` 源生成器自动发现。模块分三种：

| 类型 | 说明 | 示例 |
|---|---|---|
| **Host** | 独立入口，可作为进程主体 | CLI、AppServer、Hub、ACP |
| **Channel** | 由 AppServer 托管 | QQ / WeCom / Feishu / Telegram / WeChat 适配器 |
| **Tool-only** | 仅提供工具，不构成入口 | 例如某些扩展工具集 |

> [!NOTE]
> AppServer 负责 Automations、Heartbeat、Cron、Dashboard 和外部渠道等长期运行的工作区服务。

## Session Core

Session Core 定义 `Thread → Turn → Item` 模型，并以 `ISessionService` 作为中央 API，覆盖：

- 线程生命周期（`thread/start`、`thread/resume`、`thread/list`、`thread/read`、`thread/archive`、`thread/delete`、`thread/pause`、`thread/setMode`）
- 输入提交（`turn/start`、`turn/interrupt`）
- 流式事件订阅（`item/agentMessage/delta`、`turn/completed` 等）
- 审批流（`item/approval/request` ↔ JSON-RPC 响应）

| 入口 | 是否使用 ISessionService |
|---|---|
| CLI、ACP、Automations、外部渠道适配器 | 是（持久化 Thread + 跨入口共享） |

## AppServer

AppServer 将 `ISessionService` 暴露为面向外部客户端的 JSON-RPC 2.0：

- **传输**：stdio（JSONL 一行一消息）和 WebSocket（每帧一条消息）
- **客户端**：Desktop、CLI、ACP、外部渠道适配器、SDK（TypeScript / .NET / Python）
- **认证**：WebSocket `?token=` 查询参数（[完整说明](../lifecycle/appserver)）

详见 [AppServer 协议](../protocols/appserver-protocol) 与 [AppServer 模式](../lifecycle/appserver)。

## Hub

每个用户在本机有一个 [Hub](../lifecycle/hub)，按需启动/复用每个工作区的 AppServer，并在 `~/.craft/hub/` 维护发现信息和锁。Desktop 与 CLI 默认通过 Hub 工作。手动管理 AppServer（远程、CI、机器人、协议调试）时绕过 Hub，使用 [AppServer 模式](../lifecycle/appserver)。

## Agents

Agent runtime 分为 provider-neutral 基础层、Session Core 与 provider integration：

- `DotCraft.Agents`：agent facade、provider registry/契约、公共 middleware、工具循环与 prompt-cache 选择
- `DotCraft.Core`：Thread/Turn 生命周期、工具策略、SubAgents、compaction、持久化与 AppServer 投影
- `DotCraft.Agents.OpenAI` / `DotCraft.Agents.Anthropic`：SDK client、wire mapping、认证/目录能力及原生 history/cache 行为
- `DotCraft.App`：显式注册两个内建 provider integration 的组合根

`native` 与 `cli-oneshot` runtime 详见 [SubAgents](../../features/agent-system/subagents)。

## 配置体系

DotCraft 用两层配置叠加：

| 层级 | 路径 | 作用 |
|---|---|---|
| 全局 | `~/.craft/config.json` | Provider 凭据、Endpoint、个人偏好 |
| 工作区 | `<workspace>/.craft/config.json` | 模型选择、入口开关、自动化、安全策略 |

模块通过 `[ConfigSection("Key")]` 在自己的 assembly 中声明配置节，由源生成器收集；新增模块时配置自动合并到合并 schema，无需手工注册。

字段配置完整参考：[配置参考](../configuration)；字段如何生效（即时 / 子系统重启 / AppServer 重启）：[设置生效层级](../lifecycle/settings-lifecycle)。

## 相关入口

- [配置参考](../configuration)
- [AppServer 协议](../protocols/appserver-protocol) / [AppServer 模式](../lifecycle/appserver)
- [Hub 协议](../protocols/hub-protocol) / [Hub 本地协调](../lifecycle/hub)
- [SDK 总览](../sdks/) · [TypeScript SDK](../sdks/typescript) · [.NET SDK](../sdks/dotnet) · [Python SDK](../sdks/python)
