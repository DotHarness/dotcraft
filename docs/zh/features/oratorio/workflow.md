# 使用 Oratorio 工作流

使用 Board 推动任务依次经过接收、Agent 工作、审阅和最终决策。

## 在 Board 中查找工作

**Active** 视图按生命周期阶段展示当前任务。使用 **All**、**Cancelled** 和 **Archived** 查看已完成或不再活跃的工作。

- 搜索标题和描述，或使用 `source:` 与 `label:` qualifier 缩小范围。
- 按仓库或 Assignee 筛选。
- 选择 **Sync sources** 立即请求一次 GitHub 与 GitLab 更新。
- 选择任务卡打开 Quick View，同时保留当前 Board 筛选条件。

Quick View 会显示当前状态、最近活动、草稿、评论，以及 Oratorio 当前允许执行的操作。

## 启动并跟踪 Agent 工作

打开一个待处理任务，从界面提供的运行模式中选择一个。可用选项取决于任务来源与当前状态。

每次运行都会使用 Oratorio 管理的 Worktree。需要查看完整对话、计划、工具活动或文件改动时，打开关联的 DotCraft task。取消活跃运行前需要确认。

## 审阅任务详情

Task Detail 按五个阶段组织完整流程：

1. **Intake** — 问题描述、来源元数据、标签、Assignee 与基础分支。
2. **Analysis** — 运行尝试、实时活动、Timeline、诊断与 Worktree 信息。
3. **Review** — Agent 草稿、finding、suggestion、评论、实现交付与 follow-up task。
4. **Decision** — 批准、要求修改或拒绝，以及代码托管平台写入状态。
5. **Closed** — 已记录的结果、历史，以及可用时的归档、重新打开和重新审阅操作。

![在 DotCraft Desktop 中审阅 Oratorio 任务](https://github.com/DotHarness/resources/raw/master/dotcraft/oratorio/task-review-light.png)

## 交付或继续工作

Review Draft 可以包含内联 finding 和替换建议。需要进一步讨论时，可在发布前 resolve 或 reopen finding。

启用 source write 后，Implementation Draft 可以把分支交付为 GitHub pull request 或 GitLab merge request。Follow-up Draft 可以编辑并创建为新的本地任务。如果写入代码托管平台失败，Oratorio 会保留已记录的决策，并提供针对该写入的重试操作。

命令成功后，Oratorio 会刷新任务与 Board。重新连接只会刷新状态，不会重复发送之前的用户操作。

## 相关文档

- [Oratorio](../oratorio)
- [接入 GitHub](./github)
- [接入 GitLab](./gitlab)
- [配置 Oratorio](./settings)

