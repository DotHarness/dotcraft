# 将 GitLab 接入 Oratorio

接入之后，GitLab 的 issue 和 merge request 会同步进 Oratorio 看板，Agent 的审阅意见和实现分支也能写回 GitLab。连接靠一个项目级 Token 完成。

## 创建项目 Token

优先用 Project Access Token，把权限限制在单个项目内。按你打算启用的操作授予权限，用不到的就不给：

| 操作 | GitLab 权限 |
| --- | --- |
| **导入 issue 与 merge request** | Read API access |
| **读取仓库内容** | Repository read access |
| **发布 note 与审阅状态** | API access |
| **交付 merge request** | Repository write 与 API access |

每个接入的项目用自己的 profile 和 Token。

## 接入项目

1. 打开 Oratorio Board，选择 **Connect GitLab**。在 Oratorio 设置中选择 **Connect a source** 也会进入同一流程。
2. 选择 Token 类型并粘贴 Token。GitLab.com 保持默认 endpoint，自托管实例填写实例根地址。
3. 按 `group/project` 填写项目，subgroup 同样支持。
4. 选择持有这个项目 checkout 的 DotCraft workspace。
5. 同步计划和自动 review 保持默认，写回先关着，等最初几次 review 没问题再打开。
6. 选择 **Connect and sync**。Oratorio 会保存配置，跑一次首次同步，并确认项目可读。

要接入更多项目，再走一遍这个流程即可。私有项目不需要额外设置。Oratorio 用项目 Token 把审阅目标取回映射的 checkout，这个 checkout 本身不必保存 Git 凭据。

## 启用 Webhook

Webhook 不是必需的，但它能让来源的变化更快出现在看板上。把项目的 Webhook URL 设置为：

```text
https://your-oratorio-host/api/v1/sources/gitlab/webhook
```

在 GitLab project profile 里保存同一个 webhook secret 或 signing token，然后启用 issue、merge request 和 note 事件。除非你的[部署](../self-hosted/server-deployment)提供了带认证的 ingress 边界，否则请让这个 endpoint 保持私有。

本地 Desktop 通常收不到 GitLab 云端的 Webhook。没有可访问的 endpoint 时，用手动同步或定时同步。

## 相关文档

- [使用 Oratorio 工作流](./workflow) — 同步进来的任务在 Board 上怎么一步步推进
- [配置 Oratorio](./settings) — 调整审阅自动化、Worktree 和交付方式
