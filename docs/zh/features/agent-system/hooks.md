# 生命周期 Hooks

Hooks 让 DotCraft 在会话、prompt 或工具调用的关键时刻运行你的脚本。项目里那些每次都要做的动作，例如命令运行前先检查、会话开始时补充上下文、工具结束后审阅结果，都可以交给 hook 自动完成。

![会话时间线上的四个时刻：会话开始、工具运行前、工具完成后、本轮结束。来自用户配置、workspace 或插件的 hook 脚本挂到你选择的时刻上](/lifecycle-hooks-overview.svg)

## 什么时候使用 hooks

Hooks 适合做靠近 Agent 工作流的小型 guardrail 和可重复检查。

| 用途 | 适合的 hook 时机 |
|---|---|
| 危险 Shell 命令运行前提醒 | 工具运行前 |
| 新对话开始时加入项目提醒 | 会话开始时 |
| 文件编辑后格式化或 lint | 工具完成后 |
| 审阅最终 diff 或命令输出 | 本轮结束时 |
| 通知其他系统 | 工具或本轮完成后 |

脚本保持聚焦就好。逻辑一旦变复杂，把复杂的部分写成项目里的脚本，再从 hook 调用它。

## hooks 从哪里来

DotCraft 会从你的个人配置、当前 workspace 和已启用的插件里发现 hooks。这样私人偏好留在个人配置里，团队策略放进 workspace，可复用的 hook 则可以随插件一起安装。

hooks 会运行本地命令，所以需要你先信任。新发现的 hook 默认未信任，改动过的 hook 会重新变成待信任状态，直到你再次确认。插件带来的 hooks 按插件整包信任，你可以先展开看清它声明了什么，再决定放行。

hook 文件的结构、可用事件和写法示例见[配置参考](../../developing/configuration#automations-goals-与-hooks)。

## 在 Desktop 中管理 hooks

打开 **设置 → Hooks**，这里按来源列出所有已发现的 hooks。展开一条就能看到它的命令、匹配器、来源文件和信任状态。个人配置和 workspace 配置里的 hooks 可以直接在这里启用、停用和信任，不必去改来源文件。

配置文件始终是 hook 命令的权威来源，Desktop 只管理你的启用状态和信任状态。

## hook 安全

Hooks 的力量来自它能运行本地命令，风险也在这里。先从只观察、只打印上下文的 hook 开始，确认输出符合预期，再加入会阻断操作的行为。不要把密钥写进 workspace 文件，凭据一律走环境变量，插件 hooks 只装来源可信的。

## 相关文档

- [自动化与目标](./automations) — 需要按计划或手动跑完一整个任务，而不是挂在某个时刻上时用它
- [插件与工具](./plugins-tools) — 可以随插件分发和复用的 hooks
- [安全与沙箱](../self-hosted/security) — 文件、Shell 和沙箱行为的 guardrail
