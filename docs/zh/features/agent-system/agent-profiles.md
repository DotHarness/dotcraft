# Agent Profiles

Agent Profiles 让你保存一个专门用途的 DotCraft 智能体，并在合适的工作流里反复使用。一个 Profile 可以带有角色指令、默认模型、工具与技能选择、MCP 访问范围和审批行为。

![DotCraft 个性化智能体](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/agent-profile.gif)

当你需要一个可复用的专门 Agent 时，就适合使用 Profile：例如偏重阅读和检查的 Reviewer、负责实现的 Builder、处理 App 工作流的 Operator，或 Teams 中保持固定职责的队友。

## 在哪里使用

| 位置 | 用途 |
|---|---|
| 会话 | 在输入框里选择已保存的智能体，让当前 Thread 以对应角色和能力集运行。 |
| Teams | 给 Team 成员绑定 Profile，让每位队友保持稳定的角色、风格和工具边界。 |
| Automations | 给任务绑定已保存的智能体，让计划任务或手动运行使用该 Agent 的工具、技能和模型。 |

Profile 会在线程或任务启动时解析。已经使用 Profile 创建的 Thread 会保留当时的运行快照，直到你显式刷新，或用更新后的 Profile 启动新的线程或任务。

## Agent Builder

![DotCraft Agent Builder](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/agent-builder.gif)

Agent Builder 让你用对话定制自己的智能体。描述你想要的 Agent，再通过引导式对话调整它的指令、工具、技能、模型和审批方式。

需要精确控制时，你仍然可以直接编辑结构化 Profile。Agent Builder 和编辑器操作的是同一份草稿，所以对话始终围绕最终会保存的智能体定义展开。

## 相关文档

- [Teams](./teams) — 按角色协作的多 Agent Mission。
- [Automations 与 Goals](./automations) — 为自动化任务绑定已保存的智能体。
- [SubAgents](./subagents) — 从已有对话中进行一次性委派。
