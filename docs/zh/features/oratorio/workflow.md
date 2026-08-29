# Oratorio 工作流

[Oratorio](../oratorio) Board 上的每个任务都沿同一条路径推进：接收、交给 Agent、审阅，最后记录一个决策。

## 在 Board 上找到任务

**Active** 视图按生命周期阶段展示进行中的任务。**All**、**Cancelled** 和 **Archived** 用来翻已完成或不再活跃的工作。

- 搜索标题和描述，或者用 `source:` 与 `label:` 缩小范围。
- 按仓库或 Assignee 筛选。
- 选择 **Sync sources**，立刻向 GitHub 和 GitLab 请求一次更新。
- 选择任务卡打开 Quick View，当前的 Board 筛选条件不会丢。

Quick View 显示任务当前的状态、最近活动、草稿和评论，以及此刻允许执行的操作。

## 启动并跟踪 Agent 工作

打开一个待处理的任务，选择一种运行方式。可选项取决于任务来源和它当前所处的阶段。

每次运行都在 Oratorio 管理的 Worktree 里进行。想看完整对话、计划、工具调用或文件改动，打开任务关联的 DotCraft 会话。取消正在进行的运行需要再确认一次。

## 任务详情的五个阶段

打开任务的完整详情，整个流程按五个阶段展开：

1. **Intake** — 问题描述、来源信息、标签、Assignee 与基础分支。
2. **Analysis** — 运行尝试、实时活动、Timeline 与诊断信息。
3. **Review** — Agent 给出的草稿、finding 与修改建议。
4. **Decision** — 批准、要求修改或拒绝，以及写回代码托管平台的结果。
5. **Closed** — 已记录的结果和历史，需要时可以归档、重新打开或再审一次。

![在 DotCraft Desktop 中审阅 Oratorio 任务](https://github.com/DotHarness/resources/raw/master/dotcraft/oratorio/task-review-light.png)

## 交付或继续推进

Review Draft 里可以写内联 finding，也可以直接给出替换建议。还需要讨论的部分，在发布前 resolve 或 reopen 对应的 finding。

启用 source write 后，Implementation Draft 可以把分支交付为 GitHub pull request 或 GitLab merge request。Follow-up Draft 可以先编辑，再变成新的本地任务。写回代码托管平台失败时，Oratorio 会保留已经记录的决策，并针对这次写入提供重试。

## 相关文档

- [接入 GitHub](./github) — 把 issue 与 pull request 同步进 Board，并把审阅结果写回去
- [接入 GitLab](./gitlab) — 用项目级 Token 接入 issue 与 merge request
- [配置 Oratorio](./settings) — 调整来源同步、Agent 执行与交付行为
