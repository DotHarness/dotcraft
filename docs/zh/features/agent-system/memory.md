# 长期记忆与 Dreams

DotCraft 让 Agent 对你的项目有真正的记忆。你工作时，它会把值得记住的东西写进工作区里的明文 Markdown——跨会话、跨入口都记得住，而这些内容你随时可读、可改、可删。

## 三层记忆

| 层 | 写入者 | 记录什么 |
|---|---|---|
| **会话历史** | 引擎自动 | 每次会话的完整记录——每条消息、每个动作、每个结果 |
| **长期记忆** | Agent 在成功完成工作后维护 | 项目背景、你的偏好、近期决策、反复出现的问题 |
| **Dreams** | 后台循环 | 低权威的直觉——焦点话题、开放问题、需要避开的噪声 |

会话历史是你随时可以追溯的审计基线，长期记忆是 Agent 主动维护的高权威笔记，Dreams 是低权威的后台草稿。三者职责清晰、互不替代。想了解会话在磁盘上的具体存储方式，见 [统一会话核心](../../developing/architecture/session-core)。

## MEMORY.md / HISTORY.md：Agent 维护，你来审阅

启用长期记忆沉淀后，每完成若干轮成功对话，DotCraft 会让 Agent 把这一段对话里产生的稳定信息写回 `.craft/memory/MEMORY.md`，并把流水追加到 `HISTORY.md`。具体触发频率和记忆整合模型见 [配置完整参考](../../developing/configuration#workspace-memory-与-skills)。

写入采用 patch 风格：Agent 不会无差别覆盖整篇 MEMORY.md，而是在你或它已有的笔记里做精确插入或修改。这意味着你可以放心手动编辑这两份文件，Agent 下次会读你修改过的版本。

> [!TIP]
> 想"重置项目记忆"时，Desktop 的 **设置 → 个性化 → 重置记忆** 会一次性清空 `MEMORY.md`、`HISTORY.md`、`.craft/dreams/` 和派生缓存；不会删除会话、配置、技能或自动化任务。

## Dreams：后台被动记忆整理

Dreams 是 DotCraft 的"后台思考循环"。当 AppServer 运行时（无论你是否在主动对话），它会定期扫描近期工作区活动，生成一份**低权威**的被动项目记忆 store——可理解为一份"草稿笔记"，用户审阅后才会进入后续会话上下文。

![Dreams 审阅流程](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/dreams.gif)

![DotCraft memory lifecycle topology](/memory-lifecycle-topology.svg)

| 状态 | 含义 |
|---|---|
| **pending** | 自动生成、尚未进入上下文，等待审阅 |
| **active** | 用户审阅通过，进入后续会话的低权威上下文 |
| **discarded / archived** | 历史归档，不再参与上下文 |

Desktop 的 **设置 → 个性化 → Dreams** 提供：

- **立即运行** — 强制触发一次 Dream 整理
- **自动更新梦境** — 关闭时新 Dream 仅作为 pending；开启后未来成功运行自动应用为 active
- **管理梦境** — 列出所有 Dream，每条可跳到 Dashboard 完成 diff / trace / 应用 / 丢弃 / 取消 / 归档

Dreams 不是 `MEMORY.md` 的替代，是补充。它适合保留"近期焦点"和"需要避开的低信号上下文"——这些信息以高权威记忆形式记下来反而过强、写在临时会话里又会丢失。

## 相关文档

- [项目级工作区](../project-first) — `.craft/` 整体结构
- [Skills 与自学习](./skills) — 把成功流程沉淀为可复用 skill
- [Observability](../self-hosted/observability) — 在 Dashboard 审阅 Dreams、查看 Trace
- [配置完整参考](../../developing/configuration) — `Memory.*` / `Compaction.*` 字段
