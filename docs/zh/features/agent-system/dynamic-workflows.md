# Dynamic Workflows

Dynamic Workflows 让 DotCraft 把大型任务转成可复用的 JavaScript 编排。脚本在后台协调多个专注的
SubAgent，把中间结果留在主对话之外，并在完成后把结果送回原对话。

当任务的执行顺序、分支或并行拆分需要稳定复现时，适合使用 workflow。只委派一个边界明确的任务时，
使用单个 SubAgent 更简单。多个成员需要在执行过程中相互协调时，Agent Team 更合适。

![在对话中选择启用后，可复用的 workflow 脚本在后台运行，把任务同时扇出给多个 SubAgent，完成后把一个结果排回对话](/dynamic-workflows-overview.svg)

## 让 DotCraft 使用 workflow

在对话中明确选择 Dynamic Workflow：

```text
使用 Dynamic Workflow，从正确性、安全性和测试覆盖率三个角度审查变更，最后把发现合并成一份按优先级排序的报告。
```

DotCraft 会编写编排脚本，在当前权限策略要求时请求审批，然后在后台启动。运行期间仍可继续使用当前对话。
完成后，DotCraft 会把结果排入原对话。

## 保存可复用 workflow

把 workspace workflow 保存为 `.craft/workflows/` 直属目录下的 `.js` 文件。需要随仓库共享时，提交该文件。
把个人 workflow 保存到 `~/.craft/workflows/`，即可在本机不同 workspace 中使用。

每个 workflow 都以静态 metadata 开头，后面是 JavaScript 正文：

```js
export const meta = {
  name: "review-change",
  description: "Review a change from independent perspectives",
  whenToUse: "Use for a substantive code review",
  phases: ["review", "synthesize"]
};

const reviews = await parallel([
  () => agent("Review the change for correctness.", {
    label: "correctness",
    phase: "review"
  }),
  () => agent("Review the change for missing tests.", {
    label: "tests",
    phase: "review"
  })
]);

return agent({
  prompt: "Combine the reviews into one ranked report.",
  context: reviews
}, {
  label: "synthesis",
  phase: "synthesize"
});
```

`meta.name` 和 `meta.description` 必填，`whenToUse` 和 `phases` 可选。Metadata 必须是字面量数据。
不接受 import、计算值、函数调用或依赖运行时的 metadata。正文支持 top-level `await`，并且必须返回可 JSON
序列化的值。

保存后，workflow 会注册为 slash command。可以这样给示例传入结构化参数：

```text
/review-change 重点检查 src/ 下的认证改动
```

DotCraft 会把命令文本转换成脚本可读取的不可变 `args`。同名时，workspace workflow 优先于个人
workflow。同一位置中的名称应保持唯一。

## 编排 Agent 工作

Workflow 脚本提供一组精简的编排 API：

| API | 用途 |
|---|---|
| `agent(input, options?)` | 启动一个拥有全新上下文的原生 SubAgent，并解析为结果或 `null`。 |
| `parallel(thunks)` | 并行启动延迟调用，并按声明顺序返回结果。 |
| `pipeline(items, ...stages)` | 并行处理多个 item，同时让每个 item 依次经过各个 stage。 |
| `phase(name, detail?)` | 记录一个具名进度边界。 |
| `log(value)` | 为本次 run 记录有大小限制的诊断数据。 |
| `args` | 读取本次调用不可变的结构化输入。 |
| `budget` | 读取当前 run 限制和累计用量。 |
| `cwd` | 读取 workflow 的 workspace 根目录。 |

每个 pipeline stage 接收 `(previous, original, index)`。如果某个 stage 返回 `null`，该 item 的后续
stage 不再执行。即使 Agent 的完成顺序不同，`parallel()` 和 `pipeline()` 仍会保持输入顺序。

### 配置 Agent 调用

`agent()` 支持以下选项：

| 选项 | 用途 |
|---|---|
| `label` | 为 run 记录中的调用设置稳定名称。 |
| `phase` | 把调用关联到一个 workflow phase。 |
| `schema` | 要求结果符合指定 JSON Schema。 |
| `model` | 覆盖 child model。 |
| `effort` | 覆盖 child reasoning effort。 |
| `isolation` | 使用 `shared` 或受管 `worktree`。 |
| `agentType` | 选择一个原生 Agent role。 |

带 `schema` 的调用会在校验通过后返回提交的 JSON 值。没有 `schema` 时返回 SubAgent 的最终文本。
被取消或遇到不可恢复错误的调用会返回 `null`，使用结果前应处理该值。

## 通过插件共享 workflow

已启用的插件可以从根目录的 `workflows/` 提供 workflow，也可以通过 manifest 的 `workflows` 字段指定
其他目录。插件命令始终带 namespace：

```text
/review-tools:review-change
```

Namespace 可以防止插件 workflow 覆盖 workspace 或个人 workflow。

## 理解执行边界

JavaScript 正文只负责协调，不能直接读取文件、访问网络、启动进程、加载 module 或调用 DotCraft service。
把这些操作交给 `agent()`。child Agent 的工具调用仍会经过正常的 workspace 边界和工具审批。

每次 `agent()` 调用都使用全新的对话上下文，同时继承父对话的 workspace、权限策略和 model 默认值。
`isolation: "worktree"` 会为该调用创建受管 Git worktree。完成后，DotCraft 会删除干净的 worktree。
存在改动或 commit 时则保留以供检查，并且不会自动合并。

Runtime 会限制并发工作量，并且每次 run 最多允许 1,000 个 Agent 调用。

## 相关文档

- [SubAgents](./subagents) — 了解 workflow 创建的 child session
- [插件与工具](./plugins-tools) — 打包和分发可复用能力
- [Automations 与 Goals](./automations) — 定时执行工作或持续推进长期目标
- [安全与沙箱](../self-hosted/security) — 配置 workspace 和工具边界
