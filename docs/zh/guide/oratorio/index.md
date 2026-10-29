# Oratorio

Oratorio 是 DotCraft 内置的项目看板。本地任务、GitHub 的 issue 与 pull request、GitLab 的 issue 与 merge request 都汇总到同一块 Board 上。你在这里把工作交给 Agent、跟踪运行、审阅结果，批准之后直接交付回代码托管平台，全程不用离开 [DotCraft Desktop](../../start/desktop)。

![DotCraft Desktop 中的 Oratorio 看板](https://github.com/DotHarness/resources/raw/master/dotcraft/oratorio/board.png)

## 从一个本地任务开始

不接任何代码托管平台也能先跑起来：

1. 在 DotCraft Desktop 中把一个 Git 仓库打开为项目。
2. 在侧边栏选择 **Oratorio**。
3. 选择 **新建本地任务**，填写要解决的问题，选择仓库与基础分支。
4. 打开任务卡，用任务顶部的按钮开始运行。
5. 在任务的动态里跟踪运行，就绪后直接在那里审阅。

Agent 的工作发生在 Oratorio 单独创建的工作树里，运行期间不会动你手上正在用的 checkout。从筛选任务到审阅、交付的完整流程见 [Oratorio 工作流](./workflow)。

## 接入 GitHub 与 GitLab

在 Board 上选择 **Connect GitHub** 或 **Connect GitLab**，引导流程会带你从凭据一路走到首次同步。[GitHub](./github) 用 GitHub App 接入，[GitLab](./gitlab) 用项目级 Token 接入。也可以[在会话中接入仓库](#在会话中使用-oratorio)。

Oratorio 默认不向代码托管平台写入任何内容，需要你显式启用。没有公网 Webhook endpoint 也不影响使用，手动同步和定时同步照常工作。项目与工作区的映射、Agent 执行策略和自动化都在[配置 Oratorio](./settings)里调整。

## 在会话中使用 Oratorio

把 Oratorio 接进会话，就能在对话里直接查看和推进任务。

1. 安装 Oratorio，打开插件详情，选择 **Connect** 连接当前工作区。

   ![在插件详情中连接 Oratorio](https://github.com/DotHarness/resources/raw/master/dotcraft/oratorio/app-connection.png)

2. 新建一个会话，从第一条消息起就能用上 Oratorio。

在这样的会话里，也可以直接让 Agent 帮你接入仓库。在仓库所在的项目中发送 `$oratorio-setup`，或者直接让 Agent 把仓库接入 Oratorio。Agent 会读取仓库的远程地址，和你确认同步与审查设置，然后保存连接。需要 Token 或私钥时，会话里会出现一张表单，填入的内容直接交给 Oratorio，Agent 看不到。

插件详情中的 **Disconnect** 会把 Oratorio 从这个工作区的所有会话中移除。完整的连接与授权流程见[应用连接](../connected-apps)。

## 本地运行与远程部署

本地模式下，第一次打开 Oratorio 时 Desktop 会启动随包提供的 Oratorio Server，不需要额外安装。

需要远程部署时，把 Desktop 连接到 [DotCraft Stack](../../self-hosting/server-deployment)。Board 与任务操作完全一致，只是服务器管理设置在远程连接下是只读的。

## 相关文档

- [Oratorio 工作流](./workflow) — 从 Board 上找到任务，到审阅、交付的完整流程
- [配置 Oratorio](./settings) — 来源连接、项目映射与 Agent 执行策略
