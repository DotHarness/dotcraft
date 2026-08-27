# AGENTS.md 多协议加载参考

调研范围包括 Codex 的 `AGENTS.md`、Claude Code 的 `CLAUDE.md`，以及 DotCraft 对 OpenAI Responses、OpenAI Chat Completions 和 Anthropic Messages 的统一投影方式。DotCraft 的运行时目标文件保持为 `AGENTS.md`；`CLAUDE.md` 仅作为初始化时可导入的来源。

## 核心结论

| 维度 | Codex | Claude Code | DotCraft 采用方式 |
|---|---|---|---|
| 项目文件 | `AGENTS.md`，同目录 override 优先 | `CLAUDE.md`、rules 与 include | `AGENTS.override.md` 或 `AGENTS.md`，每目录只选一个 |
| 发现范围 | 项目根到 cwd | 高层目录到 cwd，并可按文件访问延迟加载嵌套规则 | 最近 `.git` 根到 effective cwd；无 Git marker 时只检查 cwd |
| 用户级文件 | Codex home 下的 override 或默认文件 | 用户级 Claude memory/rules | `~/.craft/AGENTS.override.md` 或 `~/.craft/AGENTS.md` |
| 模型角色 | 专用上下文项投影为 `user` | meta `user` message；system prompt 独立 | 所有 provider 统一投影为普通 `user` item |
| 生命周期 | 会话内缓存，并维护来源和更新状态 | eager 内容会话内缓存，compaction 后重载 | `StableUntilCompaction`；在明确的线程环境边界刷新 |
| 嵌套目录 | 初始加载止于 cwd，提示 agent 自查更深目录 | 文件访问时延迟注入适用规则 | 初始加载止于 cwd，由基础策略要求 agent 自查 |
| 项目预算 | 项目链共享 32 KiB，用户级内容不计入 | 普通 memory 没有同等硬预算 | 项目链共享 `ProjectDocMaxBytes`，默认 32 KiB |
| 来源可观测性 | 保留来源路径 | 渲染时标记来源 | lifecycle result 返回 `instructionSources` |

## `role=user` 的适配

`role=user` 表示 provider 消息形状，不等同于“本轮真人输入”。DotCraft 使用内部 item kind `agents_md.instructions` 区分项目指令与普通对话，并由基础 system policy 定义权限和作用域。

| 顺序 | 内容 | Provider 投影 | 权限 |
|---:|---|---|---|
| 1 | Harness policy、工具和安全规则 | OpenAI system/developer 或 Anthropic top-level system | 最高 |
| 2 | 当前 `AGENTS.md` snapshot | 普通 `user` message | 受目录作用域约束 |
| 3 | 会话历史与当前用户输入 | 正常 user/assistant/tool history | 直接指令高于项目文件 |

统一规则：

1. `AGENTS.md` 对所在目录子树生效，更深层文件优先。
2. 同目录 `AGENTS.override.md` 优先于 `AGENTS.md`。
3. 运行时策略以及直接 system、developer、user 指令高于项目文件。
4. OpenAI Responses 不把项目内容升级为 developer。
5. Anthropic 不把项目内容放入 top-level system，也不使用 Claude Code 的 `<system-reminder>` 包装。

## 发现与渲染

| 项目 | DotCraft 行为 |
|---|---|
| 用户级候选 | override 可读且非空时采用；否则告警并尝试默认文件。用户级内容不受项目预算限制。 |
| 项目根 | 从 effective cwd 向上寻找最近 `.git` 文件或目录。 |
| 目录顺序 | 项目根到 cwd。secondary roots 不参与；worktree 使用自身 checkout。 |
| 同目录候选 | override 优先；空项目 override 遮蔽默认文件但不贡献内容。 |
| 预算 | 按读取的原始字节共享预算，截断后再进行 lossy UTF-8 解码。 |
| 路径 | 读取可跟随 symlink；来源保留规范化的逻辑绝对路径。 |
| 错误 | 项目链读取错误终止该链并保留用户级内容。 |
| 渲染 | 用户内容在前，项目内容按 root→cwd 合成一个带明确边界的 item。 |

## 线程生命周期

| 事件 | 行为 |
|---|---|
| 普通 turn | 使用 admission 时捕获的 effective cwd，并复用同一个稳定 snapshot；之后发生的线程 workspace 更新仅影响下一 turn。 |
| Compaction | 从压缩输入排除 AGENTS item，释放稳定页，并基于当前 turn 捕获的 cwd 重新加载一个当前 item。独立 maintenance 使用其开始时的线程 cwd。 |
| Cold resume | 重新发现文件，并替换或删除历史中的 marked item。 |
| cwd/worktree 变化 | 根据新的 effective cwd 建立 snapshot。 |
| 普通 fork | 子线程按自己的环境加载，父线程保持不变。 |
| Native full-history SubAgent | 环境一致时通过唯一的 runtime context-page manager 继承父线程已采样的精确稳定页。 |
| Fresh/bounded child | 独立加载。 |
| External CLI | 由外部 runtime 负责发现，DotCraft 不重复注入。 |

`Content + Sources + Fingerprint` 必须来自同一个 context-page snapshot。`AgentFactory` 必须持有唯一且非空的 runtime context-page manager；调用方未传入时由 factory 创建。内容更新通过已有 history replacement checkpoint 同步到持久化历史和 Responses provider history；无内容时直接删除 marked item。

Codex 在 session 初始化时无条件创建唯一的 `AgentsMdManager`。它在 `capture_step_context` 中以 `TurnContext` 的配置和环境选择刷新 AGENTS，再把结果保存到不可变的 `StepContext.loaded_agents_md`，从而让同一次请求的指令和执行环境共享边界；full-history fork 则保留父级的 reference-context 状态，而不是在子线程首次采样前重新读取文件。DotCraft 对应地由 `AgentFactory` 保证唯一 context-page manager，并复用现有 `TurnExecutionContext.Workspace` 和稳定 context page 表达这两个约束，不引入新的 StepContext 层，也不采用 Codex 的 replacement/removal notice。

## Claude Code 的参考价值

Claude Code 的流程可概括为：

1. 加载 managed、user、project 与 local memory。
2. 处理 rules、include、symlink 和条件路径。
3. 将 eager context 作为首个 meta user message 注入，system prompt 走独立通道。
4. 文件访问时发现更深目录的适用规则，并作为新的 meta user attachment 注入。
5. Compaction 清理 memory cache，使后续请求重新加载。

DotCraft 借鉴其“项目内容与 system 分离”和“compaction 后刷新”两点，不引入运行时 `CLAUDE.md`、include、paths frontmatter 或工具访问时的 lazy injection。

## 当前范围

| 包含 | 不包含 |
|---|---|
| 用户级和项目级 `AGENTS.md` 发现 | Workspace trust gate |
| override、预算、来源和稳定 snapshot | Claude rules、include、paths frontmatter |
| 三协议 plain-user 投影 | cwd 以下自动 lazy injection |
| start/resume/fork `instructionSources` | secondary-root 聚合 |
| Setup 导入 Claude 配置与 `/init` | watcher 或手动 reload 命令 |

## 证据索引

| 主题 | 参考位置 |
|---|---|
| Codex 发现和预算 | `references/codex/codex-rs/core/src/agents_md.rs` |
| Codex 用户级候选 | `references/codex/codex-rs/codex-home/src/instructions/mod.rs` |
| Codex user-role item | `references/codex/codex-rs/core/src/context/user_instructions.rs` |
| Codex 更新状态 | `references/codex/codex-rs/core/src/context/world_state/agents_md.rs` |
| Codex request-scoped snapshot | `references/codex/codex-rs/core/src/agents_md_manager.rs`、`references/codex/codex-rs/core/src/session/mod.rs`、`references/codex/codex-rs/core/src/session/step_context.rs` |
| Codex full fork reference context | `references/codex/codex-rs/core/src/agent/control/spawn.rs` |
| Claude eager/nested discovery | `references/claudecode/utils/claudemd.ts` |
| Claude attachment projection | `references/claudecode/utils/attachments.ts` |
| Claude user context | `references/claudecode/context.ts`、`references/claudecode/query.ts` |
| DotCraft prompt contract | `specs/architecture/prompt-composition.md` |
| DotCraft thread lifecycle | `specs/architecture/session-core.md` |
| DotCraft protocol result | `specs/protocols/appserver-protocol.md` |
