# 使用 DotCraft

DotCraft 的核心是一个随项目持续演进的 Agent。这一组页面介绍如何使用它：对话里能做什么、如何塑造 Agent 本身、哪些工作可以无人值守地推进，以及 Agent 能用上哪些工具和应用。最后两部分把同一个 Agent 带进团队群聊和项目看板。

![DotCraft Agent 系统总览](/agent-system-overview.svg)

## 让对话做更多事

在 Desktop 中，回复里可以直接放上计算器、方案对比或小测验这样的[交互式回答](./interactive-answers)，或者图表、模拟演示、设计稿这样的[交互式可视化](./visualizations)。[Subagents](./subagents)在独立上下文中执行范围明确的任务，保持主对话简洁。需要交给 DotCraft 之外的 coding agent 接手时，[外部 Agent 协作](./workspace-handoff)可以把会话导出为一份交接文档。反过来，[从其他 Agent 导入](./session-import)能把 Claude Code、ChatGPT 和 Cursor 在同一项目里的会话带进 DotCraft。

## 塑造你的 Agent

[Agent 预设](./agent-profiles)为可复用的专门 Agent 保存指令、工具、技能和默认模型，Agent Builder 则让你通过对话完成定制。[技能](./skills)把验证过的工作流程沉淀为可复用的技能，同类任务直接复用。[记忆与梦境](./memory)让 Agent 跨会话保留项目背景、偏好和决策。记忆以明文 Markdown 保存在工作区里，可以随时查看和修改。[生命周期 Hooks](./hooks) 在会话的关键时刻自动运行你的脚本，例如在危险命令执行前先行确认。

## 无人值守运行

[自动化与目标](./automations)让例行任务按计划自动执行，也可以为一段对话设定持续推进的长期目标。[动态工作流](./dynamic-workflows)把可重复的编排保存成脚本，并行运行多个 subagent。

## 扩展能力

[插件与工具](./plugins-tools)介绍内置工具、插件和 MCP server 如何为 Agent 提供能力，以及各自的信任边界。需要更多插件时，[插件市场](./plugin-marketplaces)用于添加你信任的插件目录，[应用连接](./connected-apps)则让会话直接使用你已经在用的产品和服务。[DotCraft 卫星](./satellite)提供远程工具调用，由电脑主人决定访问权限，并随时暂停共享。

## 在团队群聊里提问

[社交渠道](./channels/)把同一个 Agent 接进 QQ、企业微信、飞书 / Lark、Telegram 和微信，同事在群里问一句就能拿到答案，不必打开 Desktop。

## 在一块看板上推进项目

[Oratorio](./oratorio/) 把本地任务、GitHub 的 issue 与 pull request、GitLab 的 issue 与 merge request 汇总到同一块 Board 上，你在这里把工作交给 Agent、跟踪运行并审阅结果。
