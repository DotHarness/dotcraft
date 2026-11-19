# 记忆与技能

工作区记忆、梦境，以及技能的加载。概念见[记忆与梦境](../guide/memory)和[技能](../guide/skills)。

## 记忆

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Memory.Enabled` | 让新对话使用并维护记忆。`Memory.AutoExtract` 和梦境也依赖此开关 | `true` |
| `Memory.AutoExtract` | 每隔几次回复，检查启用记忆的新对话中有没有值得记住但还没保存的内容 | `false` |
| `Memory.Types` | 记忆的种类，每种包含 `Name` 和一句说明该类内容的 `Description`，一到八种 | `user`、`feedback`、`project`、`reference` |
| `Memory.SaveGuidance` | Agent 在回复中主动保存哪些内容，以及如何写一条记忆 | 内置指引 |
| `Memory.ExtractionGuidance` | 自动检查从最近的回复中保存哪些内容 | 内置指引 |
| `Memory.ConsolidationGuidance` | 梦境整理记忆时保留什么，以及如何合并、如何处理矛盾 | 内置指引 |
| `Memory.ExtractionInterval` | 自动检查每隔几次回复运行一次，取值 1 到 50 | `7` |

两个开关只影响修改之后新建的对话。其余配置从每个对话的下一次回复起生效，删除某项即恢复内置默认值。Memory Studio 插件为这些配置提供预设，并支持在保存前预览修改效果。

## 梦境

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Dreams.Enabled` | 按计划运行梦境来整理记忆，依赖 `Memory.Enabled` | `false` |
| `Dreams.Interval` | 一份记忆距上次整理至少要隔多久，才会进行计划运行 | `24:00:00` |
| `Dreams.MinSessionsSinceLastRun` | 一份记忆自上次整理以来，至少要有多少个对话用过它并完成一轮，才会进行计划运行 | `3` |
| `Dreams.AutoApply` | 之后每次成功运行的结果不经审阅直接应用，已在等待审阅的运行不受影响 | `false` |
| `Dreams.StartupDelay` | AppServer 启动后，第一次计划检查前的等待时间 | `00:05:00` |
| `ConsolidationModel` | 顶层配置项。梦境运行使用的模型，空值使用主模型 | 空 |

梦境会逐个整理工作区记忆，以及每个声明了 `memory: own` 的 Agent 预设自己的记忆。手动运行会跳过 `Dreams.Interval` 和 `Dreams.MinSessionsSinceLastRun`。

## 技能

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Skills.DisabledSkills` | 当前工作区禁用的 skill 名称列表。被禁用的 skill 仍留在磁盘上，但不会注入 Agent 上下文 | `[]` |
| `Skills.IncludeSharedSkills` | 同时读取与其他 agent 工具共用的 `.agents/skills` 目录：工作区及其上级目录（直到仓库根目录）中的，以及 `~/.agents/skills`。重启后生效 | `true` |
| `Skills.IncludeBuiltInSkills` | 启动时把 DotCraft 自带的 skill（如 `dotcraft-guide`）部署进工作区。自带 skill 的宿主可以关掉它。重启后生效 | `true` |
