# 动态工作流

动态工作流把一件大任务变成一段可复用的编排脚本。脚本在后台同时调度多个 subagent，中间过程留在主对话之外，做完后把结果送回原来的对话。

一件工作的步骤顺序、分支和拆分方式需要每次都一样时，用动态工作流。只委派一件事，[Subagents](./subagents) 更简单。多个成员需要边做边互相协调，[Agent 团队](./teams)更合适。

![可复用的工作流脚本在后台运行，把任务同时分给多个 subagent，完成后把一份结果送回对话](/dynamic-workflows-overview.svg)

## 跑一个工作流

![在 DotCraft Desktop 中打开动态工作流运行并查看编排步骤](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/dynamic-workflows.gif)

在对话里明确要求使用动态工作流：

```text
用动态工作流从正确性、安全性和测试覆盖率三个角度审查这次变更，最后合并成一份按优先级排序的报告。
```

DotCraft 会写好编排脚本，按当前权限策略请求审批，然后在后台启动。运行期间这段对话仍然可用，结果完成后会自己送回来。

## 保存成可复用的工作流

把脚本存成 `.craft/workflows/` 下的 `.js` 文件，它就跟着仓库走，团队里每个人都能用。只想自己在本机用，就存到 `~/.craft/workflows/`。

每个脚本由一段 metadata 和一段 JavaScript 正文组成：

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

`meta.name` 和 `meta.description` 必填，`whenToUse` 和 `phases` 可选。Metadata 只能写字面量，不接受 import、计算值和函数调用。正文支持顶层 `await`，返回值必须能 JSON 序列化。

保存后，这个脚本就成了一条 slash command，后面还能补充说明：

```text
/review-change 重点检查 src/ 下的认证改动
```

命令后面的文字会作为 `args` 传给脚本。工作区和个人目录有同名脚本时，工作区的那个优先。

## 编排 API

脚本里可以使用这组编排接口：

| API | 用途 |
|---|---|
| `agent(input, options?)` | 启动一个拥有全新上下文的原生 subagent，返回它的结果或 `null`。 |
| `parallel(thunks)` | 并行启动一组延迟调用，按声明顺序返回结果。 |
| `pipeline(items, ...stages)` | 并行处理多个 item，同时让每个 item 依次经过各个 stage。 |
| `phase(name, detail?)` | 记录一个具名的进度边界。 |
| `log(value)` | 为本次运行记录诊断数据。 |
| `args` | 读取本次调用的结构化输入。 |
| `budget` | 读取当前的运行限制和已用量。 |
| `cwd` | 读取工作区根目录。 |

每个 pipeline stage 拿到 `(previous, original, index)`。某个 stage 返回 `null` 时，这个 item 后面的 stage 不再执行。即使 subagent 完成的先后顺序不同，`parallel()` 和 `pipeline()` 的结果仍然保持输入顺序。

### 配置单次调用

`agent()` 支持这些选项：

| 选项 | 用途 |
|---|---|
| `label` | 为这次调用起一个稳定的名字，便于在运行记录里辨认。 |
| `phase` | 把这次调用归到某个 phase。 |
| `schema` | 要求结果符合指定的 JSON Schema。 |
| `model` | 覆盖子调用使用的模型。 |
| `effort` | 覆盖子调用的推理强度。 |
| `isolation` | 使用 `shared` 或受管 `worktree`。 |
| `agentType` | 选择一个原生 Agent 角色。 |

带 `schema` 的调用在校验通过后返回 JSON 值，不带 `schema` 时返回 subagent 的最终文本。调用被取消或遇到无法恢复的错误会返回 `null`，使用结果前先处理这种情况。

## 通过插件分发

启用的插件也可以提供工作流，放在插件根目录的 `workflows/`，或 manifest 的 `workflows` 字段指定的目录。插件命令始终带 namespace：

```text
/review-tools:review-change
```

Namespace 保证插件里的工作流不会顶掉你自己的同名脚本。

## 脚本只负责编排

JavaScript 正文只做调度，不能自己读文件、联网或启动进程。这些动作交给 `agent()`，subagent 的工具调用照常经过工作区边界和工具审批。

每次 `agent()` 调用都从全新的上下文开始，同时继承父对话的工作区、权限策略和模型默认值。写上 `isolation: "worktree"` 会为这次调用建一个受管 Git worktree。跑完后干净的 worktree 会被删除，有改动或 commit 的会保留下来供你检查，不会自动合并。

## 相关文档

- [Subagents](./subagents) — 只委派一件事时，单个 subagent 就够了
- [Agent 团队](./teams) — 成员需要在执行过程中互相协调时的另一种选择
- [插件与工具](./plugins-tools) — 把工作流打包进插件，分发给更多人
