# 自动化与目标

概念和日常用法见[自动化与目标](../guide/automations)。

## 字段

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Automations.Enabled` | 是否启用 Automations 编排器 | `true` |
| `Automations.PollingInterval` | 轮询间隔 | `00:00:10` |
| `Automations.MaxConcurrentTasks` | 本地任务最大并发数 | `3` |
| `Automations.TurnTimeout` | 单轮对话超时时间 | `00:30:00` |
| `Automations.WorktreeRetentionEnabled` | 是否启用空闲自动化任务 worktree 清理 | `true` |
| `Automations.WorktreeRetentionIdlePeriod` | 干净自动化任务 worktree 进入清理候选前的空闲时间 | `21.00:00:00` |
| `Goals.Enabled` | 启用目标存储、AppServer 方法、目标上下文注入、用量统计和模型 Goal 工具 | `true` |
| `Goals.AutoContinueEnabled` | 允许 active 目标在线程空闲时自动继续 | `true` |

`Automations.WorktreeRetentionIdlePeriod` 必须至少为 `14.00:00:00`。Retention sweep 只会移除空闲、干净、且没有领先基础版本提交的受管自动化任务 worktree。

Automation AppServer 方法：

| Method | 说明 |
|---|---|
| `automation/list` | 列出自动化 |
| `automation/read` | 读取自动化 |
| `automation/create` | 创建自动化 |
| `automation/update` | 检查版本后保存 |
| `automation/run` | 排队执行一次 |
| `automation/delete` | 删除自动化 |
| `automation/runs/list` | 读取运行历史 |
| `automation/presets/list` | 列出对话预设 |

Goal AppServer 方法：

| Method | 说明 |
|---|---|
| `thread/goal/set` | 设置、替换、暂停或恢复 Thread goal |
| `thread/goal/get` | 读取当前 Thread goal 状态 |
| `thread/goal/clear` | 清除当前 Thread goal |
