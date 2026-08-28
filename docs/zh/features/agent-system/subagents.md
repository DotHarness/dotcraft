# SubAgents

SubAgent 让主 Agent 把一段独立任务交给一个专注的"帮手"：它在自己的上下文里干活，再把结果交回来——主对话因此保持干净。两件事决定一个 SubAgent：

- `agentRole` — 它能做什么：行为、工具边界和提示词约束。
- `profile` — 用哪个运行时跑：DotCraft 原生运行时或外部 CLI。

![DotCraft SubAgent 委派示意图](/subagent-delegation-overview.svg)

如果只想让 DotCraft 安全地做一级委派，通常不需要改配置。默认设置允许根 Agent 创建一级 SubAgent，并阻止 SubAgent 再继续创建 SubAgent。

## 快速开始

默认行为比较保守：

- 根 Agent 可以调用 `SpawnAgent` 创建一级 SubAgent。
- 一级 SubAgent 已经到达默认深度限制，不能继续创建新的 SubAgent。
- 省略 `agentRole` 时使用 `default`。
- 原生 SubAgent 使用与父线程完全相同的生成提示词。角色限制在工具被调用时生效，而不是靠裁剪提示词。

完整 role 和 profile 配置字段见 [SubAgent 与 External CLI Profiles](../../developing/configuration#subagent-与-external-cli-profiles)。

## 内置角色

| Role | 适合 | 工具策略 |
|---|---|---|
| `default` | 通用一级协作、总结、本地分析 | 禁用 AgentTools，使用保守工具集 |
| `worker` | 实现、验证、文件修改 | 允许读写、Shell、Web，AgentTools 仍受深度限制 |
| `explorer` | 只读代码探索、资料调研 | 允许只读探索、Web，以及 `git diff` 这类观察命令。禁用写入、Plan/Todo、SkillManage、AgentTools |

`worker` 具备递归委派的能力模型，但递归仍然需要通过配置显式开启。

## Shell 权限

`explorer` 可以运行 `git diff`、`git log`、`ls`、`rg` 这类只读命令。会改动工作区的命令会被拒绝，读不明白的写法同样会被拒绝 —— 拒绝理由会告诉 SubAgent 是哪一处被拦下，它可以给参数加引号或者换个思路。

自定义 role 可以自行选择级别：不给 Shell、只读，或者不加限制。见 [SubAgent 与 External CLI Profiles](../../developing/configuration#subagent-与-external-cli-profiles)。

## 共享提示词

原生 SubAgent 从与父线程逐字相同的生成提示词启动。它的角色说明单独送达——作为对话开头的一条消息，而不是系统提示词里的一节。

这正是子线程能复用父线程已经让模型缓存下来的前缀的原因：模型看到的指令块和工具列表完全一致，新增的只有子任务本身。角色限制依然生效——SubAgent 调用被禁用的工具时会拿到拒绝原因。

## Profile：选择运行时

| Profile | 说明 |
|---|---|
| `native` | DotCraft 原生 SubAgent，支持 role-resolved 工具过滤 |
| `codex-cli` | 使用 Codex CLI 的一次性外部 SubAgent |
| `cursor-cli` | 使用 Cursor CLI 的一次性外部 SubAgent |
| `custom-cli-oneshot` | 自定义外部 CLI 的模板 profile |

DotCraft 会把 role instructions 传给外部 CLI，但无法强制拦截外部 CLI 内部工具调用。需要强隔离时，优先使用 `native`，并配合 role allow/deny list 和 [安全与沙箱](../self-hosted/security)。

## 使用外部 CLI 作为子代理

外部 CLI SubAgent 会把已有 coding-agent CLI 包装成短生命周期进程。相比 `native`，外部 CLI 通常只能提供阶段级进度，而不是每个工具调用的细节。

内置外部 profile 支持 Codex CLI 和 Cursor CLI。开启对应设置且 profile 支持 resume 时，DotCraft 可以复用外部 CLI 会话。匹配规则比较保守：优先使用相同 profile、label 和 working directory，而不是盲目续接任意已保存会话。

自定义外部 CLI profile、resume 提取、权限转发和厂商 headless 细节见 [SubAgent 与 External CLI Profiles](../../developing/configuration#subagent-与-external-cli-profiles)。

## 审批与权限穿透

**原生 SubAgents**

- 原生 SubAgent 内部的文件和 Shell 工具调用会复用当前 session 的审批服务。
- 审批请求会带上 SubAgent label 前缀，方便用户知道请求来自哪里。

**外部 CLI SubAgents**

- DotCraft 不拦截外部 CLI 内部工具调用。
- 当 profile 定义了 permission mapping 时，DotCraft 会把当前审批模式转成启动参数。
- Resume 参数会插在审批参数之前，但是否续接仍由 DotCraft 决定。

## 何时用什么

| 场景 | 推荐 |
|---|---|
| 需要受控只读探索 | `native` 的 `explorer` role |
| 需要在当前工作区策略内实现任务 | `native` 的 `worker` role |
| 需要特定外部 coding CLI 工作流 | 外部 CLI profile |
| 需要强工具隔离 | 优先 `native` + allow/deny list + sandbox |
| 需要团队固定委派行为 | 在工作区配置中定义 role |

## 对话生命周期

当原生 SubAgent 拥有独立保存的对话时，DotCraft 会把它作为主对话的子对话保留。重启后，它与主对话的关系、角色、运行时选择和工具限制仍然有效。

归档主对话时，DotCraft 也会归档其中已保存的 SubAgent 对话。恢复主对话时，只会恢复之前仍处于打开状态的子对话。你明确关闭过的子对话会继续保持归档。永久删除主对话时，其中已保存的子对话也会被删除。DotCraft 随后会尝试清理配套文件，清理失败时可以重试。

## 相关文档

- [安全与沙箱](../self-hosted/security) — 用工作区边界和沙箱限制 SubAgent 行为
- [可观测性](../self-hosted/observability) — 在 Dashboard 看 SubAgent 调用与审批
- [配置完整参考](../../developing/configuration#subagent-与-external-cli-profiles)
- [会话持久化](../../developing/architecture/session-persistence)
