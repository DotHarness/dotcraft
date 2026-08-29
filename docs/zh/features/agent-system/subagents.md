# Subagents

Subagent 是主 Agent 的委派机制：把一段独立任务交给它，在单独的上下文中执行，只把结果带回主对话。探索、调研这类会产生大量中间内容的工作交给 subagent，主对话就能保持简洁。

![DotCraft subagent 委派示意图](/subagent-delegation-overview.svg)

每个 subagent 由两件事定义：角色决定它能做什么，运行时决定它在哪里跑。默认配置即可安全使用：主 Agent 可以创建一级 subagent，subagent 不会再往下派生。完整配置字段见[配置参考](../../developing/configuration#subagent-与-external-cli-profiles)。

## 角色决定它能做什么

| 角色 | 适合 | 权限 |
|---|---|---|
| `explorer` | 只读的代码探索和资料调研 | 只读文件与检索，可运行 `git diff` 这类观察命令，不能写入 |
| `worker` | 实现、验证、修改文件 | 可读写文件、运行命令和访问网络 |
| `default` | 总结、分析等通用协作 | 保守工具集，不使用高权限工具 |

角色限制在工具调用时强制执行。subagent 调用被禁止的工具会得到明确的拒绝原因，可以据此换一条路。你也可以在工作区配置中自定义角色，为团队固定常用的委派方式。

## 运行时决定它在哪里跑

原生运行时由 DotCraft 自己执行 subagent，角色的工具限制完整生效，并且与主对话共用同一份提示词前缀，启动开销小。

也可以用外部 coding CLI 作为运行时，内置支持 Codex CLI 和 Cursor CLI。外部 CLI 以独立进程运行一次性任务，通常只汇报阶段性进度。DotCraft 会把角色说明传给它，但无法约束它内部的工具调用。需要强隔离时优先使用原生运行时，并配合[安全与沙箱](../self-hosted/security)。

## 审批照常生效

原生 subagent 的文件和命令操作走当前会话同一套审批，每条请求都会标注来自哪个 subagent。外部 CLI 的内部操作 DotCraft 无法逐条拦截，但会把当前的审批模式尽量传递给它。

## 怎么选

| 场景 | 推荐 |
|---|---|
| 受控的只读探索 | 原生运行时 + `explorer` |
| 在当前工作区内完成实现 | 原生运行时 + `worker` |
| 想复用特定外部 coding CLI 的工作流 | 外部 CLI 运行时 |
| 需要最强的工具隔离 | 原生运行时 + 收紧的工具清单 + 沙箱 |

subagent 的对话会作为主对话的子对话保存。归档、恢复或删除主对话时，子对话一并处理，重启后角色和工具限制依然有效。

## 相关文档

- [安全与沙箱](../self-hosted/security) — 用工作区边界和沙箱进一步约束 subagent
- [可观测性](../self-hosted/observability) — 在 Dashboard 查看 subagent 的调用和审批记录
