# 外部 Agent 协作

DotCraft 的 workspace 天然适合和其他 coding agent 协作。`.craft/` 目录把项目规则、长期记忆、会话历史、trace 元数据和工具调用证据放在仓库旁边，因此你可以把有用上下文交给外部 agent，而不需要让它从零散聊天记录里猜。

本页面向需要把 DotCraft thread 交接给其他 coding agent 的集成方与贡献者，讲解如何找到目标 thread、把它导出成 Markdown 交接文档，以及导出内容包含什么。

DotCraft 支持两种协作方式：

| 方式 | 适用场景 | 接口 |
|---|---|---|
| 实时接入 | 对方工具可以直接接入 DotCraft | AppServer、ACP、SDK、Desktop |
| 文档交接 | 对方 coding agent 只能接收文件或 prompt 上下文 | `dotcraft context` Markdown 导出 |

本文重点讲文档交接。实时接入请看 [统一会话核心](../architecture/session-core) 和 [AppServer 模式](../lifecycle/appserver)。

## 应该交接什么

一次好的外部 agent 交接通常包含：

- 仓库本身，或外部 agent 可以读取的具体文件。
- 相关 thread 的 `dotcraft context export` Markdown 文件。
- 你希望外部 agent 接下来完成的具体任务。
- 隐私约束，例如是否允许包含工具输出、命令输出或完整记忆历史。

不要随意分享 provider 凭据、全局 `~/.craft/config.json` 或原始 state DB。除非你明确信任接收方并且正在做取证级排障，否则 Markdown 导出是更安全的默认选择，因为它经过整理，并且有脱敏开关。

## 找到相关 thread

如果你只知道症状、报错、工具名、模型 id 或用户请求片段，先搜索 workspace：

```bash
dotcraft context search --query "provider timeout gpt-5.3" --workspace "D:\path\to\project" --limit 5
```

搜索会优先读取 workspace 的 state DB，包括 thread 元数据、trace session binding、trace session 计数和 trace events。随后再为候选结果补充简短 rollout 片段。只要匹配的 thread 有 rollout 文件，结果里会给出可直接使用的 export 命令。

如果要让脚本或其他工具消费结果，可以加 `--json`：

```bash
dotcraft context search --query "thread_20260601" --workspace "D:\path\to\project" --json
```

## 导出交接文档

拿到 thread id 后：

```bash
dotcraft context export --thread thread_20260601_ab12cd --workspace "D:\path\to\project" --output handoff.md
```

默认导出参数：

| 参数 | 默认值 | 原因 |
|---|---|---|
| `--profile` | `handoff` | 输出结构面向另一个 coding agent |
| `--tool-results` | `summary` | 保留证据，但不倾倒完整命令输出或 API 响应 |
| `--history` | `tail` | 包含近期记忆历史，不把所有旧事件都带进去 |

更严格的交接：

```bash
dotcraft context export --thread thread_20260601_ab12cd --tool-results none --history tail --output handoff.md
```

完整审计 transcript：

```bash
dotcraft context export --thread thread_20260601_ab12cd --profile transcript --tool-results full --history full --output transcript.md
```

如果不传 `--output`，Markdown 会输出到 stdout。

## 导出内容

Markdown 导出包含：

- 导出元数据：workspace、`.craft` 路径、rollout 路径、profile 和隐私模式。
- Thread 元数据：状态、时间戳、来源入口、显示名和 turn 数。
- Workspace 记忆：`MEMORY.md`，以及按 `--history` 模式裁剪后的 `HISTORY.md`。
- 连续性事件：影响上下文连续性的 rollback 和 compaction 记录。
- 当前模型可见上下文：从最新可用 compaction checkpoint 加 surviving tail turns 重建。
- 会话记录：从 canonical rollout JSONL replay 后仍然存活的 turns。

Reasoning content 不会导出。工具调用会保留。工具和命令结果遵循 `--tool-results`。`RequestUserInput` 的回答正文始终省略，包括 full 导出；问题文本和关联 ID 仍会保留。

## Rollback 与 Compaction

导出时，DotCraft 不会把 rollout JSONL 当作简单追加 transcript。它会 replay canonical events：

- `thread_rolled_back` 会从导出的 conversation 中移除被回滚的尾部 turns，并添加 continuity note。
- `context_compacted` 会作为 continuity event 列出。
- 如果最新可解码 checkpoint 覆盖的 turn 仍然在 rollback 后幸存，它会用于重建 `Current Model-Visible Context`。
- 如果 checkpoint 损坏或已经不适用，导出器会回退到 surviving rollout turns，并输出 warning。

这很重要：外部 coding agent 需要的是 DotCraft 实际会继续使用的上下文，而不是已经 rollback 的过期尾部。

## 交接清单

把上下文发给其他 coding agent 前：

1. 如果不确定 thread id，先运行 `dotcraft context search`。
2. 默认使用 `--tool-results summary`，只有确实需要时才导出 full。
3. 快速打开 Markdown，检查是否包含敏感输出。
4. 明确说明 continuity warnings，尤其是 rollback 或被忽略的 compaction checkpoint。
5. 告诉外部 agent 下一步目标，以及哪些文件可以修改。

## 相关文档

- [项目级工作区](../../features/project-first)
- [统一会话核心](../architecture/session-core)
- [可观测性](../../features/self-hosted/observability)
- [AppServer 模式](../lifecycle/appserver)
