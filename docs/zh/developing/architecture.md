# 架构总览

DotCraft 是 .NET 10 / C# 编写的 Agent Harness。模块化的设计让 CLI、编辑器、机器人、自动化和 GitHub 工作流共享同一个工作区，并复用同一份会话、记忆、技能与工具。本页面向**集成方与贡献者**：解释代码层面的边界，便于扩展与排查。

## 顶层模块

![DotCraft runtime architecture topology](/runtime-architecture-topology.svg)

## 模块类型与发现机制

所有交互模式都实现 `IDotCraftModule`，由 `DotCraft.Gen` 源生成器自动发现。模块分三种：

| 类型 | 说明 | 示例 |
|---|---|---|
| **Host** | 独立入口，可作为进程主体 | CLI（`DotCraft.App`，priority `0`） |
| **Channel** | 由 Gateway 托管 | ACP、Automations、QQ / WeCom / Feishu / Telegram / WeChat |
| **Tool-only** | 仅提供工具，不构成入口 | 例如某些扩展工具集 |

> [!NOTE]
> **Host 优先级**：当多个 Host 同时启用时，优先级低的 Host 接管进程入口；都未启用时 Gateway 接管，加载启用的 Channels。

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
- **客户端**：TUI、Desktop、ACP、外部渠道适配器、SDK（TypeScript / .NET / Python）
- **认证**：WebSocket `?token=` 查询参数（[完整说明](./appserver.md)）

详见 [AppServer 协议](./appserver-protocol.md) 与 [AppServer 模式](./appserver.md)。

## Hub

每个用户在本机有一个 [Hub](./hub.md)，按需启动/复用每个工作区的 AppServer，并在 `~/.craft/hub/` 维护发现信息和锁。Desktop / TUI 默认通过 Hub 工作。手动管理 AppServer（远程、CI、机器人、协议调试）时绕过 Hub，使用 [AppServer 模式](./appserver.md)。

## Agents

`DotCraft.Core.Agents` 基于 `Microsoft.Extensions.AI` 构建：

- Agent 工厂与运行器
- 工具注入与过滤（`EnabledTools`、SubAgent role allow/deny list、Hooks 拦截）
- SubAgents：`native` 与 `cli-oneshot` 两种 runtime（详见 [SubAgents](../features/subagents.md)）
- 上下文压缩管线（自动 + 反应式 + microcompaction，由 `Compaction.*` 配置）

## 配置体系

DotCraft 用两层配置叠加：

| 层级 | 路径 | 作用 |
|---|---|---|
| 全局 | `~/.craft/config.json` | Provider 凭据、Endpoint、个人偏好 |
| 工作区 | `<workspace>/.craft/config.json` | 模型选择、入口开关、自动化、安全策略 |

模块通过 `[ConfigSection("Key")]` 在自己的 assembly 中声明配置节，由源生成器收集；新增模块时配置自动合并到合并 schema，无需手工注册。

字段配置完整参考：[配置参考](./configuration.md)；字段如何生效（即时 / 子系统重启 / AppServer 重启）：[设置生效层级](./settings-lifecycle.md)。

## 相关入口

- [配置参考](./configuration.md)
- [AppServer 协议](./appserver-protocol.md) / [AppServer 模式](./appserver.md)
- [Hub 协议](./hub-protocol.md) / [Hub 本地协调](./hub.md)
- [SDK 总览](./sdk.md) · [TypeScript SDK](./sdk-typescript.md) · [.NET SDK](./sdk-dotnet.md) · [Python SDK](./sdk-python.md)
