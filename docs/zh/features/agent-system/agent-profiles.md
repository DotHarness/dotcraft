# Agent 预设

预设把一套专门用途的 Agent 设定保存下来，需要时随时取用。一份预设可以带上角色指令、默认模型、工具与技能选择、MCP 访问范围和审批方式。

![DotCraft Agent 预设](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/agent-profile.gif)

需要一个反复用得上的专门 Agent 时，就把它存成预设：偏重阅读和检查的 Reviewer、负责实现的 Builder、处理 App 工作流的 Operator，或者在团队里长期扮演固定职责的队友。

## 在哪里用得上

| 位置 | 用途 |
|---|---|
| 对话 | 在输入框里选一个已保存的 Agent，这段对话就以对应的角色和能力运行。 |
| [Agent 团队](./teams) | 给队友绑定预设，让每位成员保持稳定的角色、风格和工具边界。 |
| [自动化与目标](./automations) | 给任务绑定预设，定时或手动运行时只使用这个 Agent 的工具、技能和模型。 |

预设在对话或任务启动时生效。已经跑起来的对话沿用启动时的那份设定，直到你手动刷新它，或者用改过的预设开一段新对话、新任务。

## Agent Builder

![DotCraft Agent Builder](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/agent-builder.gif)

Agent Builder 让你用聊天的方式定制自己的 Agent。先说清楚它该做什么，再在引导式对话里调整它的指令、工具、技能、模型和审批方式。

需要精确控制时，也可以直接编辑结构化的预设。Agent Builder 和编辑器改的是同一份草稿，所以对话始终围绕最终保存下来的那份定义展开。

## 相关文档

- [Agent 团队](./teams) — 按角色协作的多 Agent Mission，每位队友都可以绑定预设
- [自动化与目标](./automations) — 让定时任务以某个预设的身份运行
- [Subagents](./subagents) — 从当前对话里做一次性委派，不必先建预设
