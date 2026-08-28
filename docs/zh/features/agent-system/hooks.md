# 生命周期 Hooks

Lifecycle Hooks 让 DotCraft 在会话、prompt 或工具调用的关键时刻运行你的脚本。适合把项目里的固定流程自动接到 Agent 工作流里，例如命令运行前检查、会话开始时补充上下文，或工具结束后审阅结果。

![会话时间线上的四个时刻：会话开始、工具运行前、工具完成后、本轮结束；来自用户配置、workspace 或插件的 hook 脚本挂到你选择的时刻上](/lifecycle-hooks-overview.svg)

## 什么时候使用 hooks

Hooks 适合做靠近 Agent 工作流的小型 guardrail 和可重复检查。

| 用途 | 适合的 hook 时机 |
|---|---|
| 危险 Shell 命令运行前提醒 | 工具运行前 |
| 新 Thread 开始时加入项目提醒 | 会话开始时 |
| 文件编辑后格式化或 lint | 工具完成后 |
| 审阅最终 diff 或命令输出 | 本轮结束时 |
| 通知其他系统 | 工具或 turn 完成后 |

脚本应保持聚焦。如果逻辑变复杂，把复杂部分放进项目脚本，再从 hook 调用它。

## hooks 从哪里来

DotCraft 可以从个人配置、当前 workspace 和已启用插件发现 hooks。这样你可以把私人偏好留给自己，把团队策略放进 workspace，也可以通过插件安装可复用的 hook bundle。

会运行本地命令的 hooks 需要信任。新发现的 hook 默认未信任。hook 修改后会变成已修改，直到你重新信任。插件 hooks 会作为一个插件能力包一次性信任，你可以先检查插件声明的 hooks，再允许当前这一组 hooks 运行。

## 在 Desktop 中管理 hooks

打开 **Settings -> Hooks**，可以看到按来源分组的所有 hooks。

在这个页面里，你可以：

- 查看 hook 来自用户配置、workspace 配置还是插件。
- 展开 hook，检查命令、matcher、来源文件和信任状态。
- 不编辑来源文件，启用或停用用户配置和 workspace 配置里的 hooks。
- 在新增或修改后，信任用户配置或 workspace 配置里的 hook。
- 对插件提供的当前 hooks 使用一次 **信任 hooks** 操作。
- 信任插件前，可以展开单个 plugin hook 检查它声明的内容。

配置文件仍然是 hook 命令的权威来源。Desktop 只管理你的个人启用状态和信任状态。

## hook 安全

Hooks 很强大，因为它们会运行本地命令。建议先从只观察、只输出上下文的 hook 开始。确认输出清楚后，再加入阻塞行为。不要把密钥写进 workspace 文件，凭据优先使用环境变量，只安装你信任来源的插件 hooks。

## 相关文档

- [配置完整参考](../../developing/configuration#automations-goals-与-hooks) — hook 文件结构、事件、状态和示例
- [插件与工具](./plugins-tools) — 可分发可复用 hooks 的插件
- [安全与沙箱](../self-hosted/security) — 文件、Shell 和沙箱行为的 guardrail
- [Lifecycle Hooks 规范](https://github.com/DotHarness/dotcraft/blob/master/specs/features/lifecycle-hooks.md) — 面向实现者的工程契约
