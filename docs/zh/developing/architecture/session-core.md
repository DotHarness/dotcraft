# 统一会话核心

DotCraft 不为每个客户端维护一套独立的 Agent 流程。**Unified Session Core** 是把执行、状态、审批、可观测性收敛到一个引擎上的设计——CLI、Desktop、ACP、QQ 机器人、自动化任务都连同一个核心。

本页面向集成方与贡献者，说明会话模型，以及在你构建客户端或排查共享会话时需要关注的跨入口边界。

## 模型：Thread → Turn → Item

![DotCraft session core topology](/session-core-topology.svg)

| 实体 | 含义 |
|---|---|
| **Thread** | 一段长期会话。跨入口共享，可恢复、可订阅、可审计。 |
| **Turn** | 一轮"用户输入 → Agent 工作 → 用户可见结果"的逻辑单元。 |
| **Item** | Turn 内的最小事件：用户消息、Agent 消息、工具调用、工具结果、思考、审批等。 |

`ISessionService` 是核心 API：负责 Thread 生命周期、输入提交、流式事件订阅、审批流。所有入口（CLI、ACP、Automations、外部渠道适配器）都通过这一个接口跟引擎对话。

## 跨入口共享是怎么发生的

![DotCraft cross-entry session sharing topology](/session-sharing-topology.svg)

要点：

- **Hub** 在本机为每个工作区启动一个 AppServer。Desktop 和 CLI 默认走这条路，所以同一个项目目录无论从哪个入口打开，都连到同一个进程。
- **AppServer** 把 ISessionService 投影成 JSON-RPC（[完整协议](../protocols/appserver-protocol)），任何语言都可以做客户端。
- **Workspace `.craft/`** 会把权威线程记录保存在 `threads/`，把分类状态和投影保存在 `state.db`，并独立保存大型工具结果、附件等引用文件。存储权威和恢复模型见[会话持久化](./session-persistence)。

## 审批与人在环路

Session Core 把"工具调用是否允许执行"独立成审批事件，让前端可以把它转译成自己的 UI：

| 入口 | 审批呈现方式 |
|---|---|
| Desktop | 模态框 / Approvals 面板 |
| ACP（IDE） | `requestPermission` 转给编辑器 UI |
| QQ / WeCom 等渠道 | 平台原生消息回复 |

> [!NOTE]
> 同一个 Thread 在不同入口接管时，审批 UI 会用对应平台原生形式呈现——Desktop 不会把 QQ 群消息硬塞进自己的弹窗。

## 跨渠道恢复

| 维度 | 不带统一会话核心 | DotCraft Unified Session Core |
|---|---|---|
| 客户端定制 | 消息总线丢失语义，难以定制 | 各客户端按需要展示 thinking、diff、approval、tool call |
| 审批 / HITL | 无法表达平台原生交互 | 每个平台用自己的 UI |
| 跨渠道恢复 | 不支持 | 同一 Thread 可被任意已连入口接管 |
| 工作区持久化 | 不支持 | `.craft/` 内自然存档 |

典型场景：

- 早上在 Desktop 让 Agent 起草 PR，下班在地铁里用 ACP 移动客户端继续 review，仍然使用同一个 Thread。
- 自动化任务在 Cron 里跑出一半遇到审批，Desktop 收到通知接力批准或修改。
- 微信收到用户提问，机器人回复后，研发人员可在 Desktop 的同一 Thread 看历史并接手。

## Hub 与 AppServer 的分工

DotCraft 在本机有两层协调：

- **Hub** —— 用户级协调器，按需启动/复用每个工作区的 AppServer，避免一个工作区被多个 AppServer 同时占用。日常 Desktop 与 CLI 用户不用关心。
- **AppServer** —— 工作区级运行时，所有 Thread / 工具 / 审批 / 事件流的真实承担者。

需要直接控制 AppServer（远程部署、CI、机器人、调试）时，参考：

- [AppServer 模式](../lifecycle/appserver) — 手动启动、`--listen`、`--token`、远程连接
- [Hub 协议](../protocols/hub-protocol) — 实现 Hub 客户端
- [AppServer 协议](../protocols/appserver-protocol) — 实现 AppServer 客户端

## 何时关心 Session Core

| 角色 | 需要关心吗 |
|---|---|
| 普通 Desktop 用户 | 不需要 |
| 想多客户端共享同一工作区 | 阅读 [AppServer 模式](../lifecycle/appserver) |
| 想构建机器人或外部渠道 | 阅读 [Python SDK](../sdks/python) 或 [TypeScript SDK](../sdks/typescript) |
| 想接入新 IDE / 编辑器 | 阅读 [ACP 模式](../../features/entry-points/editors) |
| 想自定义协议层客户端 | 阅读 [AppServer 协议](../protocols/appserver-protocol) |

## 相关文档

- [会话持久化](./session-persistence)
- [入口总览](../../features/entry-points/)
- [可观测性](../../features/self-hosted/observability)
- [AppServer 模式](../lifecycle/appserver)
- [AppServer 协议](../protocols/appserver-protocol)
