# 入口总览

同一个工作区可以从好几个界面打开：桌面应用、终端、编辑器，还有聊天群里的机器人。无论从哪里进来，面对的都是同一个 Agent。它读同一份 `.craft/`，共享同一批会话和同一份长期记忆，变的只是你在哪种界面里和它说话。

![Desktop、CLI、编辑器和渠道机器人都连到同一个 AppServer 与共享的会话核心](/entry-points-topology.svg)

## 四种入口

| 入口 | 界面形态 | 适合 |
|---|---|---|
| [Desktop](./desktop) | 图形化桌面应用 | 第一次使用、长期协作、需要逐项审阅 diff 和审批 |
| [CLI](../../getting-started) | 一次性命令 | 脚本、SSH、CI 和轻量任务 |
| [IDE / 编辑器（ACP）](./editors) | 嵌在 JetBrains、Obsidian、Unity 等编辑器里 | 让 Agent 读到未保存的改动，用编辑器自己的终端和 diff 视图 |
| [Channels 与 Bots](./channels) | QQ、企业微信、飞书、Telegram、微信 | 团队群聊、知识库机器人、客服机器人 |

## 怎么挑

第一次用 DotCraft 就从 Desktop 开始。按[快速开始](../../getting-started)装好、选好工作区、跑通第一次对话，之后再按真实需要打开第二个入口。

在远程服务器上、在 CI 里，或者只想跑一条命令拿到结果，用 `dotcraft exec` 这类[命令行任务](../../developing/lifecycle/appserver)。想让 Agent 看到你还没保存的改动，并在编辑器自己的 diff 视图里逐项批准，用 ACP。想让一个群随时能问到项目上的事，接一个渠道机器人。想自己写客户端，就照着 [SDK](../../developing/sdks/) 写，它一样连到这个工作区。

## 换个入口，工作照旧

会话不属于任何一个入口。在 Desktop 开始的会话，可以在编辑器或另一个客户端里接着聊，审批则按当前平台原生的方式弹出来。背后是同一套[会话核心](../../developing/architecture/session-core)。

配置也只有一份。模型、安全策略和自动化都写在工作区的 `.craft/config.json` 和个人的 `~/.craft/config.json` 里，所有入口读同一份。一个工作区始终只跑一个 AppServer，本机由 [Hub](../../developing/lifecycle/hub) 自动协调，不用你管。ACP、Dashboard、自动化和外部渠道则按工作区各自启用，用得上再打开。
