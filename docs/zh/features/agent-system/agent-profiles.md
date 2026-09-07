# Agent 预设

Agent Profile 把一套专门用途的 Agent 设定保存下来，需要时随时取用。通过 Agent Builder 的对话完成定制后，就能复用它的角色指令、默认模型、工具、技能、MCP 访问范围和审批方式。

![DotCraft Agent 预设](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/agent-profile.gif)

可以先打造一个专门 Agent，也可以逐步组建完整的 Agent 团队：让 Explorer 负责调查、Builder 负责实现、Operator 处理 App 工作流。每个角色都是独立的 Profile，需要时按工作选择即可。

DotCraft 根据 Profile 名称生成 avatar，因此同一个名称在各处保持相同的视觉身份。

名称支持中文、空格及其他 Unicode 字符（1–240 个字符，不含控制字符），去除首尾空白后按 NFC 规范化，保留大小写。显示、查找和头像使用同一个名称。文件名由程序安全生成，不需要填写额外 ID。

在桌面端选择 **Agents → New agent**，即可同时打开编辑器和 Builder 会话。你可以在会话中描述需求，也可以直接填写 Profile，然后点击 **Create** 保存。要从现有角色开始，可在 Agents 列表中选择内置模板。

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
| **Night Shift** | 处理夜间交接任务，准备晨间摘要。 |
| **Inbox Triage** | 整理提供的邮件并起草回复。 |
| **Chief of Staff** | 整理团队计划，提请用户作出决策。 |
| **Negotiator** | 研究价格并起草协商信息。 |
| **Prototyper** | 将想法实现为可运行的原型。 |
| **Researcher** | 研究问题并给出证据。 |
| **Lookout** | 检查网页并报告变化。 |
| **Competitor Watcher** | 跟踪竞品定价与发布，汇总简报。 |

## Agent Builder

![DotCraft Agent Builder](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/agent-builder.gif)

Agent Builder 让你用聊天的方式定制自己的 Agent。可以从内置 Profile 开始，也可以直接描述一个新角色，再在引导式对话里调整它的指令、工具、技能、模型和审批方式。

需要精确控制时，也可以直接编辑结构化的预设。Agent Builder 和编辑器改的是同一份草稿，所以对话始终围绕最终保存下来的那份定义展开。

## 相关文档

- [自动化与目标](./automations) — 让定时任务以某个预设的身份运行
- [Subagents](./subagents) — 从当前对话里做一次性委派，不必先建预设
