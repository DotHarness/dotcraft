# 配置 Oratorio

打开 Board 并选择 **Oratorio settings**，在这里管理来源连接、项目路由、Agent 执行和自动化。

![DotCraft Desktop 中的 Oratorio 设置](https://github.com/DotHarness/resources/raw/master/dotcraft/oratorio/settings-light.png)

## 来源与项目

GitHub 和 GitLab 的凭据分别在各自的提供商页面配置。仓库和项目要逐个添加，每个都映射到含有对应 checkout 的 DotCraft workspace。来源任务不会落到别的 workspace 上。

映射的 Workspace 离线或不再注册在 DotCraft 中时，绑定仍然显示，只是标记为不可用。把它重新绑定到一个已打开的本地 Workspace，或者移除这个项目。移除项目会停掉后续的同步、自动化和派发，已有的任务历史保留。不可用的绑定不会挡住其他设置的修改，Oratorio 会在报告状态或开始运行时重新检查。

提供商页面会显示读取、写入和 Webhook 的状态。选择 **立即同步** 立即同步一次，也可以设定同步计划。

## Agent 执行与 Worktree

Agent 怎么执行工作，从什么时候停下来等审批到最后怎么交付，都在 Oratorio 设置的主页面里配置。这些值在新运行开始时读取，所以改动只影响之后的运行。

托管 Worktree 默认建在仓库内：

```text
<repositoryWorkspace>/.craft/oratorio/worktrees
```

对应的分支以 `oratorio/run/` 开头。这些 Worktree 交给 Oratorio 自己清理，它按运行的实际占用关系回收，不会只看目录存在了多久。

## 保存与 Secret

设置在你停下操作后自动保存。字段会显示保存中或失败的状态，失败可以重试。同一份配置在别处被改动过时，Desktop 会重新加载服务器上已确认的值，不会显示一个未经确认的成功状态。

已保存的 Secret 只写不读，保存后不再显示明文。要换成新值就选 **替换密钥**，要清空就选 **清除密钥**，不做任何操作则保留原值。

部分运行时设置需要重启 Oratorio Server。保存之后，设置页会提示你。

Desktop 连接远程 [DotCraft Stack](../self-hosted/server-deployment) 时，管理类设置是只读的。Board 操作、来源同步和任务操作照常可用。

## 相关文档

- [接入 GitHub](./github) — 用 GitHub App 同步 issue 与 pull request
- [接入 GitLab](./gitlab) — 用项目级 Token 同步 issue 与 merge request
