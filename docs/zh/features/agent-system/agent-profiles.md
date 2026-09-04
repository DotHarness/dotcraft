# Agent 预设

Agent Profile 把一套专门用途的 Agent 设定保存下来，需要时随时取用。通过 Agent Builder 的对话完成定制后，就能复用它的角色指令、默认模型、工具、技能、MCP 访问范围和审批方式。

![DotCraft Agent 预设](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/agent-profile.gif)

可以先打造一个专门 Agent，也可以逐步组建完整的 Agent 团队：让 Explorer 负责调查、Builder 负责实现、Operator 处理 App 工作流。每个角色都是独立的 Profile，需要时按工作选择即可。

## 在哪里用得上

| 位置 | 用途 |
|---|---|
| 对话 | 在输入框里选一个已保存的 Agent，这段对话就以对应的角色和能力运行。 |
| [自动化与目标](./automations) | 给任务绑定预设，定时或手动运行时只使用这个 Agent 的工具、技能和模型。 |

预设在对话或任务启动时生效。已经跑起来的对话沿用启动时的那份设定，直到你手动刷新它，或者用改过的预设开一段新对话、新任务。

## 内置 Profiles

DotCraft 为你的 Agent 团队提供五个起点。可以直接使用，也可以在 Agent Builder 中按照项目需要继续调整。

| Profile | 适合的工作 |
|---|---|
| <img src="/leader.svg" alt="Leader Agent Profile" width="64" height="64"> **Leader** | 规划复杂工作、委派给专门角色、验证结果并整合交付。 |
| <img src="/explorer.svg" alt="Explorer Agent Profile" width="64" height="64"> **Explorer** | 调查陌生系统、消除未知，并在不改变状态的前提下提供证据。 |
| <img src="/builder.svg" alt="Builder Agent Profile" width="64" height="64"> **Builder** | 实现范围明确的改动并验证结果。 |
| <img src="/reviewer.svg" alt="Reviewer Agent Profile" width="64" height="64"> **Reviewer** | 独立检查正确性、风险、测试覆盖和可维护性。 |
| <img src="/operator.svg" alt="Operator Agent Profile" width="64" height="64"> **Operator** | 操作 App、浏览器、MCP server 和工作流，并明确控制外部副作用。 |

## Agent Builder

![DotCraft Agent Builder](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/agent-builder.gif)

Agent Builder 让你用聊天的方式定制自己的 Agent。可以从内置 Profile 开始，也可以直接描述一个新角色，再在引导式对话里调整它的指令、工具、技能、模型和审批方式。

需要精确控制时，也可以直接编辑结构化的预设。Agent Builder 和编辑器改的是同一份草稿，所以对话始终围绕最终保存下来的那份定义展开。

## 相关文档

- [自动化与目标](./automations) — 让定时任务以某个预设的身份运行
- [Subagents](./subagents) — 从当前对话里做一次性委派，不必先建预设
