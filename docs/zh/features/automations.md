# Automations 与 Goals

DotCraft 把"让 Agent 在特定时机自动跑一段任务"和"让一个 Thread 持续朝明确目标推进"分成两个互补的能力：

- **Automations** — 工作区级任务调度，定时或手动触发 Agent 跑一段工作流
- **Goals** — Thread 级长期目标，用户显式设置后，DotCraft 可在 Thread 空闲时继续推进

![DotCraft Automations and Goals overview](/automations-goals-overview.svg)

![DotCraft Goals](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/goal.gif)

## Automations

DotCraft 原生 Automations 仅覆盖本地任务。它从当前工作区的 `.craft/tasks/` 读取任务文件，按计划或手动触发 Agent，支持线程绑定、模板、重试、活动展示和 `CompleteLocalTask` 完成路径。

### 创建本地任务

一个本地任务位于 `.craft/tasks/<task-id>/`：

```text
.craft/
  tasks/
    weekly-report/
      task.md
      workflow.md
```

`task.md` 描述任务标题、正文、调度和线程绑定；`workflow.md` 描述 Agent 要执行的工作流提示词。

### 常用能力

| 能力 | 说明 |
|---|---|
| 手动运行 | Desktop Automations 面板或 AppServer `automation/task/run` |
| 定时运行 | 在任务文件中配置 `schedule` |
| 线程绑定 | 把任务绑定到已有线程，后续运行提交到该线程 |
| 模板 | 在 `.craft/automations/templates/` 保存可复用任务模板 |
| 完成工具 | Agent 调用 `CompleteLocalTask` 写入完成摘要 |
| 删除任务 | 删除任务文件夹，可同时删除关联线程 |

### Workflow 模板变量

| 变量 | 说明 |
|---|---|
| `task.id` | 任务 id |
| `task.title` | 任务标题 |
| `task.description` | `task.md` 正文 |
| `task.thread_id` | 绑定线程 id |

AppServer 方法和 Automations 配置字段见 [Automations、Goals 与 Hooks](../developing/configuration.md#automations-goals-与-hooks)。

### `CompleteLocalTask` 工具

| 参数 | 说明 |
|---|---|
| `summary` | 简短完成说明 |

Agent 完成工作后调用，任务记录完成摘要并进入完成路径。

### 目录结构

```text
<workspace>/
  .craft/
    tasks/
      <task-id>/
        task.md
        workflow.md
    automations/
      templates/
        <template-id>/
          template.md
```

---

## Goals

Goals 是挂在单个 Thread 上的长期目标。用户显式设置目标后，DotCraft 会把目标写入会话核心，后续 Turn 都能看到这个目标；当 Thread 空闲且自动继续开启时，系统可以继续推进目标，直到完成、暂停、清除或触达预算限制。

### 适合的任务

- 需要多轮推进的重构、文档整理、迁移或排查任务。
- 需要用户随时暂停、恢复、替换或清除目标的长期工作。
- 需要把 token 用量、耗时和完成状态跟 Thread 一起持久化的工作。

### 生命周期

| 状态 | 含义 |
|---|---|
| `active` | 目标正在生效；Thread 空闲时可自动继续 |
| `paused` | 目标保留但不自动继续 |
| `budgetLimited` | 达到 token 预算，等待用户处理 |
| `complete` | Agent 审核后标记完成 |

### 常用入口

| 操作 | 入口 |
|---|---|
| 设置 / 替换目标 | Desktop 输入框的 Goal 控件、支持 `threadGoals` 的客户端、AppServer `thread/goal/set` |
| 暂停 / 恢复目标 | Desktop Goal 控件或 AppServer `thread/goal/set` 更新状态 |
| 清除目标 | Desktop Goal 控件或 AppServer `thread/goal/clear` |
| 读取目标状态 | Thread 列表、Thread 详情、AppServer `thread/goal/get` |

Goals 配置字段和 AppServer goal 方法见 [Automations、Goals 与 Hooks](../developing/configuration.md#automations-goals-与-hooks)。

### Goals × Automations

Automations 适合"按时间或手动触发一次任务"，Goals 适合"让同一个 Thread 持续朝一个长期目标推进"。两者可以组合：Automation 负责定时唤醒或提交检查任务，Goal 负责保存这条 Thread 的长期方向和完成状态。

## 常见场景

| 场景 | 推荐 |
|---|---|
| 周报、日报、定时巡检 | Automations 定时触发 |
| 自动跑测试套件并把摘要写入线程 | Automations + `CompleteLocalTask` |
| 持续推进一项重构或文档整理 | Goals |
| 让定时任务沿着同一长期目标推进 | Automations + Goals |
| 文件写入后自动 lint/format | [Hooks](./security.md#hooks) `AfterToolCall` |
| 阻止危险 Shell 命令 | [Hooks](./security.md#hooks) `BeforeToolCall` |

## 故障排查

### Goal 没有继续推进

确认配置中启用了 Goals 和自动继续，目标状态是 `active`，并且当前 Thread 没有正在运行的 Turn 或等待用户审批。

### Goal 进入 budgetLimited

目标达到 token 预算后会停止自动继续。你可以增加预算、替换目标、暂停目标，或让 Agent 总结当前进度后重新设置更小的目标。

### 客户端看不到 Goal 控件

确认 AppServer `initialize` 返回了 `capabilities.threadGoals = true`。如果 Goals 被禁用，服务器不会暴露目标控制。

### 自动化任务不显示在 Desktop

Automations 面板需要 Gateway 加载 Automations 模块；本地 Dashboard 适合调试单工作区，不负责完整自动化编排。

## 相关入口

- [项目级工作区](./workspace.md) — `.craft/tasks/`、`.craft/automations/` 在整体目录里的位置
- [统一会话核心](./session-core.md) — Thread / Turn / Item 与目标状态的关系
- [可观测性](./observability.md) — Dashboard 上的 Trace 和审批
- [安全与沙箱](./security.md#hooks) — Hooks、安全守卫与沙箱隔离
- [配置完整参考](../developing/configuration.md#automations-goals-与-hooks) — Automations、Goals 与 Hooks 字段
