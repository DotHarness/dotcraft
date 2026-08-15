# 配置 Oratorio

打开 Board 并选择 **Oratorio settings**，即可管理来源连接、项目路由、Agent 执行与自动化。

![DotCraft Desktop 中的 Oratorio 设置](https://github.com/DotHarness/resources/raw/master/dotcraft/oratorio/settings-light.png)

## Provider 与项目

在对应的 Provider 页面配置 GitHub 与 GitLab 凭据。分别添加每个仓库或项目，然后将其映射到包含匹配 checkout 的 DotCraft workspace。对于来源任务，Oratorio 不会猜测备用 workspace。

Provider 页面会显示 read、write 与 Webhook 健康状态。使用 **Sync now** 立即更新，或设置定时同步。只有来源需要完整重新对账时才使用 full repair。

## Agent 执行与 Worktree

根页面用于控制 approval policy、运行超时、托管 Worktree 的位置与分支命名、自动派发、审阅自动化和交付行为。运行时并发、重试、停滞和清理策略继续由 Server 管理，不在 Desktop 中暴露。

托管 Worktree 默认使用仓库内的目录：

```text
<repositoryWorkspace>/.craft/oratorio/worktrees
```

托管分支使用 `oratorio/run/<work-item-key>`。应当由 Oratorio 清理自己的 Worktree。清理操作会检查持久化的运行占用关系，而不是仅按时间删除目录。

## 保存与 Secret

设置会在短暂延迟后保存。字段会显示 pending 或失败状态，保存失败后可以重试。如果另一个编辑器修改了同一 revision，Desktop 会重新加载 Server 已确认的配置，而不会展示未经确认的本地成功状态。

已保存的 Secret 只可写入，无法读取明文。Secret 编辑器提供三个明确选择：

- **Keep**：保留当前值。
- **Replace**：替换为新值。
- **Clear**：清除当前值。

部分运行时设置需要重启 Oratorio Server。配置保存后，Settings 会显示该状态。

Desktop 连接远程 DotCraft Stack 时，管理设置为只读，但 Board 操作、来源同步与任务操作仍然可用。

## 相关文档

- [Oratorio](../oratorio)
- [接入 GitHub](./github)
- [接入 GitLab](./gitlab)
- [部署 DotCraft Stack](../self-hosted/server-deployment)
