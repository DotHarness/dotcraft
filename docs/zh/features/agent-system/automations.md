# Automations 与 Goals

DotCraft 给 Agent 两种"不用你一轮轮盯着也能干活"的方式：

- **Automations** — 让 Agent 定时或手动跑起来，报告、巡检、清理这类例行活儿自己就完成了。
- **Goals** — 给一段对话钉一个长期目标，每当这段对话空下来，DotCraft 就接着往前推。

![DotCraft Automations and Goals overview](/automations-goals-overview.svg)

![DotCraft Goals](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/goal.gif)

## Automations

Automations 在你的工作区里运行本地任务——可以定时，也可以随时手动触发。它适合那些你本来要自己记着去做的例行活儿：周报、夜间巡检、清理。

### 创建任务

在 Desktop 的 Automations 面板里创建和管理任务。每个任务由一段简短的说明（做什么、什么时候做）和一段工作流提示词（怎么做）组成。任务随项目保存在 `.craft/tasks/` 下，会跟着仓库走。

### 任务能做什么

| 能力 | 说明 |
|---|---|
| 手动运行 | 在 Desktop 中按需触发任务 |
| 定时运行 | 给任务设置计划，让它自己跑 |
| 线程绑定 | 把任务绑定到已有对话，后续运行继续在该对话进行 |
| Agent Profile | 让任务以某个已保存的 Agent Profile 运行，只使用该 Agent 的工具、技能和模型（可选，默认用工作区 Agent） |
| 模板 | 把任务保存为可复用模板 |
| 完成摘要 | 工作完成后，Agent 写一段简短摘要 |
| 删除 | 删除任务，可同时删除其关联对话 |

### 审核 worktree 输出

未绑定到现有 Thread 的自动化任务，在 Git 项目中会运行在受管 Git worktree 里。Desktop 的审核面板会显示分支、是否有未提交改动，以及是否有领先基础版本的提交。

你可以在审核面板里打开任务 Thread、把 worktree 交接回本地 workspace，或丢弃任务 worktree。丢弃会移除该任务的 worktree 输出和受管分支。只有在确认不再需要这些改动时再使用。

调度格式、工作流变量和完整的任务字段见 [Automations、Goals 与 Hooks](../../developing/configuration#automations-goals-与-hooks)。

---

## Goals

Goal 把一个长期目标钉在某一段对话上。设置之后，目标就跟着这段对话。每当这段对话空闲（且自动继续开启），DotCraft 就继续推进它——直到完成、暂停、清除，或被 token 预算叫停。

### 适合的任务

- 需要多轮推进的重构、文档整理、迁移或排查任务。
- 需要你随时暂停、恢复、替换或清除目标的长期工作。
- 需要把进度、耗时和完成状态跟对话一起留存的工作。

### 生命周期

| 状态 | 含义 |
|---|---|
| `active` | 正在生效，对话空闲时可自动继续 |
| `paused` | 保留，但不自动继续 |
| `budgetLimited` | token 预算用尽，等待你处理 |
| `complete` | Agent 审核完工作并标记完成 |

### 如何管理目标

| 操作 | 在哪里 |
|---|---|
| 设置或替换目标 | Desktop 的 Goal 控件 |
| 暂停或恢复目标 | Desktop 的 Goal 控件 |
| 清除目标 | Desktop 的 Goal 控件 |
| 查看目标状态 | 对话列表与详情视图 |

目标字段以及这些控件背后的 AppServer 方法见 [Automations、Goals 与 Hooks](../../developing/configuration#automations-goals-与-hooks)。

### Goals × Automations

Automations 适合"按时间或手动触发一次任务"，Goals 适合"让同一个 Thread 持续朝一个长期目标推进"。两者可以组合：Automation 负责定时唤醒或提交检查任务，Goal 负责保存这条 Thread 的长期方向和完成状态。

## 常见场景

| 场景 | 推荐 |
|---|---|
| 周报、日报、定时巡检 | Automations 定时触发 |
| 自动跑测试套件并把摘要写入对话 | Automations，带完成摘要 |
| 持续推进一项重构或文档整理 | Goals |
| 让定时任务沿着同一长期目标推进 | Automations + Goals |
| 文件写入后自动 lint/format | [生命周期 Hooks](./hooks) `PostToolUse` |
| 阻止危险 Shell 命令 | [生命周期 Hooks](./hooks) `PreToolUse` |

## 相关文档

- [统一会话核心](../../developing/architecture/session-core) — Thread / Turn / Item 与目标状态的关系
- [可观测性](../self-hosted/observability) — Dashboard 上的 Trace 和审批
- [生命周期 Hooks](./hooks) — 在会话、prompt、工具和 turn 时机运行脚本
- [配置完整参考](../../developing/configuration#automations-goals-与-hooks) — Automations、Goals 与 Hooks 字段
