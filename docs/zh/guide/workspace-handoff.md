# 外部 Agent 协作

有些工作最后要交给 DotCraft 之外的 coding agent 继续。DotCraft 可以把一个会话导出成一份 Markdown 交接文档：项目记忆、会话记录和相关证据都在里面，对方不必从零散的聊天片段里猜。

![把 DotCraft 会话导出成一份 Markdown 交接文档：定位会话、导出、重放整理，再把文件原样交给外部 coding agent](/workspace-handoff-flow.svg)

Claude Code 可以直接把任务交给 DotCraft 并拿回结果，见[在 Claude Code 里把任务交给 DotCraft](#在-claude-code-里把任务交给-dotcraft)。其他能接入 DotCraft 的工具作为客户端连上来，见 [AppServer 模式](../contribute/lifecycle/appserver)。导出文档适用于只能接收文件或粘贴内容的 coding agent。

## 该交给对方什么

一次够用的交接通常包含四样东西：

- 仓库本身，或你允许对方读取的具体文件。
- 相关会话的导出文档。
- 你希望对方接下来完成的具体任务。
- 隐私约束，例如工具输出和记忆文件能不能带上。

不要交出 Provider 凭据、全局配置 `~/.craft/config.json`，或工作区里的原始数据库文件。Markdown 导出比原始状态更容易裁剪和检查，但它不会自动变得安全。

## 找到要导出的会话

只记得症状、报错、工具名或某句话时，先搜索工作区：

```bash
dotcraft context search --query "provider timeout gpt-5.3" --workspace "D:\path\to\project" --limit 5
```

结果列出匹配的会话，能导出的会直接附上导出命令，复制就能用。搜索负责定位，内容以导出为准。要让脚本消费结果，加上 `--json`。

## 导出交接文档

拿到 thread id 后：

```bash
dotcraft context export --thread thread_20260601_ab12cd --workspace "D:\path\to\project" --output handoff.md
```

默认输出就是按交接场景准备的：工具结果保留摘要而不是完整输出。想更保守，把工具结果整个去掉：

```bash
dotcraft context export --thread thread_20260601_ab12cd --tool-results none --output handoff.md
```

想要最完整的一份 transcript：

```bash
dotcraft context export --thread thread_20260601_ab12cd --profile transcript --tool-results full --output transcript.md
```

不写 `--output` 时，Markdown 直接输出到 stdout。

> [!NOTE]
> `--tool-results full` 只是取消导出时的截断上限。体积过大、已经另存到 `.craft/tool-results/` 的原始结果不会被取回，导出里仍然只有当时记录的预览。

## 导出文档包含什么

一份导出里有会话的基本信息、工作区记忆、模型当前实际看到的上下文，以及会话记录本身。它不是把日志原样倒出来：已经回滚掉的内容不会出现，导出的是这个会话真正会继续使用的上下文。

推理内容和内部 Provider 数据不会导出。工具调用会保留，工具结果和你回答 Agent 提问时填写的内容都按 `--tool-results` 的范围处理。

## 发出前先审阅

导出不做任何脱敏。记录里出现过的内容都会原样进入文档，包括工具结果里带出的密钥和令牌。需要收窄范围就用 `--tool-results none` 或 `summary` 导出。发出前打开 Markdown 通读一遍，确认里面没有不该出去的东西，再告诉对方接下来做什么、哪些文件可以改。

## 在 Claude Code 里把任务交给 DotCraft

装上 DotCraft 的 Claude Code 插件后，Claude 可以把任务交给 DotCraft，结果回到同一个对话。DotCraft 用你在 DotCraft 里配置的模型（比如 GPT 模型）和它自己的工具完成任务，包括生成图片。任务是一个普通的 DotCraft 会话，你可以在 Desktop 里查看进度或接着聊。

先把项目文件夹设为 DotCraft 工作区，并让 `dotcraft` 命令在 PATH 上，然后在 Claude Code 里安装插件：

```text
/plugin marketplace add DotHarness/dotcraft-claude-code
/plugin install dotcraft@dotcraft
/dotcraft:setup
```

`/dotcraft:setup` 会告诉你还缺什么。之后用 `/dotcraft:task` 交出任务，用 `/dotcraft:image` 生成图片，用 `/dotcraft:cancel` 停掉还在运行的任务，也可以直接让 Claude 请 DotCraft 去做。每个回答都会标明来自 DotCraft，并附上会话 id，方便继续追问。生成的图片保存在项目的 `.craft/generated_images` 文件夹下。

DotCraft 会直接执行项目内的普通命令和改动，凡是需要你审批的操作都会拒绝，除非任务里加了 `--auto-approve`。插件底层调用的是 `dotcraft exec` 命令，见[命令行参考](../contribute/lifecycle/appserver)。

## 相关文档

- [Subagents](./subagents) — 想让外部 coding CLI 直接在 DotCraft 里跑，用 subagent 的外部运行时
- [可观测性](../self-hosting/observability) — 在 Dashboard 里回看会话轨迹，确认要导出哪一段
