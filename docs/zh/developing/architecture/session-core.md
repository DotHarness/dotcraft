# 统一会话核心

DotCraft 不给每个客户端配一套独立的 Agent 流程。**Unified Session Core** 把执行、状态、审批和[可观测性](../../features/self-hosted/observability)收敛到同一个引擎上，CLI、Desktop、ACP、QQ 机器人和自动化任务连的都是它。

本页面向集成方与贡献者，说明会话模型，以及构建客户端或排查共享会话时需要关注的跨入口边界。

![DotCraft 会话核心拓扑图](/session-core-topology.svg)

## 模型：Thread → Turn → Item

| 实体 | 含义 |
|---|---|
| **Thread** | 一段长期会话。跨入口共享，可恢复、可订阅、可审计。 |
| **Turn** | 一轮"用户输入 → Agent 工作 → 用户可见结果"的逻辑单元。 |
| **Item** | Turn 内的最小事件：用户消息、Agent 消息、工具调用、工具结果、思考、审批等。 |

`ISessionService` 是核心 API，负责 Thread 生命周期、输入提交、流式事件订阅和审批流。所有入口（CLI、ACP、Automations、外部渠道适配器）都通过这一个接口跟引擎对话。

## 跨入口共享是怎么发生的

![DotCraft 跨入口会话共享拓扑图](/session-sharing-topology.svg)

要点：

- **Hub** 在本机为每个工作区启动或复用一个 AppServer。Desktop 和 CLI 默认走这条路，所以同一个项目目录无论从哪个入口打开，都连到同一个进程。
- **AppServer** 把 `ISessionService` 投影成 JSON-RPC（[完整协议](../protocols/appserver-protocol)），任何语言都可以实现客户端。
- **工作区 `.craft/`** 把权威线程记录保存在 `threads/`，把分类状态和查询投影保存在 `state.db`，大型工具结果、附件等配套文件单独存放。存储权威和恢复模型见[会话持久化](./session-persistence)。

## 审批与人在环路

Session Core 把"这次工具调用是否放行"独立成审批事件，前端可以按自己的形态渲染：

| 入口 | 审批呈现方式 |
|---|---|
| Desktop | 模态框 / Approvals 面板 |
| ACP（IDE） | 转成 `requestPermission` 交给编辑器 UI |
| QQ / WeCom 等渠道 | 平台原生消息回复 |

> [!NOTE]
> 同一个 Thread 换一个入口接管时，审批 UI 用的是当前平台的原生形式——Desktop 不会把 QQ 群消息硬塞进自己的弹窗。

## 跨渠道恢复

Session Core 发给适配器的是结构化事件流，不是渲染好的文本。思考内容、工具调用、工具结果、审批各自是独立的 Item 类型，客户端按自己的形态渲染，语义不会在传输途中损失。Thread 本身存活在工作区的 `.craft/` 里，任何已连入的入口都可以接管，接管后的每个 Turn 都记录了发起它的渠道。

典型场景：

- 早上在 Desktop 让 Agent 起草 PR，下班在地铁里用 ACP 移动客户端继续 review，仍然是同一个 Thread。
- 自动化任务在 Cron 里跑到一半遇到审批，Desktop 收到通知，你接力批准或修改。
- 微信收到用户提问，机器人回复后，研发人员在 Desktop 的同一个 Thread 里看历史并接手。

## Hub 与 AppServer 的分工

DotCraft 在本机有两层协调：

- **Hub** —— 用户级协调器，按需启动或复用每个工作区的 AppServer，避免一个工作区被多个 AppServer 同时占用。日常 Desktop 与 CLI 用户不用关心。
- **AppServer** —— 工作区级运行时，所有 Thread、工具、审批、事件流的真实承担者。

需要直接控制 AppServer（远程部署、CI、机器人、调试）时，参考：

- [AppServer 模式](../lifecycle/appserver) — 手动启动、`--listen`、`--token`、远程连接
- [Hub 协议](../protocols/hub-protocol) — 实现 Hub 客户端
- [AppServer 协议](../protocols/appserver-protocol) — 实现 AppServer 客户端

## 何时关心 Session Core

| 场景 | 从哪里开始 |
|---|---|
| 多个客户端共享同一个工作区 | [AppServer 模式](../lifecycle/appserver) |
| 构建机器人或外部渠道 | [TypeScript SDK](../sdks/typescript) |
| 接入新的 IDE 或编辑器 | [IDE / 编辑器（ACP）](../../features/entry-points/editors) |
| 自己实现协议层客户端 | [AppServer 协议](../protocols/appserver-protocol) |
