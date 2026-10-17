# Oratorio 工作流

[Oratorio](./) Board 上的每个任务都沿同一条路径推进：接收、交给 Agent、审阅，最后记录一个决策。

## 在 Board 上找到任务

**Active** 视图按生命周期阶段展示进行中的任务。**All**、**Cancelled** 和 **Archived** 用来翻已完成或不再活跃的工作。

- 搜索标题和描述，或者用 `source:` 与 `label:` 缩小范围。
- 按仓库或 Assignee 筛选。
- 选择 **Sync sources**，立刻向 GitHub 和 GitLab 请求一次更新。
- 选择任务卡，会在 Board 旁边打开这个任务，当前的 Board 筛选条件不会丢。

任务面板显示描述、任务属性和动态，顶部是此刻最该做的那个操作。选择 **完整详情** 可以把同一个任务铺满整个视图。

## 启动并跟踪 Agent 工作

打开一个待处理的任务，用顶部的按钮开始运行，按钮菜单里是其他运行方式。可选项取决于任务来源。

每次运行都在 Oratorio 管理的 Worktree 里进行。想看完整对话、计划、工具调用或文件改动，打开任务关联的 DotCraft 会话。取消正在进行的运行需要再确认一次。

## 审阅与决定

一个任务的所有内容都在同一页上从上往下读：Agent 的运行像 CI 检查一样列出，Agent 草稿以卡片呈现，决定和评论按发生顺序穿插其中。

- 任务就绪后，选择 **审阅**，批准、要求修改或拒绝。要求修改时需要写评论。
- 要关闭任务，打开左上角的状态菜单，选择 **归档**。已归档的任务也从这里重新打开。
- Pull request 在批准后又有新提交时，**审阅新版本** 会发起一次新的审阅。

![在 DotCraft Desktop 中审阅 Oratorio 任务](https://github.com/DotHarness/resources/raw/master/dotcraft/oratorio/task-review.png)

## 交付或继续推进

pull request 或 merge request 的审阅一结束，Oratorio 就把其中的 finding 和替换建议以仅评论的审阅形式发布到 GitHub 或 GitLab。它不会批准、要求修改或合并，任务仍然等你做决定。发布出去的 finding 在处理之前一直保持打开：在任务上选择 **标记为已修复** 或 **忽略**，需要再看一眼时选择 **重新打开**。审阅发布不出去时，比如写回没开，或者 pull request 又有了新提交，任务上会说明原因，你可以重试发布，也可以丢弃这次审阅。

启用 source write 后，Implementation Draft 可以把分支交付为 GitHub pull request 或 GitLab merge request。Follow-up Draft 可以先编辑，再变成新的本地任务。写回代码托管平台失败时，Oratorio 会保留已经记录的决策，并针对这次写入提供重试。

## 相关文档

- [接入 GitHub](./github) — 把 issue 与 pull request 同步进 Board，并把审阅结果写回去
- [接入 GitLab](./gitlab) — 用项目级 Token 接入 issue 与 merge request
- [配置 Oratorio](./settings) — 调整来源同步、Agent 执行与交付行为
