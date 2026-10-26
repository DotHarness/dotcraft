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

公开和私有仓库都支持。要接入更多仓库，重复上述步骤即可。

## 在 GitHub 里找 Oratorio

已接入仓库的 owner、成员和协作者可以在开放的 issue 或 pull request 中提问。打开[写回](./settings)，在新评论开头写上 `@dotcraft-ai`，后面跟上问题。

Oratorio 接下评论后会加一个 👀 表情，再在原处回复。不需要配置 Webhook，新评论通常在约 30 秒内被发现。接入前的评论不会被处理。

### 讨论 issue

要分析问题原因或讨论解决方案，在 issue 评论中发送：

```text
@dotcraft-ai What changes would address this issue?
```

Oratorio 会结合 issue 正文、已有讨论和仓库代码回答。提问不会启动代码修改或创建 pull request。

### 审阅和讨论 pull request

要审阅 pull request 的最新代码，在评论中发送：

```text
@dotcraft-ai review
```

想指定关注点，就用 `/review`，并在同一行写上关注点，例如 `@dotcraft-ai /review security regressions`。审阅命令只适用于 pull request。

要询问代码或审阅意见，直接写出问题：

```text
@dotcraft-ai Why does the refresh path keep the old token?
```

也可以在行内审阅评论和讨论串中提问，Oratorio 会在对应的讨论串里回复。提问不会发起新的审阅。

对某条审阅意见有异议时，在它的讨论串里写明理由并提问。如果讨论确认这条意见不成立或已经处理，Oratorio 会把它标记为忽略并解决讨论串。

## 启用 Webhook

Webhook 可以让同步和评论响应更快。本地 Desktop 通常收不到 GitHub 云端的 Webhook，无需配置。

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
