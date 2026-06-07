# Teams

交给 DotCraft 一个复杂请求，它会派一支小队来做。Team Leader 把请求拆成一块任务板，并行分派给一支固定的专家小队——Explorer、Builder、Reviewer、Operator——再把各自的结果汇总成一个完成的答复。你只提一个需求，拿回的是做完的 Mission，而不是一堆要自己盯的子任务。

Teams 通过内置插件 `agent-teams` 提供。在插件目录启用后，Desktop 会出现 Team 入口；Team 面板是创建 Mission、查看团队状态的主入口。

![DotCraft Teams](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/teams.gif)

> [!NOTE]
> 每位队友都在自己的对话里工作，拥有独立的上下文、工具、历史与审计记录——你可以打开任意队友的线程，完整跟踪它做了什么。

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

成员是固定的：你不需要创建或移除成员。每位成员已经带着适配自己那部分工作的专注角色和工具集。

## Mission 生命周期

Mission 是用户视角的交付单元，包含一块任务板、邮箱事件与最终回复。

| 状态 | 含义 |
|---|---|
| `planning` | Mission 已创建；Leader 正在制定计划。 |
| `active` | Leader 已有计划，并派发了至少一个任务。 |
| `awaitingLeaderReview` | 所有任务和审核都已完成；Leader 正在撰写最终答复。 |
| `done` | Leader 已交付最终答复。 |
| `cancelled` | 你取消了 Mission；其未完成的任务也一并取消。 |

只有已结束（`done` / `cancelled`）的 Mission 可以归档。归档保留记录，不会删除 Mission 或其队友线程。

> [!TIP]
> Desktop 里取消和归档都是卡片动作：把 Mission 卡片拖入"丢弃堆"并确认。丢弃堆不是可点击按钮——拖拽本身就是有意的确认动作。

## 任务板

每个 Mission 都有一块共享任务板。板上每条任务都带着执行人、状态、依赖、阻塞和输出摘要，让你一眼就能看清谁在做什么、卡在哪里。

| 状态 | 含义 |
|---|---|
| `pending` | 已创建，等待调度。 |
| `waitingDependencies` | 受上游任务阻塞。 |
| `ready` | 已具备执行条件，但执行人还没空出来。 |
| `running` | 正在执行人的线程中运行。 |
| `blocked` | 执行人遇到阻塞，需要其他地方先处理。 |
| `review` | 工作已完成，但尚未通过审核。 |
| `done` | 已被采纳，计入 Mission 最终答复。 |
| `failed` | 需要 Leader 介入才能继续。 |
| `cancelled` | 随 Mission 取消，或被显式取消。 |

Leader 在派发任务时会设置任务之间的依赖，也可以先审阅上游结果再放行下游工作。审核本身也是一条任务——任何成员都可以被指派去审核别人的工作。

## 协作回路

队友之间通过三种方式协作：

- **消息** —— 成员之间或发给 Leader 的轻量便条，用来提示某件事或请求输入。
- **工件** —— 显式交接：一份命名的结果，附上它的位置和简短摘要，让接手的队友清楚自己拿到的是什么。
- **进度更新** —— 中途状态或抛出的阻塞，让任务板保持最新。

Leader 不会一直轮询。它派发工作后就退到一旁；只有当结果产出、出现阻塞、队友需要答复，或 Mission 进入最终审核时，它才会被重新唤起。

## Desktop UI

Team 面板是一块"卡牌协作桌"：

- 机器人队友卡、Mission 卡、Task 卡，状态都从 Teams 数据实时拉取。
- 右侧详情栏展示当前选中的队友、Mission 或 Task。
- 桌面上提供主 Mission 起草工作流，用于创建新 Mission。
- 当上下文充分时，链接可直接打开真实的 Mission 队友线程；插件启用后，Mission 队友线程也会出现在常规会话列表中。

状态徽章会展示调度相关状态（`waitingDependencies`、`blocked`、`review`、`awaitingLeaderReview`、`done`），让卡上就能诊断停滞的 Mission。

## 配置

Teams 由内置插件 `agent-teams` 控制。它的 Mission、任务和交接文件都存放在工作区内，因此会随项目走，并在各入口之间保持可用。每个 Mission 都为队友提供一块共享草稿区，用于存放可复用的交接物。

运行时设置在 `teams` 配置段中。可用键见 [配置完整参考](../../developing/configuration)，Mission 线程的存储方式见 [统一会话核心](../../developing/architecture/session-core)。

## 相关文档

- [SubAgents](./subagents) — Teams 显得过重时的单级委派。
- [Automations 与 Goals](./automations) — 计划驱动的任务与 Thread 级目标，可与 Teams Mission 组合使用。
- [统一会话核心](../../developing/architecture/session-core) — 支撑 Mission 队友线程的 Thread / Turn / Item 模型。
