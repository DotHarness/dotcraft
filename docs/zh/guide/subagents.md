# Subagents

Subagent 是主 Agent 的委派机制：把一段独立任务交给它，在单独的上下文中执行，只把结果带回主对话。探索、调研这类会产生大量中间内容的工作交给 subagent，主对话就能保持简洁。

![DotCraft subagent 委派示意图](/subagent-delegation-overview.svg)

每个 subagent 在 DotCraft 内部以独立上下文运行，并由角色决定它能做什么。默认配置即可安全使用：主 Agent 可以创建一级 subagent，subagent 不会再往下派生。完整配置字段见[Subagent 配置](../configuration/subagents)。

## 角色决定它能做什么

| 角色 | 适合 | 权限 |
|---|---|---|
| `explorer` | 只读的代码探索和资料调研 | 只读文件与检索，可运行 `git diff` 这类观察命令，不能写入 |
| `worker` | 实现、验证、修改文件 | 可读写文件、运行命令和访问网络 |
| `default` | 总结、分析等通用协作 | 保守工具集，不使用高权限工具 |

角色限制在工具调用时强制执行。subagent 调用被禁止的工具会得到明确的拒绝原因，可以据此换一条路。你也可以在工作区配置中自定义角色，为团队固定常用的委派方式。

## 在 Desktop 中查看 subagent 进度

![在 DotCraft Desktop 中查看多个后台 subagent 及其状态](https://github.com/DotHarness/resources/raw/master/dotcraft/docs/subagents.webp)

subagent 工作时，Desktop 中的主对话仍然可用。打开后台 Agent 摘要，可以看到哪些任务还在运行、哪些已经完成。最终结果仍会返回主对话。

## 审批照常生效

subagent 的文件和命令操作走当前对话同一套审批，每条请求都会标注来自哪个 subagent。需要更强隔离时，按[安全](../self-hosting/security)里的做法收紧角色的工具清单和审批。

## 怎么选

| 场景 | 推荐 |
|---|---|
| 受控的只读探索 | `explorer` |
| 在当前工作区内完成实现 | `worker` |
| 总结、分析或通用协作 | `default` |
| 需要最强的工具隔离 | 自定义角色 + 收紧的工具清单 + 请求批准 |

subagent 的对话会作为主对话的子对话保存。归档、恢复或删除主对话时，子对话一并处理，重启后角色和工具限制依然有效。

## 相关文档

- [安全](../self-hosting/security) — 用工作区边界和审批进一步约束 subagent
- [可观测性](../self-hosting/observability) — 在 Dashboard 查看 subagent 的调用和审批记录
