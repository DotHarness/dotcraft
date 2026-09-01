# Agent 系统

DotCraft 的核心是一个随项目持续演进的 Agent。这一组页面介绍它的四类能力：扩展它能做的事、保留跨会话的上下文、把工作分派给更多 Agent，以及在无人值守时继续推进工作。

![DotCraft Agent 系统总览](/agent-system-overview.svg)

## 扩展能力

[技能与自学习](./skills)把验证过的工作流程沉淀为可复用的技能，同类任务直接复用。[插件与工具](./plugins-tools)介绍内置工具、插件和 MCP server 如何为 Agent 提供能力，以及各自的信任边界。[Remote Tool Host](./remote-tool-host)无需迁移 Agent 会话，就能在另一台设备的工作区中运行符合条件的内置工具。需要更多插件时，[插件市场](./plugin-marketplaces)用于添加你信任的插件目录，[应用连接](./connected-apps)则让会话直接使用你已经在用的产品和服务。

## 保留上下文

[长期记忆与梦境](./memory)让 Agent 跨会话保留项目背景、偏好和决策。记忆以明文 Markdown 保存在工作区里，可以随时查看和修改。

## 分派工作

[Subagents](./subagents) 在独立的上下文中执行你交派的任务，保持主对话简洁。[Agent 团队](./teams)让多个 Agent 协同完成同一项工作。[动态工作流](./dynamic-workflows)用可复用的编排脚本在后台并行处理大任务。[Agent 预设](./agent-profiles)用于创建具有不同专长设定的 Agent，供上述场景复用。需要交给 DotCraft 之外的 coding agent 接手时，[外部 Agent 协作](./workspace-handoff)可以把会话导出为一份交接文档。

## 无人值守运行

[自动化与目标](./automations)让例行任务按计划自动执行，也可以为一段对话设定持续推进的长期目标。[生命周期 Hooks](./hooks) 在会话的关键时刻自动运行你的脚本，例如在危险命令执行前先行确认。
