# 长期记忆与 Dreams

DotCraft 不靠模型自带的"对话窗口"假装记忆。它把会话中沉淀下来的稳定信息写到工作区里两份明文 Markdown，让 Agent 跨会话、跨入口都能读得到，也让你随时可读、可改、可删。

## 三层记忆

| 层 | 存放 | 写入者 | 用途 |
|---|---|---|---|
| **会话历史** | `.craft/threads/` | 引擎自动 | 单次会话的完整 Thread/Turn/Item 时序 |
| **长期记忆 / 历史** | `.craft/memory/MEMORY.md`、`HISTORY.md` | Agent 在成功回合后维护 | 项目背景、用户偏好、近期决策、反复出现的问题 |
| **Dreams** | `.craft/dreams/` | 后台整理流程 | 低权威被动上下文（焦点话题、开放问题、需要避免的低信号内容） |

会话历史是审计基线，长期记忆是 Agent 主动维护的高权威笔记，Dreams 是低权威的"后台沉淀"。三者职责清晰、互不替代。

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

## 不同记忆方式的差异

| 方式 | 是不是黑箱 | 用户可读 | 用户可改 | 触发时机 |
|---|---|---|---|---|
| 模型上下文窗口 | 是 | 否 | 否 | 每次请求 |
| 向量索引 / RAG | 部分 | 通常否 | 通常否 | 检索时 |
| **DotCraft MEMORY.md** | 否 | 是（Markdown） | 是 | 每 N 轮 |
| **DotCraft Dreams** | 否 | 是（Markdown） | 是 | 后台周期性 |

把所有记忆放在工作区可读 Markdown 里，是 DotCraft 在"记忆可信度"上的核心选择：**你不能信任一份你看不到的记忆**。

## 何时用什么

| 场景 | 推荐 |
|---|---|
| 临时记下"这次会话用 Python 3.11" | 不写记忆，留在会话本地即可 |
| 记下"这个项目用 pnpm，禁止用 npm" | 写入 `MEMORY.md`（让 Agent 主动 patch 即可） |
| 想让 Agent 周期性自检"近期我们都在解决什么" | 启用 Dreams |
| 想清空所有沉淀重新开始 | Desktop "重置记忆" |
| 团队共享项目约定 | 把 `MEMORY.md` 提交进仓库 |

## 隐私与本地化

`.craft/memory/` 与 `.craft/dreams/` 全部位于工作区目录内，DotCraft 不会主动把这些内容上传到任何外部服务（除非你把工作区放在被同步的目录下）。如果你需要更强约束，可以：

- 把 `.craft/memory/`、`.craft/dreams/` 加入 `.gitignore`，让它们只留在本机；
- 在 [安全配置](../self-hosted/security) 里禁用相关工具，限制 Agent 写入工作区外路径。

## 相关入口

- [项目级工作区](../project-first) — `.craft/` 整体结构
- [Skills 与自学习](./skills) — 把成功流程沉淀为可复用 skill
- [Observability](../self-hosted/observability) — 在 Dashboard 审阅 Dreams、查看 Trace
- [配置完整参考](../../developing/configuration) — `Memory.*` / `Compaction.*` 字段
