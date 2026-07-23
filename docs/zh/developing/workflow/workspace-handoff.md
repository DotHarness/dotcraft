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

不要随意分享 provider 凭据、全局 `~/.craft/config.json` 或原始 state DB。除非你明确信任接收方并且正在做取证级排障，否则优先使用更容易限制和审阅的 Markdown 导出。但导出内容并不会自动变得安全；请通过 `--tool-results` 和 `--history` 缩小范围，再人工检查。

## 找到相关 thread

如果你只知道症状、报错、工具名、模型 id 或用户请求片段，先搜索 workspace：

```bash
dotcraft context search --query "provider timeout gpt-5.3" --workspace "D:\path\to\project" --limit 5
```

搜索会优先读取 workspace 的 state DB，包括 thread 元数据、trace session binding、trace session 计数和 trace events。随后再为候选结果补充简短 rollout 片段。只要匹配的 thread 有 rollout 文件，结果里会给出可直接使用的 export 命令。

搜索不会索引内部的精确模型历史或上下文压缩检查点内容。请把搜索片段用于定位，再导出选中的 thread 以获得重放后的对话。

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

最详细的 transcript：

```bash
dotcraft context export --thread thread_20260601_ab12cd --profile transcript --tool-results full --history full --output transcript.md
```

如果不传 `--output`，Markdown 会输出到 stdout。

> [!NOTE]
> `--tool-results full` 只取消导出器对 session 记录中现有工具结果的 preview 长度上限。它不会取回已经 spill 到 `.craft/tool-results/` 的原始 artifact，因此导出中仍可能只有记录下来的 preview 或引用。

## 导出内容

Markdown 导出包含：

- 导出元数据：workspace、`.craft` 路径、rollout 路径、profile 和隐私模式。
- Thread 元数据：状态、时间戳、来源入口、显示名和 turn 数。
- Workspace 记忆：`MEMORY.md`，以及按 `--history` 模式裁剪后的 `HISTORY.md`。
- 连续性事件：影响上下文连续性的 rollback 和 compaction 记录。
- 当前模型可见上下文：从最新可用 compaction checkpoint 加 surviving tail turns 重建。
- 会话记录：从 canonical rollout JSONL replay 后仍然存活的 turns。

导出器只会处理工具参数和结果中可识别的敏感键，以及有限的敏感文本模式。它不会把每个字段都作为 secret 扫描；会话消息、workspace 记忆、错误文本和其他自由格式内容可能原样出现。分享前必须人工审阅每一份导出。

Reasoning content、自由格式的 thread 元数据，以及内部 Provider 或会话 payload 不会导出。工具调用会保留，工具和命令结果遵循 `--tool-results`。`RequestUserInput` 的回答正文始终省略，包括 full 导出；问题文本和关联 ID 仍会保留。

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

- [项目工作区](../../features/project-workspace)
- [统一会话核心](../architecture/session-core)
- [会话持久化](../architecture/session-persistence)
- [可观测性](../../features/self-hosted/observability)
- [AppServer 模式](../lifecycle/appserver)
