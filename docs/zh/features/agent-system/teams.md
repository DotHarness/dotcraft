# Teams

交给 DotCraft 一个复杂请求，它会派一支小队来做。Team Leader 把请求拆成一块任务板，并行分派给一支固定的专家小队——Explorer、Builder、Reviewer、Operator——再把各自的结果汇总成一个完成的答复。你只提一个需求，拿回的是做完的 Mission，而不是一堆要自己盯的子任务。

Teams 通过内置插件 `agent-teams` 提供。在插件目录启用后，Desktop 会出现 Team 入口；Team 面板是创建 Mission、查看团队状态的主入口。

![DotCraft Teams](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/teams.gif)

> [!NOTE]
> Teams 是构建在 Session Core 上的 Managed App Binding 运行时。每位队友的 Mission 线程都是一条普通的 DotCraft 顶层 Thread，拥有独立的上下文、工具、历史与审计。

## 何时使用 Teams

| 场景 | 推荐 |
|---|---|
| 在已有对话中一次性委派独立任务 | [SubAgents](./subagents) |
| 用户请求需要规划、并行执行并产出统一答复 | Teams |
| 按计划或手动触发的工作 | [Automations](./automations) |
| 持续推进一条 Thread 朝长期目标演进 | [Goals](./automations#goals) |

如果工作能舒服地塞进一条 Thread，Teams 是过度设计；如果工作天然按角色拆分、有显式依赖，或需要 Leader 在最后做综合，那 Teams 是合适的选择。

## 默认成员

团队成员固定如下：

| 成员 | 角色 |
|---|---|
| **Team Leader** | 拆解 Mission、分派任务、协调团队、撰写最终面向用户的答复。 |
| **Explorer** | 调研、勘察、梳理未知项。 |
| **Builder** | 实施变更、产出工件。 |
| **Reviewer** | 检查质量、风险与正确性。 |
| **Operator** | 处理 App / 计算机相关的操作型任务。 |

按角色自定义（Skills、MCP server、插件、提示词、权限模式、工具表面）在路线图中。动态创建团队 / 成员不在本里程碑范围内。

## Mission 生命周期

Mission 是用户视角的交付单元，包含一块任务板、邮箱事件与最终回复。

| 状态 | 含义 |
|---|---|
| `planning` | Mission 已创建；Leader 输入已入队或正在运行。 |
| `active` | Leader 已记录计划或派发至少一个任务。 |
| `awaitingLeaderReview` | 所有必需任务和审核门完成；Leader 需要写入 `finalResponse`。 |
| `done` | Leader 已通过 `finalResponse` 完成 Mission。 |
| `cancelled` | 用户取消 Mission；未完成的任务一并取消。 |

只有终止状态（`done` / `cancelled`）的 Mission 可以归档。归档保留状态，不会删除 Mission 或其队友线程。

> [!TIP]
> Desktop 里取消和归档都是卡片动作：把 Mission 卡片拖入"丢弃堆"并确认。丢弃堆不是可点击按钮——拖拽本身就是有意的确认动作。

## 任务板

任务（`TeamTask`）构成 Mission 范围内的共享任务板，每条任务都有显式的执行人、状态、依赖、阻塞、输出摘要与元数据。

| 状态 | 含义 |
|---|---|
| `pending` | 已创建，等待调度评估。 |
| `waitingDependencies` | 受上游任务阻塞。 |
| `ready` | 可以执行，但执行人线程尚未就绪。 |
| `running` | 已入队或正在执行人 Mission 线程中运行。 |
| `blocked` | 执行人报告阻塞，需要其他动作。 |
| `review` | 工作完成；尚未通过审核门。 |
| `done` | 已被 Mission 完成所采纳。 |
| `failed` | 没有 Mission 级介入无法继续。 |
| `cancelled` | 随 Mission 取消或被显式取消。 |

任务之间用 Mission 内短别名（`t1`、`t2`）引用，工件同理（`a1`、`a2`）；规范 id（`task_...`、`artifact_...`）仍然存储，用于兼容。

### 依赖与 Leader 综合

- Leader 在派发任务时用 `dependsOnTaskIds` 记录显式依赖。
- 当下游任务需要 Leader 解读上游结果再派发时，可以把任务标记为 `requiresLeaderSynthesis`——调度器在上游完成后唤醒 Leader，并在 Leader 给出综合指令前不向队友派发。
- 审核是任务属性（`kind = "review"`），不是硬编码的角色规则。任何成员都可以被指派为审核执行人。

## 协作回路

队友之间通过以下机制协作：

- **邮箱消息**（`SendMessage`）—— 轻量的 Mission 内事件，可发给某位成员或 Leader。它们不是任何线程里的 Turn；调度器会将它们合批，并在消息可执行时唤醒接收方。
- **工件**（`PublishArtifact`）—— 显式的交接记录，包含标题、路径或 URI 与摘要。发布工件不会完成任务；队友仍需调用 `MarkTaskDone` 或 `ReportProgress(status: "blocked")`。
- **进度**（`ReportProgress`）—— 中途进度或阻塞更新，不是完成信号。

Leader 不轮询。派发工作或发送消息后，Leader 结束当前回合；调度器会在任务结果、阻塞、队友消息、综合需求或最终审核需要时唤醒它。

## Desktop UI

Team 面板是一块"卡牌协作桌"：

- 机器人队友卡、Mission 卡、Task 卡，状态都从 Teams 数据实时拉取。
- 右侧详情栏展示当前选中的队友、Mission 或 Task。
- 桌面上提供主 Mission 起草工作流，用于创建新 Mission。
- 当上下文充分时，链接可直接打开真实的 Mission 队友线程；插件启用后，Mission 队友线程也会出现在常规会话列表中。

状态徽章会展示调度相关状态（`waitingDependencies`、`blocked`、`review`、`awaitingLeaderReview`、`done`），让卡上就能诊断停滞的 Mission。

## 配置

Teams 由内置插件 `agent-teams` 控制。状态持久化在：

```text
.craft/teams/state.json
.craft/teams/missions/{missionId}/
```

Mission 的 scratchpad / 工件工作区路径会以 App Context Block 形式挂入每位队友的 Mission 线程，告诉队友把可复用的交接物放在哪里。

运行时字段在 `teams` 配置段中。可用键见 [配置完整参考](../../developing/configuration)。

## 相关文档

- [SubAgents](./subagents) — Teams 显得过重时的单级委派。
- [Automations 与 Goals](./automations) — 计划驱动的任务与 Thread 级目标，可与 Teams Mission 组合使用。
- [统一会话核心](../../developing/architecture/session-core) — 支撑 Mission 队友线程的 Thread / Turn / Item 模型。
