# Oratorio

Oratorio 是 DotCraft 内置的项目看板，用于管理本地任务、GitHub issue 与 pull request，以及 GitLab issue 与 merge request。你可以直接在 DotCraft Desktop 中把工作交给 Agent、跟踪运行、审阅结果并交付已批准的改动。

![DotCraft Desktop 中的 Oratorio 看板](https://github.com/DotHarness/resources/raw/master/dotcraft/oratorio/board-light.png)

## 从本地任务开始

1. 在 DotCraft Desktop 中把一个 Git 仓库打开为项目。
2. 在侧边栏选择 **Oratorio**。
3. 选择 **New local task**，填写问题并选择仓库与基础分支。
4. 打开任务卡，在 **Start Agent work** 下选择当前可用的操作。
5. 在 Quick View 中跟踪运行，或在任务进入审阅状态后打开完整详情。

Oratorio 会为 Agent 工作创建隔离的托管 Worktree。运行期间不会修改你当前使用的 checkout。

## 接入外部工作

在 Oratorio 设置中添加项目，即可从代码托管平台同步工作：

- 使用 GitHub App [接入 GitHub](./oratorio/github)。
- 使用项目级 Token [接入 GitLab](./oratorio/gitlab)。

只有显式启用后，Oratorio 才会向代码托管平台写入内容。没有公网 Webhook endpoint 时，手动同步和定时同步仍然可用。

## 在会话中使用 Oratorio

1. 安装 Oratorio，打开插件详情，然后选择 **Connect**，连接当前 workspace。

   ![在插件详情中连接 Oratorio](https://github.com/DotHarness/resources/raw/master/dotcraft/oratorio/app-connection-light.png)

2. 打开目标会话，选择 **Apps**，然后启用 Oratorio。

   ![在会话的 Apps 选择器中启用 Oratorio](https://github.com/DotHarness/resources/raw/master/dotcraft/oratorio/thread-app-light.png)

在 **Apps** 中关闭 Oratorio 只影响当前会话。插件详情中的 **Disconnect** 会撤销当前 workspace 的连接及相关会话绑定。完整的连接与授权流程请参阅 [Connected Apps](./agent-system/connected-apps)。

## 本地与远程使用

在本地模式下，Desktop 会在你首次打开功能时启动随包提供的 Oratorio Server。DotCraft Hub 管理该进程，Oratorio 的用户级状态保存在 `~/.craft/oratorio/`。

远程部署通过 [DotCraft Stack](./self-hosted/server-deployment) 连接。Board 与任务操作保持一致，但通过远程 Desktop 连接时，服务器管理设置为只读。

## 相关文档

- [使用 Oratorio 工作流](./oratorio/workflow)
- [配置 Oratorio](./oratorio/settings)
- [Connected Apps](./agent-system/connected-apps)
- [部署 DotCraft Stack](./self-hosted/server-deployment)
- [Desktop](./entry-points/desktop)
