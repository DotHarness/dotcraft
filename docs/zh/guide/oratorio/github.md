# 将 GitHub 接入 Oratorio

接入之后，GitHub 的 issue 和 pull request 会同步进 Oratorio 看板，Agent 的审阅意见、检查结果和实现分支也能写回 GitHub。连接靠一个 GitHub App 完成。

## 创建 GitHub App

在拥有这些仓库的用户或组织下创建一个 GitHub App，也可以复用已有的。只把它安装到 Oratorio 需要访问的仓库上。

按你打算启用的操作授予权限，用不到的就不给：

| 操作 | GitHub 权限 |
| --- | --- |
| **导入 issue 与 pull request** | Issues 与 pull requests：read |
| **读取文件与讨论** | Pull requests：read，Contents：read |
| **发布评论与审阅** | Issues 与 pull requests：write |
| **回应 `@dotcraft-ai` 评论** | Issues 与 pull requests：write |
| **发布审阅检查** | Checks：write |
| **交付 pull request** | Contents 与 pull requests：write |

再为这个 App 生成一个 private key。接下来配置 DotCraft 时要用到 App ID 和这把密钥。

## 接入仓库

1. 打开 Oratorio Board，选择 **Connect GitHub**。在 Oratorio 设置中选择 **Connect a source** 也会进入同一流程。
2. 填入 App ID 和 private key。GitHub.com 保持默认 endpoint，GitHub Enterprise 填写自己的 API endpoint。
3. 按 `owner/repository` 填写仓库。连接时 Oratorio 会自动检测 App 的 installation，只有检测失败时才需要手动填 Installation ID。
4. 选择持有这个仓库 checkout 的 DotCraft workspace。
5. 同步计划和自动 review 保持默认，写回先关着，等最初几次 review 没问题再打开。
6. 选择 **Connect and sync**。Oratorio 会保存配置，跑一次首次同步，并确认仓库可读。

要接入更多仓库，再走一遍这个流程即可。私有仓库不需要额外设置。Oratorio 用 App installation 凭据把审阅目标取回映射的 checkout，这个 checkout 本身不必保存 Git 凭据。

## 在 pull request 里找 Oratorio

已接入仓库的 owner、成员和协作者可以在任何开放的 pull request 上找 Oratorio。在 pull request 评论、行内审阅评论或审阅讨论串的回复开头写上 `@dotcraft-ai` 即可。

要审阅当前 head，发送：

```text
@dotcraft-ai review
```

想让这次审阅盯住某个方面，就用 `/review`，并在同一行写上关注点，例如 `@dotcraft-ai /review security regressions`。

提及之后写别的内容，就是向 Oratorio 提问：

```text
@dotcraft-ai Why does the refresh path keep the old token?
```

Oratorio 接下评论后会加一个 👀 表情，然后在你提问的地方回答：pull request 对话里的提问就回复在对话里，行内评论就回复在对应的审阅讨论串里。提问不会发起审阅，也不会改变任务状态。

对 Oratorio 的某条审阅意见有异议时，在这条意见的讨论串里回复 `@dotcraft-ai`。如果讨论下来这条意见不成立或者已经处理，Oratorio 会把它标记为忽略并解决这个讨论串，同时照常回复你。

不需要 Webhook。Oratorio 会在约 30 秒内发现新评论，接入仓库之前的评论不会被处理。打开写回后，表情和回复才会出现在 GitHub 上。

## 启用 Webhook

Webhook 是可选的。同步和 `@dotcraft-ai` 评论都不依赖它，Webhook 只是让事件更快到达 Oratorio。本地 Desktop 通常收不到 GitHub 云端的 Webhook。

如果你运行的是远程 [DotCraft Stack](../../self-hosting/server-deployment)，只公开受限的 Webhook endpoint：

```bash
dotcraft stack webhook enable \
  --dir /opt/dotcraft-stack \
  --public-host hooks.example.com
```

把命令输出的 endpoint 填成 GitHub App 的 Webhook URL，把生成的 secret 填进 App，保持 SSL verification 开启，然后订阅工作流用到的 issue comment、issue、pull request、review 和 review comment 事件。

## 相关文档

- [使用 Oratorio 工作流](./workflow) — 同步进来的任务在 Board 上怎么一步步推进
- [配置 Oratorio](./settings) — 调整审阅自动化、工作树和交付方式
