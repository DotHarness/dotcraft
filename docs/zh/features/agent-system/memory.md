# 长期记忆与 Dreams

DotCraft 可以把有用的项目背景带到后续会话中。需要长期保留的记忆会以明文 Markdown 存在工作区里，你可以随时阅读、编辑或删除。

![DotCraft 记忆使用流程](/memory-lifecycle-topology.svg)

## DotCraft 记忆信息的三种方式

| 类型 | 主要用途 |
|---|---|
| **会话历史** | 重新打开或继续之前会话中的工作 |
| **已保存的记忆** | 复用稳定的项目背景、偏好、决策和反复出现的问题 |
| **Dreams** | 保留关于近期重点、开放问题和低信号信息的暂定笔记 |

会话历史保留每个会话中的工作。已保存的记忆记录跨会话仍有用的信息。Dreams 提供暂定的背景笔记，不会取代前两者。

## MEMORY.md 与 HISTORY.md

启用已保存的记忆后，DotCraft 会定期把有长期价值的信息更新到 `.craft/memory/MEMORY.md`，并在 `HISTORY.md` 中添加一条简短记录。具体触发频率和模型见[配置完整参考](../../developing/configuration#workspace-memory-与-skills)。

你可以检查和编辑这两个文件。DotCraft 会在下次更新记忆前读取当前内容，因此你的修改会成为它后续使用的记忆的一部分。

> [!TIP]
> 想"重置项目记忆"时，Desktop 的 **设置 → 个性化 → 重置记忆** 会一次性清空 `MEMORY.md`、`HISTORY.md`、`.craft/dreams/` 和派生缓存，但不会删除会话、配置、技能或自动化任务。

## Dreams

Dreams 会在后台查看近期工作区活动，即使你当前没有在对话。它会整理出可供后续会话参考的暂定笔记，但不会把这些笔记当作指令或已经确认的事实。

Dreams 默认关闭。请在 Desktop 的 **设置 → 个性化 → Dreams** 中开启 **启用 Dreams**，主动为当前工作区启用该功能。启用后，成功的 Dreams 运行会等待你审阅，之后 DotCraft 才会使用结果。开启 **自动更新梦境** 后，未来成功的运行会跳过人工审阅并自动可用。之前已经处于 pending 的运行不会被自动应用。

![Dreams 审阅流程](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/dreams.gif)

| 状态 | 含义 |
|---|---|
| **pending** | 等待审阅，尚未用于会话 |
| **applied** | 审阅通过或由自动更新应用，可供后续会话使用 |
| **discarded / archived** | 不再用于后续会话 |

Desktop 的 **设置 → 个性化 → Dreams** 提供：

- **启用 Dreams** — 为当前工作区开启后台 Dreams
- **立即运行** — 立即开始一次 Dreams 更新
- **自动更新梦境** — 让未来成功的运行跳过人工审阅并自动可用
- **管理梦境** — 查看近期运行，并应用、丢弃、取消或归档

Dreams 不会取代已保存的记忆。需要 DotCraft 可靠使用的事实和偏好应写入 `MEMORY.md`。Dreams 只是辅助笔记，仍可能需要更正或删除。

## 相关文档

- [Skills 与自学习](./skills) — 把成功流程沉淀为可复用 skill
- [Observability](../self-hosted/observability) — 在 Dashboard 审阅 Dreams、查看 Trace
- [配置完整参考](../../developing/configuration) — `Memory.*` / `Compaction.*` 字段
- [会话持久化](../../developing/architecture/session-persistence)
